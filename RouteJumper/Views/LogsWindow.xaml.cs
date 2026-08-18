using System.IO;
using System.Windows;
using System.Windows.Controls;
using RouteJumper.Common;
using RouteJumper.Services;
using RouteJumper.Services.Logging;

namespace RouteJumper.Views
{
    /// <summary>
    /// Non-modal live log viewer (Help &gt; Logs, SPEC's Logging section). Opens empty and only
    /// ever shows entries logged while it's actually open - the on-disk file (FileLogSink) is the
    /// durable record; this window is a live, in-memory view only, and retains nothing once
    /// closed (a fresh open starts blank again, even for the same session). Log.EntryLogged can
    /// fire from any thread (background journal watchers, HTTP calls, ...), so every append is
    /// marshalled onto this window's own Dispatcher.
    /// </summary>
    public partial class LogsWindow : Window
    {
        private const int MaxLines = 5000;
        private const int TrimBatch = 500;

        private int _lineCount;

        public LogsWindow()
        {
            InitializeComponent();
            Log.EntryLogged += OnEntryLogged;
            Closed += (_, _) => Log.EntryLogged -= OnEntryLogged;
        }

        private void OnEntryLogged(object? sender, LogEntry entry) =>
            Dispatcher.BeginInvoke(() => AppendLine(entry.Format()));

        /// <summary>
        /// Trims in batches (not one line at a time) once the buffer grows past MaxLines - a
        /// per-line trim would mean re-slicing the whole TextBox's text on every single append
        /// forever once the cap is first reached, which scales badly over a long-running session;
        /// batching amortizes that cost.
        /// </summary>
        private void AppendLine(string line)
        {
            LogTextBox.AppendText(line + Environment.NewLine);
            _lineCount++;

            if (_lineCount > MaxLines + TrimBatch)
            {
                var text = LogTextBox.Text;
                var index = -1;
                for (var i = 0; i < TrimBatch; i++)
                {
                    index = text.IndexOf('\n', index + 1);
                    if (index < 0)
                    {
                        break;
                    }
                }

                if (index >= 0)
                {
                    LogTextBox.Text = text[(index + 1)..];
                    _lineCount -= TrimBatch;
                }
            }

            LogTextBox.ScrollToEnd();
        }

        /// <summary>Opens the on-disk Logs folder in Explorer - the durable record for anything that happened before this window was opened, since the view above only ever shows entries logged while it's open.</summary>
        private void OnOpenLogsFolderClick(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            BrowserLauncher.Open(AppPaths.LogsDirectory);
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            _lineCount = 0;
        }

        /// <summary>
        /// Wrapped: long lines wrap within the window's own width instead of running off the
        /// right edge - the horizontal scrollbar is switched off too, since there's nothing left
        /// for it to scroll. Unwrapped (the default) restores the original "one line per log
        /// entry, scroll right to see the rest" behaviour.
        /// </summary>
        private void OnWrapTextChanged(object sender, RoutedEventArgs e)
        {
            var wrap = WrapTextCheckBox.IsChecked == true;
            LogTextBox.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            LogTextBox.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
