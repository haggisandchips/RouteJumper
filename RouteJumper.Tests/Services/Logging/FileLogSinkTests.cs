using System.IO;
using System.Linq;
using RouteJumper.Services.Logging;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services.Logging
{
    public class FileLogSinkTests
    {
        private static LogSettings DefaultSettings => new(RetentionDays: 7, MaxFileSizeMb: 10, MaxTotalSizeMb: 100);

        private static string LogsDir(TempDirectory dir) => Path.Combine(dir.Path, "Logs");

        [Fact]
        public void Enqueue_ThenDispose_WritesFormattedLineToTodaysFile()
        {
            using var dir = new TempDirectory();
            var sink = new FileLogSink(dir.Path, () => DefaultSettings);

            sink.Enqueue(new LogEntry(DateTime.Now, LogLevel.Info, "Test", "hello world"));
            sink.Dispose(); // waits for the background writer to drain and flush

            var expectedPath = Path.Combine(LogsDir(dir), $"routejumper-{DateTime.Now:yyyy-MM-dd}.log");
            Assert.True(File.Exists(expectedPath));
            Assert.Contains("Test: hello world", File.ReadAllText(expectedPath));
        }

        [Fact]
        public void Dispose_WithNothingEnqueued_DoesNotThrowOrCreateAFile()
        {
            using var dir = new TempDirectory();
            var sink = new FileLogSink(dir.Path, () => DefaultSettings);

            sink.Dispose();

            Assert.False(Directory.Exists(LogsDir(dir)) && Directory.EnumerateFiles(LogsDir(dir)).Any());
        }

        [Fact]
        public void Enqueue_PastMaxFileSize_RollsToANewNumberedSegment()
        {
            using var dir = new TempDirectory();
            var settings = new LogSettings(RetentionDays: 7, MaxFileSizeMb: 1, MaxTotalSizeMb: 1000);
            var sink = new FileLogSink(dir.Path, () => settings);

            // ~60 bytes/line * 20,000 well exceeds the 1MB cap, forcing at least one rollover.
            for (var i = 0; i < 20_000; i++)
            {
                sink.Enqueue(new LogEntry(DateTime.Now, LogLevel.Info, "Test", $"line number {i:D6} of padding"));
            }

            sink.Dispose();

            var datePart = DateTime.Now.ToString("yyyy-MM-dd");
            var files = Directory.EnumerateFiles(LogsDir(dir), $"routejumper-{datePart}*.log").ToList();
            Assert.True(files.Count >= 2, $"Expected at least 2 segments, found {files.Count}: {string.Join(", ", files.Select(Path.GetFileName))}");
            Assert.Contains(files, f => Path.GetFileName(f) == $"routejumper-{datePart}.log");
            Assert.Contains(files, f => Path.GetFileName(f) == $"routejumper-{datePart}.2.log");
        }

        [Fact]
        public void Construct_DeletesFilesOlderThanRetentionDays()
        {
            using var dir = new TempDirectory();
            Directory.CreateDirectory(LogsDir(dir));

            var oldFile = Path.Combine(LogsDir(dir), "routejumper-2020-01-01.log");
            File.WriteAllText(oldFile, "stale entry\n");
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

            var sink = new FileLogSink(dir.Path, () => new LogSettings(RetentionDays: 7, MaxFileSizeMb: 10, MaxTotalSizeMb: 100));
            sink.Dispose(); // forces the initial housekeeping pass (runs before the writer loop reads anything) to complete

            Assert.False(File.Exists(oldFile));
        }

        [Fact]
        public void Construct_OverMaxTotalSize_DeletesOldestFilesFirst()
        {
            using var dir = new TempDirectory();
            Directory.CreateDirectory(LogsDir(dir));

            var oldest = Path.Combine(LogsDir(dir), "routejumper-2026-08-10.log");
            var newer = Path.Combine(LogsDir(dir), "routejumper-2026-08-11.log");
            var payload = new string('x', 600_000); // ~0.6MB each
            File.WriteAllText(oldest, payload);
            File.WriteAllText(newer, payload);
            File.SetLastWriteTime(oldest, DateTime.Now.AddHours(-2));
            File.SetLastWriteTime(newer, DateTime.Now.AddHours(-1));

            var sink = new FileLogSink(dir.Path, () => new LogSettings(RetentionDays: 7, MaxFileSizeMb: 10, MaxTotalSizeMb: 1));
            sink.Dispose();

            Assert.False(File.Exists(oldest));
            Assert.True(File.Exists(newer));
        }
    }
}
