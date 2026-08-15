using RouteJumper.Services.Logging;
using Xunit;

namespace RouteJumper.Tests.Services.Logging
{
    public class LogEntryTests
    {
        [Fact]
        public void Format_IncludesTimestampLevelCategoryAndMessage()
        {
            var timestamp = new DateTime(2026, 8, 15, 9, 5, 3, 250);
            var entry = new LogEntry(timestamp, LogLevel.Warn, "Http", "GET failed");

            var formatted = entry.Format();

            Assert.Equal("2026-08-15 09:05:03.250 [WARN ] Http: GET failed", formatted);
        }

        [Theory]
        [InlineData(LogLevel.Debug, "DEBUG")]
        [InlineData(LogLevel.Info, "INFO ")]
        [InlineData(LogLevel.Warn, "WARN ")]
        [InlineData(LogLevel.Error, "ERROR")]
        public void Format_LevelTextIsFixedWidth(LogLevel level, string expectedLevelText)
        {
            var entry = new LogEntry(DateTime.Now, level, "Cat", "msg");

            Assert.Contains($"[{expectedLevelText}]", entry.Format());
        }
    }
}
