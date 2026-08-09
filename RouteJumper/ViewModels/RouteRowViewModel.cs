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
        private DateTime? _phaseEndUtc;
        private DateTime? _phaseStartUtc;
        private double _progress;

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

        /// <summary>
        /// The real-world UTC instant the current Status (Plotted/Jumping/Cooldown) will itself
        /// end - set by RouteSequencer from RowEvent.PhaseEndUtc alongside Status, and cleared
        /// (null) whenever Status is cleared or set to something with no known end. Setting this
        /// also captures "now" as the phase's start (see Progress), so the Status column's
        /// countdown progress bar (§4.4) always starts full, however long the underlying
        /// journal-derived window actually is.
        /// </summary>
        public DateTime? PhaseEndUtc
        {
            get => _phaseEndUtc;
            set
            {
                if (SetProperty(ref _phaseEndUtc, value))
                {
                    _phaseStartUtc = value.HasValue ? DateTime.UtcNow : null;
                    OnPropertyChanged(nameof(HasProgress));
                    RefreshProgress();
                }
            }
        }

        /// <summary>True while Status has a known end time - drives the Status column's countdown progress bar's visibility.</summary>
        public bool HasProgress => _phaseEndUtc.HasValue;

        /// <summary>
        /// Fraction of the current timed phase's window still remaining: 1 the instant it starts,
        /// draining down to 0 as it approaches PhaseEndUtc - drives the Status column's countdown
        /// progress bar's Value. Purely cosmetic, recomputed periodically by RouteViewModel's own
        /// UI timer (see RefreshProgress) - never mutates Status/Icon itself, so it stays outside
        /// CLAUDE.md's event-driven rule for Sequencing/.
        /// </summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        /// <summary>Recomputes Progress against the current wall clock - a no-op (Progress -> 0) once PhaseEndUtc is cleared, or if the phase's own window is degenerate (start == end).</summary>
        public void RefreshProgress()
        {
            if (_phaseEndUtc is not { } end || _phaseStartUtc is not { } start)
            {
                Progress = 0;
                return;
            }

            var totalTicks = (end - start).Ticks;
            if (totalTicks <= 0)
            {
                Progress = 0;
                return;
            }

            var remainingTicks = (end - DateTime.UtcNow).Ticks;
            Progress = Math.Clamp(remainingTicks / (double)totalTicks, 0, 1);
        }
    }
}
