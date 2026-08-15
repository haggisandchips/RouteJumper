using System.Globalization;
using RouteJumper.Common;
using RouteJumper.Models;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Represents one row of the Route table: Icon | # | System | Distance | Star Type | Status.
    /// </summary>
    public class RouteRowViewModel : ObservableObject
    {
        private RowIcon _icon = RowIcon.None;
        private int _number;
        private string _systemText = string.Empty;
        private string _status = string.Empty;
        private bool _isCopiedToClipboard;
        private double? _distance;
        private string? _starType;
        private EdsmLookupState _ownCoordinatesState = EdsmLookupState.Resolving;
        private EdsmLookupState _ownStarTypeState = EdsmLookupState.Resolving;
        private DateTime? _phaseEndUtc;
        private DateTime? _phaseStartUtc;
        private double _progress;
        private string _timeRemainingDisplay = string.Empty;

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

        /// <summary>
        /// Current action status ("Status" column), e.g. "Plotted", "Jumping", "Cooldown" - the
        /// raw word alone, used as-is by IconStatusToPackIconKindConverter. See StatusDisplay for
        /// the text actually shown in the Status cell, which appends the countdown.
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusDisplay));
                    OnPropertyChanged(nameof(IsIndeterminateProgress));
                    OnPropertyChanged(nameof(ShowProgressBar));
                }
            }
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
        /// Leg distance in light-years, previous row -&gt; this row - row 1's "previous" is
        /// wherever the CMDR's own ship (Fleet Carrier mode: the Captain's; Ship mode: the tracked
        /// instance's) was when the route was last saved; every other row is the static distance
        /// between two named systems. Set once by RouteRowEnrichmentService after Save/restore,
        /// never by RouteSequencer - this describes the route's static topology, not tracked
        /// progress, so it's deliberately outside CLAUDE.md's event-driven rule for Sequencing/.
        /// Null (blank cell, see DistanceDisplay) until resolved, or permanently if EDSM has no
        /// coordinates for one of the two systems involved.
        /// </summary>
        public double? Distance
        {
            get => _distance;
            set
            {
                if (SetProperty(ref _distance, value))
                {
                    OnPropertyChanged(nameof(DistanceDisplay));
                }
            }
        }

        /// <summary>
        /// This row's own coordinate-lookup outcome, independent of the Distance chain - set by
        /// RouteRowEnrichmentService each pass (SPEC §4.9). Drives the "Plot needed" placeholder:
        /// true (Unavailable) only when *this* row's own system is the one EDSM has no data for,
        /// never for a row whose Distance is merely blank because the row before it (or, for row
        /// 1, the CMDR's own origin position) is the actual problem.
        /// </summary>
        public EdsmLookupState OwnCoordinatesState
        {
            get => _ownCoordinatesState;
            set
            {
                if (SetProperty(ref _ownCoordinatesState, value))
                {
                    OnPropertyChanged(nameof(IsDistancePlaceholder));
                    OnPropertyChanged(nameof(DistanceDisplay));
                    OnPropertyChanged(nameof(IsStarTypePlaceholder));
                    OnPropertyChanged(nameof(StarTypeDisplay));
                }
            }
        }

        /// <summary>This row's own star-type lookup outcome - see OwnCoordinatesState's doc comment for the general shape. Drives the "Target needed" placeholder.</summary>
        public EdsmLookupState OwnStarTypeState
        {
            get => _ownStarTypeState;
            set
            {
                if (SetProperty(ref _ownStarTypeState, value))
                {
                    OnPropertyChanged(nameof(IsStarTypePlaceholder));
                    OnPropertyChanged(nameof(StarTypeDisplay));
                }
            }
        }

        /// <summary>True when this row's own coordinates are confirmed unavailable from EDSM this session - drives "Plot needed" in the Distance cell.</summary>
        public bool IsDistancePlaceholder => OwnCoordinatesState == EdsmLookupState.Unavailable;

        /// <summary>
        /// True when this row's own coordinates resolved fine but its own star type specifically
        /// didn't - drives "Target needed" in the Star Type cell. Deliberately excludes the case
        /// where coordinates are themselves unavailable (IsDistancePlaceholder already covers
        /// that row; a full route plot fixes both, so Star Type doesn't need its own separate
        /// callout there too).
        /// </summary>
        public bool IsStarTypePlaceholder =>
            OwnCoordinatesState == EdsmLookupState.Resolved && OwnStarTypeState == EdsmLookupState.Unavailable;

        /// <summary>What the Distance cell actually shows: "Plot needed" if this row's own coordinates are confirmed unavailable, "12.3 ly" once resolved, or blank while still resolving/blocked by a neighboring row.</summary>
        public string DistanceDisplay => IsDistancePlaceholder
            ? "Plot needed"
            : Distance is { } distance
                ? $"{distance.ToString("0.0", CultureInfo.InvariantCulture)} ly"
                : string.Empty;

        /// <summary>
        /// This row's system's main star's EDSM subType (e.g. "K (Yellow-Orange)"), set once
        /// by RouteRowEnrichmentService after Save/restore. Null (blank cell) until resolved, or
        /// permanently if EDSM has no record of this system at all.
        /// </summary>
        public string? StarType
        {
            get => _starType;
            set
            {
                if (SetProperty(ref _starType, value))
                {
                    OnPropertyChanged(nameof(StarTypeDisplay));
                }
            }
        }

        /// <summary>What the Star Type cell actually shows: "Target needed" if coordinates are known but star type specifically isn't, the resolved star type, or blank.</summary>
        public string StarTypeDisplay => IsStarTypePlaceholder ? "Target needed" : StarType ?? string.Empty;

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
                    OnPropertyChanged(nameof(ShowProgressBar));
                    OnPropertyChanged(nameof(StatusDisplay));
                    RefreshProgress();
                }
            }
        }

        /// <summary>True while Status has a known end time - drives the Status column's countdown progress bar's Value/text.</summary>
        public bool HasProgress => _phaseEndUtc.HasValue;

        /// <summary>
        /// True while Status is "Plotting" - there's no way to know in advance how long playing
        /// the Captain's macro (and the game's own request/acknowledgement) will take, so this
        /// shows an indeterminate progress cue instead of a countdown one.
        /// </summary>
        public bool IsIndeterminateProgress => Status == "Plotting";

        /// <summary>Drives the Status column's progress bar's Visibility - shown for either a known countdown (HasProgress) or the "Plotting" indeterminate cue.</summary>
        public bool ShowProgressBar => HasProgress || IsIndeterminateProgress;

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

        /// <summary>"H:MM:SS" time remaining until PhaseEndUtc (hours uncapped/unpadded, minutes and seconds always 2 digits) - empty while HasProgress is false. See StatusDisplay.</summary>
        public string TimeRemainingDisplay
        {
            get => _timeRemainingDisplay;
            private set
            {
                if (SetProperty(ref _timeRemainingDisplay, value))
                {
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        /// <summary>What the Status cell's text actually shows (§4.4): Status alone while there's no countdown, or "Status (H:MM:SS)" while there is.</summary>
        public string StatusDisplay => HasProgress ? $"{Status} ({TimeRemainingDisplay})" : Status;

        /// <summary>Recomputes Progress/TimeRemainingDisplay against the current wall clock - a no-op (Progress -> 0, TimeRemainingDisplay -> empty) once PhaseEndUtc is cleared, or if the phase's own window is degenerate (start == end).</summary>
        public void RefreshProgress()
        {
            if (_phaseEndUtc is not { } end || _phaseStartUtc is not { } start)
            {
                Progress = 0;
                TimeRemainingDisplay = string.Empty;
                return;
            }

            var totalTicks = (end - start).Ticks;
            if (totalTicks <= 0)
            {
                Progress = 0;
                TimeRemainingDisplay = string.Empty;
                return;
            }

            var now = DateTime.UtcNow;
            var remainingTicks = (end - now).Ticks;
            Progress = Math.Clamp(remainingTicks / (double)totalTicks, 0, 1);

            var remaining = end - now;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            // (int)TotalHours, not Hours - the latter wraps at 24, and a Plotted phase can
            // legitimately be well over an hour out. Minutes/Seconds (the TimeSpan's own
            // component values, always 0-59) are what "hours can be single digit" implies should
            // still be zero-padded.
            TimeRemainingDisplay = $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }
}
