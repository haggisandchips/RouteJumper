using RouteJumper.Models;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class RouteRowViewModelTests
    {
        [Fact]
        public void Status_Change_AlsoRaisesStatusDisplayChanged()
        {
            var row = new RouteRowViewModel();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.Status = "Plotted";

            Assert.Contains(nameof(RouteRowViewModel.Status), raised);
            Assert.Contains(nameof(RouteRowViewModel.StatusDisplay), raised);
        }

        [Fact]
        public void StatusDisplay_NoProgress_IsJustTheStatusWord()
        {
            var row = new RouteRowViewModel { Status = "Plotted" };
            Assert.Equal("Plotted", row.StatusDisplay);
        }

        [Fact]
        public void HasProgress_FalseUntilPhaseEndUtcSet()
        {
            var row = new RouteRowViewModel();
            Assert.False(row.HasProgress);

            row.PhaseEndUtc = DateTime.UtcNow.AddMinutes(1);
            Assert.True(row.HasProgress);

            row.PhaseEndUtc = null;
            Assert.False(row.HasProgress);
        }

        [Fact]
        public void RefreshProgress_NoPhaseEnd_ProgressZeroAndTimeRemainingEmpty()
        {
            var row = new RouteRowViewModel();
            row.RefreshProgress();

            Assert.Equal(0, row.Progress);
            Assert.Equal(string.Empty, row.TimeRemainingDisplay);
        }

        [Fact]
        public void PhaseEndUtc_JustSet_ProgressStartsAtOne()
        {
            var row = new RouteRowViewModel
            {
                Status = "Plotted",
                PhaseEndUtc = DateTime.UtcNow.AddMinutes(10)
            };

            Assert.Equal(1.0, row.Progress, precision: 2);
        }

        [Fact]
        public void RefreshProgress_PastPhaseEnd_ClampsToZeroAndZeroTimeRemaining()
        {
            var row = new RouteRowViewModel
            {
                Status = "Plotted",
                PhaseEndUtc = DateTime.UtcNow.AddMilliseconds(1)
            };

            Thread.Sleep(50);
            row.RefreshProgress();

            Assert.Equal(0, row.Progress);
            Assert.Equal("0:00:00", row.TimeRemainingDisplay);
        }

        [Fact]
        public void StatusDisplay_WithProgress_AppendsFormattedCountdown()
        {
            var row = new RouteRowViewModel
            {
                Status = "Plotted",
                PhaseEndUtc = DateTime.UtcNow.AddHours(1).AddMinutes(2).AddSeconds(3)
            };

            row.RefreshProgress();

            Assert.StartsWith("Plotted (", row.StatusDisplay);
            Assert.EndsWith(")", row.StatusDisplay);
            // "H:MM:SS" - hours unpadded, minutes/seconds always two digits.
            Assert.Matches(@"^Plotted \(\d+:\d{2}:\d{2}\)$", row.StatusDisplay);
        }

        [Fact]
        public void TimeRemainingDisplay_HoursNeverWrapAt24()
        {
            var row = new RouteRowViewModel
            {
                PhaseEndUtc = DateTime.UtcNow.AddHours(30)
            };

            Assert.StartsWith("29:", row.TimeRemainingDisplay);
        }

        [Fact]
        public void IsIndeterminateProgress_TrueOnlyWhilePlotting()
        {
            var row = new RouteRowViewModel { Status = "Plotting" };
            Assert.True(row.IsIndeterminateProgress);

            row.Status = "Plotted";
            Assert.False(row.IsIndeterminateProgress);

            row.Status = string.Empty;
            Assert.False(row.IsIndeterminateProgress);
        }

        [Fact]
        public void ShowProgressBar_TrueWhilePlotting_EvenWithNoPhaseEnd()
        {
            var row = new RouteRowViewModel { Status = "Plotting" };

            Assert.False(row.HasProgress);
            Assert.True(row.ShowProgressBar);
        }

        [Fact]
        public void ShowProgressBar_TrueWhilePlotted_ViaHasProgress()
        {
            var row = new RouteRowViewModel { Status = "Plotted", PhaseEndUtc = DateTime.UtcNow.AddMinutes(1) };

            Assert.True(row.ShowProgressBar);
        }

        [Fact]
        public void ShowProgressBar_FalseForBlankStatusWithNoPhaseEnd()
        {
            var row = new RouteRowViewModel();

            Assert.False(row.ShowProgressBar);
        }

        [Fact]
        public void StatusDisplay_Plotting_IsJustTheStatusWordWithNoCountdown()
        {
            var row = new RouteRowViewModel { Status = "Plotting" };

            Assert.Equal("Plotting", row.StatusDisplay);
        }

        [Fact]
        public void Status_Change_AlsoRaisesIndeterminateProgressAndShowProgressBarChanged()
        {
            var row = new RouteRowViewModel();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.Status = "Plotting";

            Assert.Contains(nameof(RouteRowViewModel.IsIndeterminateProgress), raised);
            Assert.Contains(nameof(RouteRowViewModel.ShowProgressBar), raised);
        }

        [Fact]
        public void Distance_Change_AlsoRaisesDistanceDisplayChanged()
        {
            var row = new RouteRowViewModel();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.Distance = 12.3;

            Assert.Contains(nameof(RouteRowViewModel.Distance), raised);
            Assert.Contains(nameof(RouteRowViewModel.DistanceDisplay), raised);
        }

        [Fact]
        public void DistanceDisplay_NoDistance_IsEmpty()
        {
            var row = new RouteRowViewModel();
            Assert.Equal(string.Empty, row.DistanceDisplay);
        }

        [Fact]
        public void DistanceDisplay_FormatsToOneDecimalPlaceWithLySuffix()
        {
            var row = new RouteRowViewModel { Distance = 12.345 };
            Assert.Equal("12.3 ly", row.DistanceDisplay);
        }

        [Fact]
        public void StarType_RoundTrips()
        {
            var row = new RouteRowViewModel { StarType = "K (Yellow-Orange) Star" };
            Assert.Equal("K (Yellow-Orange) Star", row.StarType);
        }

        [Fact]
        public void DistanceDisplay_OwnCoordinatesUnavailable_ShowsPlotNeeded()
        {
            var row = new RouteRowViewModel { OwnCoordinatesState = EdsmLookupState.Unavailable };

            Assert.True(row.IsDistancePlaceholder);
            Assert.Equal("Plot needed", row.DistanceDisplay);
        }

        [Fact]
        public void DistanceDisplay_ResolvedDistanceTakesPrecedenceOverPlaceholder()
        {
            var row = new RouteRowViewModel
            {
                OwnCoordinatesState = EdsmLookupState.Resolved,
                Distance = 12.3
            };

            Assert.False(row.IsDistancePlaceholder);
            Assert.Equal("12.3 ly", row.DistanceDisplay);
        }

        [Fact]
        public void StarTypeDisplay_CoordinatesResolvedButStarTypeUnavailable_ShowsTargetNeeded()
        {
            var row = new RouteRowViewModel
            {
                OwnCoordinatesState = EdsmLookupState.Resolved,
                OwnStarTypeState = EdsmLookupState.Unavailable
            };

            Assert.True(row.IsStarTypePlaceholder);
            Assert.Equal("Target needed", row.StarTypeDisplay);
        }

        [Fact]
        public void StarTypeDisplay_CoordinatesUnavailable_NeverShowsTargetNeeded()
        {
            // Coordinates unavailable is Distance's "Plot needed" case - Star Type doesn't get
            // its own separate callout, since plotting a route fixes both.
            var row = new RouteRowViewModel
            {
                OwnCoordinatesState = EdsmLookupState.Unavailable,
                OwnStarTypeState = EdsmLookupState.Unavailable
            };

            Assert.False(row.IsStarTypePlaceholder);
            Assert.Equal(string.Empty, row.StarTypeDisplay);
        }

        [Fact]
        public void StarTypeDisplay_Resolved_ShowsTheStarType()
        {
            var row = new RouteRowViewModel
            {
                OwnCoordinatesState = EdsmLookupState.Resolved,
                OwnStarTypeState = EdsmLookupState.Resolved,
                StarType = "K (Yellow-Orange)"
            };

            Assert.False(row.IsStarTypePlaceholder);
            Assert.Equal("K (Yellow-Orange)", row.StarTypeDisplay);
        }

        [Fact]
        public void OwnCoordinatesState_Change_RaisesPlaceholderAndDisplayPropertyChanged()
        {
            var row = new RouteRowViewModel();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.OwnCoordinatesState = EdsmLookupState.Unavailable;

            Assert.Contains(nameof(RouteRowViewModel.OwnCoordinatesState), raised);
            Assert.Contains(nameof(RouteRowViewModel.IsDistancePlaceholder), raised);
            Assert.Contains(nameof(RouteRowViewModel.DistanceDisplay), raised);
        }

        [Fact]
        public void OwnStarTypeState_Change_RaisesPlaceholderAndDisplayPropertyChanged()
        {
            var row = new RouteRowViewModel { OwnCoordinatesState = EdsmLookupState.Resolved };
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.OwnStarTypeState = EdsmLookupState.Unavailable;

            Assert.Contains(nameof(RouteRowViewModel.OwnStarTypeState), raised);
            Assert.Contains(nameof(RouteRowViewModel.IsStarTypePlaceholder), raised);
            Assert.Contains(nameof(RouteRowViewModel.StarTypeDisplay), raised);
        }

        [Fact]
        public void StarType_Change_AlsoRaisesStarTypeDisplayChanged()
        {
            var row = new RouteRowViewModel();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.StarType = "K (Yellow-Orange)";

            Assert.Contains(nameof(RouteRowViewModel.StarType), raised);
            Assert.Contains(nameof(RouteRowViewModel.StarTypeDisplay), raised);
        }

        [Fact]
        public void SettingIcon_RaisesPropertyChanged()
        {
            var row = new RouteRowViewModel();
            var raised = false;
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RouteRowViewModel.Icon))
                {
                    raised = true;
                }
            };

            row.Icon = RowIcon.InProgress;

            Assert.True(raised);
        }
    }
}
