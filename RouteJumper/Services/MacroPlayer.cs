using System.Runtime.InteropServices;
using System.Windows.Input;
using RouteJumper.Models;

namespace RouteJumper.Services
{
    /// <summary>
    /// Replays a recorded macro script (SPEC §6.3) against one target window: brings it to the
    /// foreground, then sends real keyboard/mouse input via <c>SendInput</c> - the same
    /// injection API countless legitimate remote-control, accessibility, and macro tools use;
    /// posting messages directly to the window (<c>PostMessage</c>) is not reliable for a
    /// DirectX game like Elite Dangerous, which reads real input state rather than the Windows
    /// message queue.
    ///
    /// An ACTION_NAME token is resolved to a key via <paramref name="resolveBinding"/> at the
    /// moment it's played, not at record time, so a script keeps working correctly if the user
    /// rebinds a key after recording it. A "KEY &lt;storage&gt;" token always sends that literal
    /// key regardless of any binding. An unresolvable token (e.g. a typo from hand-editing, or a
    /// CALL to a macro that doesn't exist) is silently skipped - consistent with
    /// MacroScriptParser's own permissive philosophy - rather than aborting the whole script.
    ///
    /// A Paste step's text can contain the literal placeholder "{NEXT_SYSTEM}", substituted via
    /// <paramref name="getNextSystemName"/> at the moment it's played - not at record time, the
    /// same "resolved late" principle ACTION_NAME tokens already follow - since the whole point
    /// is to paste whatever the Route tab's next system *currently* is, not whatever it happened
    /// to be when the macro was recorded. If nothing is available (e.g. no route saved) the
    /// placeholder resolves to empty text rather than pasting the literal placeholder text.
    ///
    /// A Click/HoldClick's X and/or Y may likewise be the literal placeholder "{CENTRE}" instead
    /// of a number, resolved at the moment it's played against the target window's *current*
    /// client-area size (via GetClientRect) - the window may have been resized since the macro
    /// was recorded, so this is deliberately not baked in at record or parse time either.
    /// </summary>
    public sealed class MacroPlayer
    {
        private const int MaxCallDepth = 50;
        private const string NextSystemPlaceholder = "{NEXT_SYSTEM}";
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0x00;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const int FocusWatchdogIntervalMs = 250;

        private readonly IntPtr _targetWindow;
        private readonly Func<ControlAction, string?> _resolveBinding;
        private readonly Func<string?> _getNextSystemName;
        private readonly int _autoWaitMs;

        /// <summary>
        /// <paramref name="autoWaitMs"/> (Controls tab Options §6.1) is applied automatically by
        /// <see cref="RunAsync"/> after every leaf instruction it executes during a full
        /// <see cref="PlayAsync"/> run - lets a script rely on consistent built-in pacing between
        /// actions instead of needing an explicit WAIT after every single one, applied uniformly
        /// regardless of how playback was started (manual Play, Auto Pilot, or - via a separate,
        /// equivalent mechanism in ControlsViewModel.RunStepAsync, since Step executes one
        /// instruction per call rather than a whole script - the macro editor's Step button).
        /// Skipped only when the very next instruction is itself a WAIT, so the two delays never
        /// stack redundantly back-to-back with nothing happening in between.
        /// </summary>
        public MacroPlayer(
            IntPtr targetWindow,
            Func<ControlAction, string?> resolveBinding,
            Func<string?> getNextSystemName,
            int autoWaitMs = 0)
        {
            _targetWindow = targetWindow;
            _resolveBinding = resolveBinding;
            _getNextSystemName = getNextSystemName;
            _autoWaitMs = autoWaitMs;
        }

        /// <summary>
        /// Plays the script, aborting early with a <see cref="PlaybackAbortedException"/> if the
        /// target window ever loses focus while playing - SendInput delivers to whichever window
        /// currently has focus, not to a specific HWND, so continuing after focus has moved
        /// elsewhere would send the rest of the script into the wrong window instead of just
        /// silently failing.
        /// </summary>
        public async Task PlayAsync(string scriptText, CancellationToken cancellationToken)
        {
            var parsed = MacroScriptParser.Parse(scriptText);

            Win32Foreground.ForceForegroundWindow(_targetWindow);
            await Task.Delay(200, cancellationToken); // give the window a moment to actually gain focus

            // A separate linked token lets the focus watchdog abort playback on its own, without
            // the caller's own token (a real user Stop) appearing to have fired - RunAsync only
            // ever sees one token and can't tell the two apart itself, so that distinction has to
            // be made here, after the fact, by checking which token was actually the cause.
            using var focusLossCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var watchdog = MonitorFocusAsync(focusLossCts);

            try
            {
                await RunAsync(parsed.MainSteps, parsed.Macros, 0, focusLossCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PlaybackAbortedException("Playback aborted - the target window lost focus.");
            }
            finally
            {
                focusLossCts.Cancel();
                await watchdog;
            }
        }

        private async Task MonitorFocusAsync(CancellationTokenSource focusLossCts)
        {
            try
            {
                while (!focusLossCts.IsCancellationRequested)
                {
                    await Task.Delay(FocusWatchdogIntervalMs, focusLossCts.Token);
                    if (NativeMethods.GetForegroundWindow() != _targetWindow)
                    {
                        focusLossCts.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected once PlayAsync's finally block cancels this token after playback ends
                // normally (or was Stopped by the user) - nothing to do.
            }
        }

        private async Task RunAsync(
            IReadOnlyList<MacroInstruction> steps,
            IReadOnlyDictionary<string, IReadOnlyList<MacroInstruction>> macros,
            int callDepth,
            CancellationToken cancellationToken)
        {
            if (callDepth > MaxCallDepth)
            {
                return;
            }

            for (var i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[i];

                switch (step)
                {
                    case MacroInstruction.Repeat repeat:
                        for (var r = 0; r < repeat.Count; r++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await RunAsync(repeat.Body, macros, callDepth + 1, cancellationToken);
                        }
                        break;

                    case MacroInstruction.Call call:
                        if (macros.TryGetValue(call.MacroName, out var body))
                        {
                            await RunAsync(body, macros, callDepth + 1, cancellationToken);
                        }
                        break;

                    default:
                        await ExecuteLeafAsync(step, cancellationToken);

                        // Auto-pace after every leaf instruction, unless the next one (at this
                        // same nesting level) is itself a WAIT - its own duration already
                        // provides the pause, so stacking ours in front of it would just be a
                        // redundant extra delay. Reaching the end of a nested REPEAT/CALL body
                        // with nothing left to peek at here defaults to pacing anyway (rather
                        // than trying to look across the recursion boundary into whatever the
                        // caller's own next step is) - a harmless, at-worst-slightly-redundant
                        // wait beats the complexity of tracking that precisely.
                        var nextIsWait = i + 1 < steps.Count && steps[i + 1] is MacroInstruction.Wait;
                        if (!nextIsWait && _autoWaitMs > 0)
                        {
                            await Task.Delay(_autoWaitMs, cancellationToken);
                        }
                        break;
                }
            }
        }

        /// <summary>Executes one non-structural instruction - everything RunAsync's switch doesn't itself recurse into (Repeat/Call) - shared by normal playback and single-step execution (see RunSingleStepAsync/Flatten).</summary>
        private async Task ExecuteLeafAsync(MacroInstruction step, CancellationToken cancellationToken)
        {
            switch (step)
            {
                case MacroInstruction.Tap tap:
                    await SendTokenAsync(tap.Token, holdMs: null, cancellationToken);
                    break;

                case MacroInstruction.Hold hold:
                    await SendTokenAsync(hold.Token, hold.DurationMs, cancellationToken);
                    break;

                case MacroInstruction.Click click:
                    SendClick(click.X, click.Y);
                    break;

                case MacroInstruction.HoldClick holdClick:
                    await SendHoldClickAsync(holdClick.X, holdClick.Y, holdClick.DurationMs, cancellationToken);
                    break;

                case MacroInstruction.Wait wait:
                    await Task.Delay(wait.DurationMs, cancellationToken);
                    break;

                case MacroInstruction.Paste paste:
                    await SendPasteAsync(ResolvePasteText(paste.Text), cancellationToken);
                    break;
            }
        }

        /// <summary>
        /// Parses and flattens a script into just its leaf (non-structural) instructions, in
        /// execution order - a REPEAT block is unrolled Count times and a CALL is inlined with
        /// the referenced macro's own leaves (recursively, to the same MaxCallDepth PlayAsync
        /// itself enforces; an unresolvable CALL target contributes nothing, same as PlayAsync).
        /// WAIT steps are dropped entirely - stepping is a manual, one-command-at-a-time debugging
        /// aid (SPEC §6.5), so there's nothing useful for the user to observe from a step that's
        /// pure delay, and it would otherwise cost them an extra Step click for no visible effect.
        /// Used by the Controls tab editor's single-step facility, which needs a concrete,
        /// indexable sequence to step through one instruction at a time rather than PlayAsync's
        /// own recursive, run-to-completion walk of the parsed tree.
        /// </summary>
        public static IReadOnlyList<MacroInstruction> Flatten(string scriptText)
        {
            var parsed = MacroScriptParser.Parse(scriptText);
            var result = new List<MacroInstruction>();
            FlattenSteps(parsed.MainSteps, parsed.Macros, 0, result);
            return result;
        }

        private static void FlattenSteps(
            IReadOnlyList<MacroInstruction> steps,
            IReadOnlyDictionary<string, IReadOnlyList<MacroInstruction>> macros,
            int callDepth,
            List<MacroInstruction> result)
        {
            if (callDepth > MaxCallDepth)
            {
                return;
            }

            foreach (var step in steps)
            {
                switch (step)
                {
                    case MacroInstruction.Repeat repeat:
                        for (var i = 0; i < repeat.Count; i++)
                        {
                            FlattenSteps(repeat.Body, macros, callDepth + 1, result);
                        }
                        break;

                    case MacroInstruction.Call call:
                        if (macros.TryGetValue(call.MacroName, out var body))
                        {
                            FlattenSteps(body, macros, callDepth + 1, result);
                        }
                        break;

                    case MacroInstruction.Wait:
                        break;

                    default:
                        result.Add(step);
                        break;
                }
            }
        }

        /// <summary>
        /// Executes exactly one leaf instruction (from <see cref="Flatten"/>) against the target
        /// window, foregrounding it fresh first - unlike PlayAsync's one-time foreground at the
        /// start of a whole script, a manual single step (SPEC §6.5's editor "Step" facility)
        /// needs this every call, since the user's own click on RouteJumper's own Step button
        /// will itself have taken focus away from the target in between steps. Deliberately no
        /// focus-loss watchdog here (unlike PlayAsync) - a single step is short enough that
        /// losing focus mid-instruction isn't a realistic concern the way it is across a whole
        /// script.
        /// </summary>
        public async Task RunSingleStepAsync(MacroInstruction step, CancellationToken cancellationToken)
        {
            Win32Foreground.ForceForegroundWindow(_targetWindow);
            await Task.Delay(200, cancellationToken);
            await ExecuteLeafAsync(step, cancellationToken);
        }

        /// <summary>
        /// Resolves a token to a (Key, Modifiers) pair - via the action's *current* binding if
        /// the token is an ACTION_NAME, or parsed directly if it's "KEY &lt;storage&gt;" - and
        /// sends it: modifiers down, key down, wait the full requested duration (or a short
        /// realistic default for a plain tap - a true zero-length down+up can be unreliable for
        /// a game to register as a real keypress at all), then key up, modifiers up.
        /// </summary>
        private async Task SendTokenAsync(string token, int? holdMs, CancellationToken cancellationToken)
        {
            if (!TryResolveToken(token, out var key, out var modifiers))
            {
                return;
            }

            var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            var modifierVks = ModifierVirtualKeys(modifiers);

            SendKeyEvents(modifierVks, vk, keyUp: false);
            await Task.Delay(holdMs ?? 30, cancellationToken);
            SendKeyEvents(modifierVks, vk, keyUp: true);
        }

        private bool TryResolveToken(string token, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            if (token.StartsWith("KEY ", StringComparison.OrdinalIgnoreCase))
            {
                return KeyBindingFormatter.TryParse(token[4..].Trim(), out key, out modifiers);
            }

            if (ControlActionExtensions.TryParseActionName(token, out var action))
            {
                var binding = _resolveBinding(action);
                return binding != null && KeyBindingFormatter.TryParse(binding, out key, out modifiers);
            }

            return false;
        }

        private static IReadOnlyList<ushort> ModifierVirtualKeys(ModifierKeys modifiers)
        {
            var list = new List<ushort>();
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                list.Add((ushort)KeyInterop.VirtualKeyFromKey(Key.LeftCtrl));
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                list.Add((ushort)KeyInterop.VirtualKeyFromKey(Key.LeftShift));
            }

            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                list.Add((ushort)KeyInterop.VirtualKeyFromKey(Key.LeftAlt));
            }

            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                list.Add((ushort)KeyInterop.VirtualKeyFromKey(Key.LWin));
            }

            return list;
        }

        private static void SendKeyEvents(IReadOnlyList<ushort> modifierVks, ushort vk, bool keyUp)
        {
            // Modifiers go down before, and up after, the main key - whether pressing or
            // releasing follows the same order either way here since callers invoke this once
            // for the down half and once for the up half, not both in one call.
            var inputs = new List<NativeMethods.INPUT>();
            if (!keyUp)
            {
                foreach (var mod in modifierVks)
                {
                    inputs.Add(KeyInput(mod, false));
                }

                inputs.Add(KeyInput(vk, false));
            }
            else
            {
                inputs.Add(KeyInput(vk, true));
                for (var i = modifierVks.Count - 1; i >= 0; i--)
                {
                    inputs.Add(KeyInput(modifierVks[i], true));
                }
            }

            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
        }

        /// <summary>
        /// A synthesized event carrying only a virtual-key code (no scan code) is silently
        /// ignored by many DirectInput/raw-input-reading games, Elite Dangerous included - they
        /// read the physical scan code, not the translated VK, the same reason this class exists
        /// at all rather than posting WM_KEYDOWN messages. wScan and KEYEVENTF_EXTENDEDKEY (for
        /// keys like the arrow keys and Insert/Delete/Home/End, which share a base scan code with
        /// their numpad equivalents and are only told apart by that flag) make the synthesized
        /// event indistinguishable from a real keypress at the scan-code level too.
        /// </summary>
        private static NativeMethods.INPUT KeyInput(ushort vk, bool keyUp)
        {
            var flags = keyUp ? KEYEVENTF_KEYUP : 0u;
            if (IsExtendedKey(vk))
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            return new NativeMethods.INPUT
            {
                type = INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = (ushort)NativeMethods.MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                        dwFlags = flags
                    }
                }
            };
        }

        /// <summary>
        /// Keys that share a base scan code with a numpad equivalent (or otherwise require the
        /// extended-key flag to be identified correctly) - notably the dedicated arrow keys,
        /// which several of this app's default action bindings (UP/DOWN/LEFT/RIGHT) use directly.
        /// </summary>
        private static bool IsExtendedKey(ushort vk) => vk switch
        {
            0xA3 => true, // VK_RCONTROL
            0xA5 => true, // VK_RMENU
            0x2D => true, // VK_INSERT
            0x2E => true, // VK_DELETE
            0x24 => true, // VK_HOME
            0x23 => true, // VK_END
            0x21 => true, // VK_PRIOR (Page Up)
            0x22 => true, // VK_NEXT (Page Down)
            0x26 => true, // VK_UP
            0x28 => true, // VK_DOWN
            0x25 => true, // VK_LEFT
            0x27 => true, // VK_RIGHT
            0x90 => true, // VK_NUMLOCK
            0x2C => true, // VK_SNAPSHOT (Print Screen)
            0x6F => true, // VK_DIVIDE (numpad /)
            0x5B => true, // VK_LWIN
            0x5C => true, // VK_RWIN
            0x5D => true, // VK_APPS
            _ => false
        };

        /// <summary>
        /// Moves the cursor to the given position (relative to the target window's client area,
        /// recomputed against its *current* screen position and size - the window may have moved
        /// or been resized since the macro was recorded) and clicks.
        /// </summary>
        private void SendClick(string xToken, string yToken)
        {
            if (!TryResolveClickPosition(xToken, yToken, out var x, out var y) || !TryMoveCursorTo(x, y))
            {
                return;
            }

            SendMouseButtonEvent(down: true);
            SendMouseButtonEvent(down: false);
        }

        /// <summary>Like SendClick, but holds the button down for durationMs before releasing.</summary>
        private async Task SendHoldClickAsync(string xToken, string yToken, int durationMs, CancellationToken cancellationToken)
        {
            if (!TryResolveClickPosition(xToken, yToken, out var x, out var y) || !TryMoveCursorTo(x, y))
            {
                return;
            }

            SendMouseButtonEvent(down: true);
            await Task.Delay(durationMs, cancellationToken);
            SendMouseButtonEvent(down: false);
        }

        /// <summary>
        /// Resolves a Click/HoldClick's raw X/Y tokens (each either an integer or the literal
        /// placeholder "{CENTRE}") against the target window's current client-area size.
        /// </summary>
        private bool TryResolveClickPosition(string xToken, string yToken, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (!NativeMethods.GetClientRect(_targetWindow, out var clientRect))
            {
                return false;
            }

            return TryResolveCoordinate(xToken, clientRect.Right - clientRect.Left, out x) &&
                   TryResolveCoordinate(yToken, clientRect.Bottom - clientRect.Top, out y);
        }

        private static bool TryResolveCoordinate(string token, int extent, out int value)
        {
            if (token.Equals("{CENTRE}", StringComparison.OrdinalIgnoreCase))
            {
                value = extent / 2;
                return true;
            }

            return int.TryParse(token, out value);
        }

        private bool TryMoveCursorTo(int x, int y)
        {
            var point = new NativeMethods.POINT { X = x, Y = y };
            if (!NativeMethods.ClientToScreen(_targetWindow, ref point))
            {
                return false;
            }

            NativeMethods.SetCursorPos(point.X, point.Y);
            return true;
        }

        private static void SendMouseButtonEvent(bool down)
        {
            var input = new NativeMethods.INPUT
            {
                type = INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP }
                }
            };

            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private string ResolvePasteText(string text) =>
            text.Contains(NextSystemPlaceholder, StringComparison.Ordinal)
                ? text.Replace(NextSystemPlaceholder, _getNextSystemName() ?? string.Empty, StringComparison.Ordinal)
                : text;

        /// <summary>
        /// Sets the clipboard to <paramref name="text"/> and sends Ctrl+V - a short delay after
        /// the clipboard write gives it a moment to actually land before the paste keystroke,
        /// same reasoning as the 30ms default tap duration in <see cref="SendTokenAsync"/>.
        /// </summary>
        private async Task SendPasteAsync(string text, CancellationToken cancellationToken)
        {
            if (text.Length == 0)
            {
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The clipboard is a shared, sometimes-briefly-locked system resource (another
                // app can be mid-access) - skip this paste rather than crash the whole playback.
                return;
            }

            await Task.Delay(30, cancellationToken);

            var ctrlVk = (ushort)KeyInterop.VirtualKeyFromKey(Key.LeftCtrl);
            var vVk = (ushort)KeyInterop.VirtualKeyFromKey(Key.V);
            SendKeyEvents(new[] { ctrlVk }, vVk, keyUp: false);
            await Task.Delay(30, cancellationToken);
            SendKeyEvents(new[] { ctrlVk }, vVk, keyUp: true);
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint MapVirtualKey(uint uCode, uint uMapType);

            [DllImport("user32.dll")]
            public static extern bool SetCursorPos(int x, int y);

            [DllImport("user32.dll")]
            public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X, Y;
            }

            /// <summary>Left/Top are always 0 for GetClientRect (it's relative to the window's own client area) - Right/Bottom are the client area's width/height.</summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left, Top, Right, Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct INPUT
            {
                public int type;
                public InputUnion U;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct InputUnion
            {
                [FieldOffset(0)] public MOUSEINPUT mi;
                [FieldOffset(0)] public KEYBDINPUT ki;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }
        }
    }
}
