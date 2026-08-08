using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using RouteJumper.Sequencing;

namespace RouteJumper.Services
{
    /// <summary>
    /// Watches one commander's journal file for CarrierJumpRequest/CarrierLocation events
    /// belonging to a specific carrier (the Captain role's assigned carrier), and reports each
    /// one via <paramref name="onRowEvent"/> as it's read. Only Plotted (on the
    /// CarrierJumpRequest event itself) fires immediately - every other transition is scheduled
    /// for a real-world instant *offset* from a journal event's own timestamp, not the instant
    /// the event is read: Jumping 3 minutes before CarrierJumpRequest's DepartureTime; the
    /// composite Arrived/Cooldown step 1 minute after CarrierLocation's own timestamp;
    /// CooldownElapsed a further 4 minutes after that (see the field comments below for why
    /// these specific offsets). All scheduling lives here, not in RouteSequencer, which has no
    /// notion of "later" and only ever reacts to events as they arrive.
    ///
    /// On <see cref="StartAsync"/>, the whole file is read first (oldest to newest), so a
    /// freshly-assigned Captain (or an app restart mid-journey) replays the carrier's full
    /// history in order - which is what lets RouteSequencer's row-addressable catch-up bring
    /// the route fully up to date from a single pass. Since that replay processes long-past
    /// events, any derived transition whose scheduled time has already elapsed fires
    /// immediately rather than waiting - see <see cref="ScheduleRowEvent"/>.
    ///
    /// A CarrierLocation event is only trusted as evidence of a real, deliberate jump - and so
    /// only used for row matching/catch-up at all - once a CarrierJumpRequest has been seen for
    /// this carrier in this journal (see <see cref="ProcessLine"/>). The very first
    /// CarrierLocation in a fresh journal is Frontier's passive "wherever the carrier happened
    /// to be when the game loaded" snapshot, not the result of a jump, so it's never trusted for
    /// row matching/catch-up on its own.
    ///
    /// Exception: if this carrier has made *no* jump requests anywhere in the journal at all
    /// (e.g. the game was just restarted mid-journey and nothing new has been requested yet
    /// this session), the passive snapshot is the only evidence of progress available at all -
    /// <see cref="HasAnyJumpRequestSoFar"/> seeds the gate as already-open in that case, since a
    /// long multi-session journey otherwise couldn't resume until the very next real jump.
    ///
    /// Separately from all of the above scheduling, a genuinely live-tailed CarrierLocation
    /// (never the historical replay) also raises <see cref="RowEventKind.LiveCarrierLocation"/>
    /// immediately - see that value's doc comment for what it drives.
    ///
    /// CarrierJumpCancelled carries no SystemName, so it can't be scheduled/targeted the way
    /// the events above are - see <see cref="RowEventKind.JumpCancelled"/> for how RouteSequencer
    /// resolves it. It also cancels whatever Jumping transition the cancelled request had
    /// scheduled, so a stale timer can't re-set "Jumping" on a row after its jump was cancelled.
    ///
    /// CarrierStats is not row-related at all - a live (never replayed) occurrence just invokes
    /// <paramref name="onCarrierStatsObserved"/>, so the Roles tab can refresh itself without
    /// waiting for a manual click whenever the commander opens Carrier Management in-game (the
    /// only time Frontier logs this event, and so the only time the card's carrier name/fuel
    /// level can actually have changed).
    /// </summary>
    public sealed class CarrierRouteJournalWatcher : IDisposable
    {
        // Real-world in-game transitions don't line up exactly with when the journal logs each
        // event: Plotted -> Jumping happens 3 minutes *before* CarrierJumpRequest's own
        // DepartureTime; Jumping -> the composite Arrived/Cooldown step happens 1 minute *after*
        // CarrierLocation's own timestamp; Cooldown -> cleared happens a further 4 minutes after
        // that (5 minutes after CarrierLocation in total).
        private static readonly TimeSpan JumpingLeadTime = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ArrivalToCooldownDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(4);

        private readonly string _journalPath;
        private readonly long _carrierId;
        private readonly Action<RowEventKind, string> _onRowEvent;
        private readonly Action _onCarrierStatsObserved;
        private readonly object _readLock = new();
        private readonly List<Timer> _scheduledTimers = new();

        private FileSystemWatcher? _watcher;
        private long _readOffset;
        private bool _disposed;

        /// <summary>
        /// The Jumping transition scheduled by the most recent CarrierJumpRequest, if it hasn't
        /// fired yet - tracked separately from <see cref="_scheduledTimers"/> (which it's also
        /// still a member of) purely so a CarrierJumpCancelled can cancel *this specific* timer
        /// without a stale one later re-setting "Jumping" on a row whose jump was cancelled.
        /// </summary>
        private Timer? _pendingJumpingTimer;

        /// <summary>
        /// True once a CarrierJumpRequest for this carrier has been seen (in this replay or
        /// live) - see ProcessLine's CarrierLocation branch. Deliberately a single flag spanning
        /// both the initial replay and live monitoring, not reset between them: once real jump
        /// activity has been observed, every CarrierLocation from that point on is trusted.
        ///
        /// Seeded (see StartAsync) from a quick pre-scan of the journal as it stands at
        /// assignment time: if this carrier has *no* CarrierJumpRequest anywhere in it yet (e.g.
        /// the game/journal was just restarted, mid-journey, and no new jump has been requested
        /// this session), the passive startup CarrierLocation snapshot is the only evidence of
        /// progress available at all - trusting it is better than showing nothing for however
        /// long a real multi-session journey takes to resume a new request. If a request *does*
        /// exist somewhere in the journal, this starts false as normal.
        /// </summary>
        private bool _hasSeenJumpRequest;

        public CarrierRouteJournalWatcher(
            string journalPath,
            long carrierId,
            Action<RowEventKind, string> onRowEvent,
            Action onCarrierStatsObserved)
        {
            _journalPath = journalPath;
            _carrierId = carrierId;
            _onRowEvent = onRowEvent;
            _onCarrierStatsObserved = onCarrierStatsObserved;
        }

        public Task StartAsync() => Task.Run(() =>
        {
            _hasSeenJumpRequest = !HasAnyJumpRequestSoFar();

            ReadNewLines(isLive: false);

            if (_disposed)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_journalPath);
            var fileName = Path.GetFileName(_journalPath);
            if (directory is null)
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += (_, _) => ReadNewLines(isLive: true);

            lock (_readLock)
            {
                if (_disposed)
                {
                    watcher.Dispose();
                    return;
                }

                _watcher = watcher;
                _watcher.EnableRaisingEvents = true;
            }
        });

        /// <summary>
        /// <paramref name="isLive"/> is true only when called from the FileSystemWatcher
        /// callback (i.e. a line genuinely newly observed while monitoring) - false for the
        /// one-off historical catch-up read in StartAsync. See RowEventKind.LiveCarrierLocation
        /// for what this distinction drives.
        /// </summary>
        private void ReadNewLines(bool isLive)
        {
            lock (_readLock)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    using var stream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length < _readOffset)
                    {
                        // Unexpected truncation/rotation - start over rather than seek past EOF.
                        _readOffset = 0;
                    }

                    stream.Seek(_readOffset, SeekOrigin.Begin);

                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var text = Encoding.UTF8.GetString(buffer.ToArray());

                    // Only consume complete lines - a trailing partial line (the game mid-write)
                    // is left unread so the next pass picks it up whole.
                    var lastNewline = text.LastIndexOf('\n');
                    if (lastNewline < 0)
                    {
                        return;
                    }

                    var complete = text[..(lastNewline + 1)];
                    _readOffset += Encoding.UTF8.GetByteCount(complete);

                    foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        ProcessLine(line.TrimEnd('\r'), isLive);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>
        /// Quick pre-scan (whole file, no state mutation) for whether this carrier has ever
        /// requested a jump in this journal, used to seed <see cref="_hasSeenJumpRequest"/>
        /// before the main pass. Only meaningful at the point <see cref="StartAsync"/> calls it
        /// (before anything has been appended live) - a request that arrives later, live, is
        /// still picked up normally by <see cref="ProcessLine"/> regardless of what this found.
        /// </summary>
        private bool HasAnyJumpRequestSoFar()
        {
            try
            {
                using var stream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (JournalEventName.Extract(line) != "CarrierJumpRequest")
                    {
                        continue;
                    }

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    using (doc)
                    {
                        var root = doc.RootElement;
                        var carrierType = root.TryGetProperty("CarrierType", out var ct) ? ct.GetString() : null;
                        if (carrierType == "FleetCarrier" &&
                            root.TryGetProperty("CarrierID", out var idEl) &&
                            idEl.TryGetInt64(out var id) &&
                            id == _carrierId)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (IOException)
            {
            }

            return false;
        }

        private static readonly HashSet<string> RelevantEvents = new()
        {
            "CarrierJumpRequest", "CarrierLocation", "CarrierJumpCancelled", "CarrierStats"
        };

        private void ProcessLine(string line, bool isLive)
        {
            var eventName = JournalEventName.Extract(line);
            if (eventName is null || !RelevantEvents.Contains(eventName))
            {
                return;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;

                // CarrierJumpCancelled/CarrierStats carry no CarrierType field at all (unlike
                // CarrierJumpRequest/CarrierLocation, which can also describe a shared squadron
                // carrier that must not be mistaken for this commander's own) - CarrierID alone
                // is enough to identify them, since only this carrier's own owner-triggered
                // events land in this commander's journal for those two event types.
                if (eventName is "CarrierJumpRequest" or "CarrierLocation")
                {
                    var carrierType = root.TryGetProperty("CarrierType", out var ct) ? ct.GetString() : null;
                    if (carrierType != "FleetCarrier")
                    {
                        return;
                    }
                }

                if (!root.TryGetProperty("CarrierID", out var idEl) || !idEl.TryGetInt64(out var id) || id != _carrierId)
                {
                    return;
                }

                if (eventName == "CarrierJumpRequest")
                {
                    if (root.TryGetProperty("SystemName", out var sn) && sn.GetString() is { } systemName)
                    {
                        _hasSeenJumpRequest = true;
                        _onRowEvent(RowEventKind.Plotted, systemName);

                        var departureTimeUtc = TryReadTimestampUtc(root, "DepartureTime");
                        if (departureTimeUtc.HasValue)
                        {
                            _pendingJumpingTimer = ScheduleRowEvent(RowEventKind.Jumping, systemName, departureTimeUtc.Value - JumpingLeadTime);
                        }
                    }
                }
                else if (eventName == "CarrierJumpCancelled")
                {
                    // Cancel whatever Jumping transition the cancelled request had scheduled -
                    // without this, a stale timer could still fire later and re-set "Jumping" on
                    // a row whose jump was just cancelled. JumpCancelled carries no SystemName
                    // (the event itself has none), so RouteSequencer resolves the target row
                    // itself - see RowEventKind.JumpCancelled.
                    lock (_readLock)
                    {
                        if (_pendingJumpingTimer is { } pending)
                        {
                            _scheduledTimers.Remove(pending);
                            pending.Dispose();
                            _pendingJumpingTimer = null;
                        }
                    }

                    _onRowEvent(RowEventKind.JumpCancelled, string.Empty);
                }
                else if (eventName == "CarrierStats")
                {
                    // Only for a genuinely live occurrence - firing this during the one-off
                    // historical replay would trigger a redundant refresh burst for old,
                    // already-reflected Carrier Management visits (assigning Captain already
                    // triggers one refresh of its own regardless).
                    if (isLive)
                    {
                        _onCarrierStatsObserved();
                    }
                }
                else if (!_hasSeenJumpRequest)
                {
                    // A CarrierLocation with no CarrierJumpRequest yet this session is Frontier's
                    // passive startup snapshot (wherever the carrier happened to be when the
                    // journal/session started) - not evidence of a deliberate, route-following
                    // jump, so it's not safe to use for row matching/catch-up at all - trusting
                    // it (even just for cooldown timing) can mark a route complete based on
                    // where the carrier happened to be sitting, not where it actually traveled.
                }
                else if (root.TryGetProperty("StarSystem", out var ss) && ss.GetString() is { } arrivedSystem)
                {
                    // Both the composite Arrived/Cooldown step and the later status-clear are
                    // themselves scheduled - see the field comments above for the timing. If no
                    // usable timestamp is found
                    // (shouldn't happen for a real journal, but defensively possible), fall back
                    // to firing Arrived immediately rather than losing the event entirely - there
                    // is nothing to base a schedule on in that case, so CooldownElapsed is skipped
                    // too (matches the existing "can't schedule what we can't compute" pattern
                    // Jumping already has when DepartureTime is missing).
                    var arrivedAtUtc = TryReadTimestampUtc(root, "timestamp");
                    if (arrivedAtUtc.HasValue)
                    {
                        ScheduleRowEvent(RowEventKind.Arrived, arrivedSystem, arrivedAtUtc.Value + ArrivalToCooldownDelay);
                        ScheduleRowEvent(RowEventKind.CooldownElapsed, arrivedSystem, arrivedAtUtc.Value + ArrivalToCooldownDelay + CooldownDuration);
                    }
                    else
                    {
                        _onRowEvent(RowEventKind.Arrived, arrivedSystem);
                    }

                    // Deliberately immediate and unscheduled, unlike Arrived above - the
                    // "Auto Copy To Clipboard" feature this drives wants the next
                    // system ready to paste as soon as the carrier's actual arrival is observed,
                    // not delayed to match the (intentionally lagged) UI transition. Only for a
                    // genuinely live-tailed line - never during the one-off historical replay a
                    // fresh Captain assignment does, which would otherwise fire a burst of these
                    // for old, already-resolved jumps.
                    if (isLive)
                    {
                        _onRowEvent(RowEventKind.LiveCarrierLocation, arrivedSystem);
                    }
                }
            }
        }

        /// <summary>
        /// Fires a derived row event at (or as soon as possible after) a real-world UTC
        /// instant: immediately if that instant has already passed (e.g. while replaying old
        /// journal history), otherwise via a one-shot, non-blocking <see cref="Timer"/> - never
        /// a blocking wait, and never inside RouteSequencer. Returns the created Timer (or null
        /// if it fired immediately instead, or the watcher is disposed), so a caller that needs
        /// to cancel a specific scheduled event later - see the CarrierJumpCancelled handling in
        /// <see cref="ProcessLine"/> - can hold onto the right one.
        /// </summary>
        private Timer? ScheduleRowEvent(RowEventKind kind, string systemName, DateTime whenUtc)
        {
            var delay = whenUtc - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                _onRowEvent(kind, systemName);
                return null;
            }

            lock (_readLock)
            {
                if (_disposed)
                {
                    return null;
                }

                // Timer must be kept referenced (a field, not a local) or the GC can collect it
                // before it fires. Self-removes from the list once it has fired so the list
                // doesn't grow unbounded across a long session with many jumps.
                Timer? timer = null;
                timer = new Timer(
                    _ =>
                    {
                        _onRowEvent(kind, systemName);
                        lock (_readLock)
                        {
                            if (timer != null)
                            {
                                _scheduledTimers.Remove(timer);
                                if (ReferenceEquals(_pendingJumpingTimer, timer))
                                {
                                    _pendingJumpingTimer = null;
                                }
                            }
                        }
                    },
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);

                _scheduledTimers.Add(timer);
                return timer;
            }
        }

        private static DateTime? TryReadTimestampUtc(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            var text = element.GetString();
            if (text != null && DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }

        public void Dispose()
        {
            lock (_readLock)
            {
                _disposed = true;

                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                foreach (var timer in _scheduledTimers)
                {
                    timer.Dispose();
                }

                _scheduledTimers.Clear();
            }
        }
    }
}
