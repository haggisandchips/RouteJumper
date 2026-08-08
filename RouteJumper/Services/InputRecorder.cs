using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RouteJumper.Services
{
    /// <summary>
    /// Captures keyboard and mouse input directed at one specific target window while recording
    /// is active (SPEC §6.3), via low-level, system-wide Win32 hooks (WH_KEYBOARD_LL/
    /// WH_MOUSE_LL) - the only way to see input aimed at a window that isn't this app's own,
    /// since a normal WPF input event only ever fires for this app's own windows. Every captured
    /// event is filtered to "was the target window the foreground window at that instant" before
    /// being recorded at all, so input directed elsewhere (including at this app itself) is
    /// never captured.
    ///
    /// A key held down for at least <see cref="HoldThresholdMs"/> before being released is
    /// recorded as a Hold (with its actual duration); anything shorter is a Tap. A gap of at
    /// least <see cref="WaitThresholdMs"/> between two captured events is recorded as an
    /// explicit Wait step, so playback reproduces roughly the same pacing it was recorded at -
    /// short, incidental gaps are not, to avoid cluttering the script with near-zero waits.
    ///
    /// Each captured key is translated to a script token via <paramref name="resolveToken"/> -
    /// the SPEC §6.1 action name if it matches one of the current key bindings, otherwise a
    /// literal "KEY &lt;storage string&gt;" - at the moment it's captured, not retroactively, so
    /// a later rebind doesn't change what an already-recorded macro says.
    ///
    /// The hook callbacks are Windows' own delivery mechanism for these events and must return
    /// essentially instantly and never throw - both callbacks are wrapped defensively and do
    /// only fast, allocation-light work (a foreground-window check, a dictionary lookup, a list
    /// append) before returning control to <see cref="CallNextHookEx"/>. Requires a message pump
    /// on the thread that starts recording (true for any WPF UI-thread call, which is the only
    /// place this is ever used from).
    /// </summary>
    public sealed class InputRecorder : IDisposable
    {
        private const int HoldThresholdMs = 400;
        private const int WaitThresholdMs = 150;
        private const int SW_RESTORE = 9;

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;

        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private readonly IntPtr _targetWindow;
        private readonly Func<Key, ModifierKeys, string> _resolveToken;
        private readonly List<MacroInstruction> _steps = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly Dictionary<int, long> _keyDownAtMs = new();
        private readonly HashSet<int> _heldModifierVks = new();

        // Kept as fields, not locals - a hook delegate must stay alive (not GC'd) for the whole
        // time its hook is installed, the same reason CarrierRouteJournalWatcher keeps its
        // Timer references as fields rather than locals.
        private HookProc? _keyboardProc;
        private HookProc? _mouseProc;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private long _lastEventAtMs;
        private bool _disposed;

        public InputRecorder(IntPtr targetWindow, Func<Key, ModifierKeys, string> resolveToken)
        {
            _targetWindow = targetWindow;
            _resolveToken = resolveToken;
        }

        public void Start()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                return;
            }

            // Every captured event is filtered to "target window is foreground" (see the class
            // doc comment), so without this, recording would silently capture nothing at all
            // until the user manually alt-tabbed to the target themselves - the same reason
            // MacroPlayer.PlayAsync foregrounds its target before sending any input.
            NativeMethods.ShowWindow(_targetWindow, SW_RESTORE);
            NativeMethods.SetForegroundWindow(_targetWindow);

            _stopwatch.Restart();
            _lastEventAtMs = 0;

            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = module is null ? IntPtr.Zero : NativeMethods.GetModuleHandle(module.ModuleName);

            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
            _keyboardHookId = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            _mouseHookId = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }

        /// <summary>Unhooks and returns everything captured since <see cref="Start"/>.</summary>
        public IReadOnlyList<MacroInstruction> Stop()
        {
            Unhook();
            return _steps;
        }

        private void Unhook()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }

            if (_mouseHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            _keyboardProc = null;
            _mouseProc = null;
        }

        private bool IsTargetForeground() => NativeMethods.GetForegroundWindow() == _targetWindow;

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var vk = (int)data.vkCode;
                    var msg = wParam.ToInt32();

                    if (IsModifierVk(vk))
                    {
                        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                        {
                            _heldModifierVks.Add(vk);
                        }
                        else if (msg is WM_KEYUP or WM_SYSKEYUP)
                        {
                            _heldModifierVks.Remove(vk);
                        }
                    }
                    else if (IsTargetForeground())
                    {
                        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                        {
                            _keyDownAtMs[vk] = _stopwatch.ElapsedMilliseconds;
                        }
                        else if (msg is WM_KEYUP or WM_SYSKEYUP && _keyDownAtMs.TryGetValue(vk, out var downAt))
                        {
                            _keyDownAtMs.Remove(vk);
                            RecordKeyRelease(vk, _stopwatch.ElapsedMilliseconds - downAt);
                        }
                    }
                }
            }
            catch
            {
                // A hook callback must never let an exception escape - see the class doc comment.
            }

            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN && IsTargetForeground())
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var point = data.pt;
                    if (NativeMethods.ScreenToClient(_targetWindow, ref point))
                    {
                        AddStep(new MacroInstruction.Click(point.X, point.Y));
                    }
                }
            }
            catch
            {
            }

            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void RecordKeyRelease(int vk, long durationMs)
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            var modifiers = CurrentModifiers();
            var token = _resolveToken(key, modifiers);

            AddStep(durationMs >= HoldThresholdMs
                ? new MacroInstruction.Hold(token, (int)durationMs)
                : new MacroInstruction.Tap(token));
        }

        private ModifierKeys CurrentModifiers()
        {
            var modifiers = ModifierKeys.None;
            if (_heldModifierVks.Contains(VK_LCONTROL) || _heldModifierVks.Contains(VK_RCONTROL))
            {
                modifiers |= ModifierKeys.Control;
            }

            if (_heldModifierVks.Contains(VK_LSHIFT) || _heldModifierVks.Contains(VK_RSHIFT))
            {
                modifiers |= ModifierKeys.Shift;
            }

            if (_heldModifierVks.Contains(VK_LMENU) || _heldModifierVks.Contains(VK_RMENU))
            {
                modifiers |= ModifierKeys.Alt;
            }

            if (_heldModifierVks.Contains(VK_LWIN) || _heldModifierVks.Contains(VK_RWIN))
            {
                modifiers |= ModifierKeys.Windows;
            }

            return modifiers;
        }

        private static bool IsModifierVk(int vk) =>
            vk is VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

        private void AddStep(MacroInstruction step)
        {
            var now = _stopwatch.ElapsedMilliseconds;
            if (_steps.Count > 0)
            {
                var gap = now - _lastEventAtMs;
                if (gap >= WaitThresholdMs)
                {
                    _steps.Add(new MacroInstruction.Wait((int)gap));
                }
            }

            _steps.Add(step);
            _lastEventAtMs = now;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unhook();
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string? lpModuleName);
        }
    }
}
