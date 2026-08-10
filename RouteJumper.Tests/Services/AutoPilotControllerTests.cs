using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class AutoPilotControllerTests
    {
        private static readonly DateTime NowUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ComputeAnnounceDelay_ReturnsThePositiveDelay_WhenDueTimeIsInTheFuture()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(5), NowUtc, clampToImmediate: false);

            Assert.Equal(TimeSpan.FromSeconds(5), delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_ReturnsNull_WhenDueTimeHasPassedAndNotClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(-1), NowUtc, clampToImmediate: false);

            Assert.Null(delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_ReturnsZero_WhenDueTimeHasPassedAndClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(-1), NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.Zero, delay);
        }

        /// <summary>
        /// Regression test for the exact bug reported in production: with the default 5000ms
        /// Auto Pilot delay, the Engineer's "in 5 seconds" mark lands exactly at "now" (the
        /// instant Cooldown starts) - by the time this runs, real elapsed time has ticked microseconds
        /// past that instant, so whenUtc is ever-so-slightly in the past. Without clamping, that
        /// silently skipped the announcement on every single run rather than firing it.
        /// </summary>
        [Fact]
        public void ComputeAnnounceDelay_ReturnsZero_WhenDueTimeIsExactlyNowAndClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc, NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.Zero, delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_StillReturnsThePositiveDelay_WhenClampedButNotYetDue()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(2), NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.FromSeconds(2), delay);
        }
    }
}
