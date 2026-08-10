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
