using RouteJumper.Common;
using RouteJumper.Models;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Represents one row of the Route table: Icon | # | System | Status.
    /// </summary>
    public class RouteRowViewModel : ObservableObject
    {
        private RowIcon _icon = RowIcon.None;
        private int _number;
        private string _systemText = string.Empty;
        private string _status = string.Empty;
        private bool _isCopiedToClipboard;

        public RowIcon Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value);
        }

        /// <summary>Sequential row number ("#" column), starting at 1.</summary>
        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        /// <summary>The original line of text ("System" column).</summary>
        public string SystemText
        {
            get => _systemText;
            set => SetProperty(ref _systemText, value);
        }

        /// <summary>Current action status ("Status" column), e.g. "Plotted", "Jumping", "Cooldown".</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// True while this row's System text is believed to be the current contents of the
        /// system clipboard - drives a small icon shown after the System text. Set by
        /// RouteViewModel whenever it copies this row's text; cleared either when
        /// a different row is copied instead, or when the clipboard changes for any other
        /// reason (this app or an external one) - see RouteViewModel.OnSystemClipboardChanged.
        /// </summary>
        public bool IsCopiedToClipboard
        {
            get => _isCopiedToClipboard;
            set => SetProperty(ref _isCopiedToClipboard, value);
        }
    }
}
