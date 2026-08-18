using System.IO;
using System.Text;
using System.Threading.Channels;

namespace RouteJumper.Services.Logging
{
    /// <summary>
    /// Background, non-blocking writer of LogEntry lines to a date-stamped file under
    /// AppPaths.DataDirectory\Logs. Log.Write only ever enqueues onto an in-memory
    /// System.Threading.Channels.Channel - genuinely non-blocking, so no caller (a journal
    /// watcher's own background thread, a hot HTTP request/response pair, ...) is ever slowed
    /// down by disk I/O. A single background task owns the file handle and drains the channel:
    /// every entry is written immediately, and the writer is flushed once the channel has been
    /// drained empty (rather than after every single line, or only periodically) - this keeps a
    /// separate process tailing the file (e.g. `Get-Content -Wait`) seeing new lines with minimal
    /// lag during a burst, without paying a flush's own cost per line. The file itself is opened
    /// with FileShare.ReadWrite so a concurrent tailing process can always open it for read.
    ///
    /// Housekeeping (retention days / max file size / max total size, all configurable via
    /// AppConfigStore/LogSettings) runs at startup, whenever the sink rolls to a new file (a new
    /// day, or the current file crossing its size cap), and periodically in the background
    /// (HousekeepingInterval) - so old/oversized log data never accumulates unbounded "at all
    /// times", not just on next app launch. LogSettings itself is only re-read from disk at that
    /// same cadence (never per log line) - the whole point of caching it in <see cref="_settings"/>
    /// is that a per-write settings read would itself be exactly the kind of avoidable I/O this
    /// class exists to keep off the hot logging path.
    /// </summary>
    public sealed class FileLogSink : IDisposable
    {
        private const string FilePrefix = "routejumper-";
        private const string FileExtension = ".log";
        private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromMinutes(30);

        private readonly string _logsDirectory;
        private readonly Func<LogSettings> _getSettings;
        private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        private readonly Task _writerTask;

        private LogSettings _settings;
        private StreamWriter? _writer;
        private string? _currentFilePath;
        private DateTime _currentFileDate;
        private int _currentSequence = 1;
        private long _currentFileSize;

        public FileLogSink(string dataDirectory, Func<LogSettings> getSettings)
        {
            _logsDirectory = Path.Combine(dataDirectory, "Logs");
            _getSettings = getSettings;
            _settings = getSettings();
            Directory.CreateDirectory(_logsDirectory);

            _writerTask = Task.Run(RunWriterLoopAsync);
        }

        /// <summary>Never blocks - the whole point of the background writer above.</summary>
        public void Enqueue(LogEntry entry) => _channel.Writer.TryWrite(entry);

        private async Task RunWriterLoopAsync()
        {
            RunHousekeeping();
            var lastHousekeepingUtc = DateTime.UtcNow;

            // WaitToReadAsync + a draining TryRead loop, rather than ReadAllAsync - this channel
            // configuration doesn't support ChannelReader.Count, so "flush once the current burst
            // has drained" has to be expressed as "keep TryRead-ing until nothing's left", not by
            // checking Count == 0. Flushing once per drained batch (not per line) is what keeps a
            // concurrent tail seeing fresh data promptly without paying a flush's cost per write
            // during a burst of many lines in quick succession.
            while (await _channel.Reader.WaitToReadAsync())
            {
                while (_channel.Reader.TryRead(out var entry))
                {
                    WriteEntry(entry);
                }

                _writer?.Flush();

                if (DateTime.UtcNow - lastHousekeepingUtc > HousekeepingInterval)
                {
                    RunHousekeeping();
                    lastHousekeepingUtc = DateTime.UtcNow;
                }
            }

            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }

        private void WriteEntry(LogEntry entry)
        {
            try
            {
                EnsureWriterForToday();

                var line = entry.Format();
                _writer!.WriteLine(line);
                _currentFileSize += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

                if (_currentFileSize >= _settings.MaxFileSizeBytes)
                {
                    RollToNewFile(newSegmentToday: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void EnsureWriterForToday()
        {
            if (_writer != null && _currentFileDate == DateTime.Now.Date)
            {
                return;
            }

            RollToNewFile(newSegmentToday: false);
        }

        /// <summary>
        /// Opens the next file to write to: a fresh day always starts back at sequence 1 (picking
        /// up whatever the highest existing segment for that date already is, in case the app was
        /// restarted more than once on the same day); <paramref name="newSegmentToday"/> forces
        /// the next segment (crossing the per-file size cap mid-day). Either way, if the chosen
        /// file already exists and is already at/over the size cap (e.g. resuming a same-day
        /// segment after a restart), it's skipped in favour of the next one instead of being
        /// appended past its own limit.
        /// </summary>
        private void RollToNewFile(bool newSegmentToday)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            var today = DateTime.Now.Date;
            if (_currentFileDate != today)
            {
                _currentFileDate = today;
                _currentSequence = FindLatestSequenceForDate(today);
            }
            else if (newSegmentToday)
            {
                _currentSequence++;
            }

            _currentFilePath = BuildFilePath(today, _currentSequence);
            var existingSize = File.Exists(_currentFilePath) ? new FileInfo(_currentFilePath).Length : 0;

            if (existingSize >= _settings.MaxFileSizeBytes)
            {
                _currentSequence++;
                _currentFilePath = BuildFilePath(today, _currentSequence);
                existingSize = 0;
            }

            var stream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
            _currentFileSize = existingSize;

            RunHousekeeping();
        }

        private int FindLatestSequenceForDate(DateTime date)
        {
            var datePart = date.ToString("yyyy-MM-dd");
            var max = 1;

            try
            {
                foreach (var file in Directory.EnumerateFiles(_logsDirectory, $"{FilePrefix}{datePart}*{FileExtension}"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var suffix = name[(FilePrefix.Length + datePart.Length)..]; // "" for segment 1, ".N" otherwise
                    if (suffix.Length > 1 && suffix[0] == '.' && int.TryParse(suffix[1..], out var sequence) && sequence > max)
                    {
                        max = sequence;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return max;
        }

        private string BuildFilePath(DateTime date, int sequence)
        {
            var datePart = date.ToString("yyyy-MM-dd");
            var name = sequence <= 1 ? $"{FilePrefix}{datePart}{FileExtension}" : $"{FilePrefix}{datePart}.{sequence}{FileExtension}";
            return Path.Combine(_logsDirectory, name);
        }

        /// <summary>
        /// Deletes anything older than RetentionDays (by last-write time - simple and close
        /// enough, given every file's own name is already date-stamped to roughly the same day),
        /// then - if the whole Logs folder is still over MaxTotalSizeMb - deletes the oldest
        /// remaining files (never the one currently being written to) until it isn't. Best-effort:
        /// disk/permission failures degrade to "housekeeping skipped this pass" rather than
        /// crashing the writer loop, same philosophy AppSettingsStore/AppConfigStore already use
        /// for their own I/O.
        /// </summary>
        private void RunHousekeeping()
        {
            _settings = _getSettings();

            try
            {
                var files = Directory.EnumerateFiles(_logsDirectory, $"{FilePrefix}*{FileExtension}")
                    .Select(path => new FileInfo(path))
                    .ToList();

                var cutoff = DateTime.Now.Date.AddDays(-Math.Max(0, _settings.RetentionDays));
                foreach (var file in files.Where(f => f.LastWriteTime.Date < cutoff && !IsCurrentFile(f.FullName)))
                {
                    TryDelete(file);
                }

                var remaining = files
                    .Where(f => File.Exists(f.FullName))
                    .OrderBy(f => f.LastWriteTime)
                    .ToList();

                var totalBytes = remaining.Sum(f => f.Length);
                foreach (var file in remaining)
                {
                    if (totalBytes <= _settings.MaxTotalSizeBytes)
                    {
                        break;
                    }

                    if (IsCurrentFile(file.FullName))
                    {
                        continue;
                    }

                    totalBytes -= file.Length;
                    TryDelete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private bool IsCurrentFile(string path) =>
            _currentFilePath != null && string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase);

        private static void TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Completes the channel and waits (briefly - this runs on app shutdown) for the writer loop to drain and flush whatever's left.</summary>
        public void Dispose()
        {
            _channel.Writer.TryComplete();
            try
            {
                _writerTask.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException)
            {
            }
        }
    }
}
