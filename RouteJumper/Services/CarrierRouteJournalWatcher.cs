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
    /// On <see cref="StartAsync"/>, the whole file's history is read first (see
    /// <see cref="ComputeCatchUpState"/>) to work out a *single* authoritative answer - is a jump
    /// currently plotted (not yet arrived), or is the carrier just sitting wherever it last
    /// arrived - and only that one derived event is applied to the route, not one event per
    /// historical line. This matters because the carrier's real path through a session isn't
    /// necessarily a clean walk forward through the pasted route in order - a system visited more
    /// than once (e.g. a fleet carrier revisiting a tritium depot between "real" hops) would, if
    /// replayed line by line, have its row completed the first time some *later* row's event
    /// arrived (RouteSequencer's own catch-up completes every earlier not-yet-complete row along
    /// the way), permanently excluding it from ever matching again on its subsequent, genuine
    /// visits. Computing the final state up front sidesteps that entirely: whichever request or
    /// location comes last in the journal's own line order (the strictly authoritative sequence
    /// these events actually happened in - not each one's own "timestamp" field) is applied once,
    /// and RouteSequencer's normal "complete everything before the matched row" rule only ever
    /// runs against that one, correct target - matching the *first* occurrence of a repeated
    /// system name, since the scan always starts from a freshly Reset table. A CMDR plotting a
    /// genuinely circular route (the same
    /// system appearing more than once *as real, intended waypoints*) is the one case this can
    /// still get wrong - §4.2's manual "Set next system" override exists for exactly that.
    ///
    /// After that one-off catch-up, new lines are live-tailed via a FileSystemWatcher and applied
    /// incrementally, one event per line, through the ordinary <see cref="ProcessLine"/> path -
    /// safe to do live (unlike during catch-up) since there's only ever one new event at a time,
    /// never a backlog to reconcile.
    ///
    /// Separately from all of the above scheduling, a genuinely live-tailed CarrierLocation
    /// (never the historical catch-up) also raises <see cref="RowEventKind.LiveCarrierLocation"/>
    /// immediately - see that value's doc comment for what it drives.
    ///
    /// Every RowEvent this class raises carries whether the *line* that triggered it was seen
    /// live (via FileSystemWatcher) or during the one-off historical catch-up StartAsync does -
    /// see <see cref="RowEvent.IsLive"/>. RouteSequencer currently uses this to gate whether an
    /// Arrived event puts the next row into Cooldown at all: only ever for a genuinely live
    /// arrival, never as a side effect of a fresh Captain assignment catching up on old history.
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
        private readonly Action<RowEventKind, string, bool, DateTime?> _onRowEvent;
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

        public CarrierRouteJournalWatcher(
            string journalPath,
            long carrierId,
            Action<RowEventKind, string, bool, DateTime?> onRowEvent,
            Action onCarrierStatsObserved)
        {
            _journalPath = journalPath;
            _carrierId = carrierId;
            _onRowEvent = onRowEvent;
            _onCarrierStatsObserved = onCarrierStatsObserved;
        }

        public Task StartAsync() => Task.Run(() =>
        {
            var initialLines = ReadNewCompleteLines();
            var catchUp = ComputeCatchUpState(initialLines);

            if (_disposed)
            {
                return;
            }

            if (catchUp is { IsPlotted: true } plotted)
            {
                FirePlotted(plotted.SystemName, plotted.DepartureTimeUtc, isLive: false);
            }
            else if (catchUp is { IsPlotted: false } arrived)
            {
                _onRowEvent(RowEventKind.Arrived, arrived.SystemName, false, null);
            }

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
            watcher.Changed += (_, _) =>
            {
                foreach (var line in ReadNewCompleteLines())
                {
                    ProcessLine(line, isLive: true);
                }
            };

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
        /// Reads and returns every complete line appended to the journal since <see cref="_readOffset"/>,
        /// advancing it past them - the one file-reading mechanism shared by both the initial
        /// catch-up scan (StartAsync) and ongoing live tailing (the FileSystemWatcher callback).
        /// Only consumes complete lines - a trailing partial line (the game mid-write) is left
        /// unread so the next pass picks it up whole.
        /// </summary>
        private List<string> ReadNewCompleteLines()
        {
            lock (_readLock)
            {
                if (_disposed)
                {
                    return new List<string>();
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

                    var lastNewline = text.LastIndexOf('\n');
                    if (lastNewline < 0)
                    {
                        return new List<string>();
                    }

                    var complete = text[..(lastNewline + 1)];
                    _readOffset += Encoding.UTF8.GetByteCount(complete);

                    return complete
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.TrimEnd('\r'))
                        .ToList();
                }
                catch (IOException)
                {
                    return new List<string>();
                }
            }
        }

        /// <summary>
        /// Works out this carrier's single, authoritative state as of the end of
        /// <paramref name="lines"/>: still-plotted (a CarrierJumpRequest not yet followed by a
        /// matching arrival or a cancellation), or else wherever its most recent CarrierLocation
        /// placed it, or null if this carrier has no relevant history in these lines at all.
        /// Decided purely by *line order* - the journal's own sequence, which is strictly
        /// authoritative, since it's the order these events actually happened in - never by each
        /// event's own "timestamp" field: <paramref name="lines"/> is walked oldest to newest, and
        /// whichever of a request/arrival/cancellation was seen *last* in that walk is definitionally
        /// the current word on this carrier's status; a carrier can only ever have one jump in
        /// flight at a time, so there's nothing to reconcile beyond "what happened most recently".
        /// </summary>
        private (bool IsPlotted, string SystemName, DateTime? DepartureTimeUtc)? ComputeCatchUpState(IReadOnlyList<string> lines)
        {
            (string SystemName, DateTime? DepartureTimeUtc)? lastRequest = null;
            string? lastLocationSystem = null;

            // True whenever the most recent of the three relevant kinds seen so far (in line
            // order) was the CarrierJumpRequest currently held in lastRequest - cleared by
            // either a CarrierLocation or a CarrierJumpCancelled, both of which resolve
            // whatever was pending. This is the whole tie-break: no timestamp comparison needed.
            var requestStillOpen = false;

            foreach (var line in lines)
            {
                var eventName = JournalEventName.Extract(line);
                if (eventName is not ("CarrierJumpRequest" or "CarrierLocation" or "CarrierJumpCancelled"))
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

                    // CarrierJumpCancelled carries no CarrierType (same as ProcessLine's own
                    // handling) - CarrierID alone identifies it.
                    if (eventName is "CarrierJumpRequest" or "CarrierLocation")
                    {
                        var carrierType = root.TryGetProperty("CarrierType", out var ct) ? ct.GetString() : null;
                        if (carrierType != "FleetCarrier")
                        {
                            continue;
                        }
                    }

                    if (!root.TryGetProperty("CarrierID", out var idEl) || !idEl.TryGetInt64(out var id) || id != _carrierId)
                    {
                        continue;
                    }

                    if (eventName == "CarrierJumpRequest")
                    {
                        if (root.TryGetProperty("SystemName", out var sn) && sn.GetString() is { } systemName)
                        {
                            lastRequest = (systemName, TryReadTimestampUtc(root, "DepartureTime"));
                            requestStillOpen = true;
                        }
                    }
                    else if (eventName == "CarrierJumpCancelled")
                    {
                        requestStillOpen = false;
                    }
                    else if (root.TryGetProperty("StarSystem", out var ss) && ss.GetString() is { } locationSystem)
                    {
                        lastLocationSystem = locationSystem;
                        requestStillOpen = false;
                    }
                }
            }

            if (requestStillOpen && lastRequest is { } request)
            {
                return (true, request.SystemName, request.DepartureTimeUtc);
            }

            if (lastLocationSystem is { } location)
            {
                return (false, location, null);
            }

            return null;
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
                        FirePlotted(systemName, TryReadTimestampUtc(root, "DepartureTime"), isLive);
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

                    _onRowEvent(RowEventKind.JumpCancelled, string.Empty, isLive, null);
                }
                else if (eventName == "CarrierStats")
                {
                    // Only for a genuinely live occurrence - firing this during the one-off
                    // historical catch-up would trigger a redundant refresh burst for old,
                    // already-reflected Carrier Management visits (assigning Captain already
                    // triggers one refresh of its own regardless).
                    if (isLive)
                    {
                        _onCarrierStatsObserved();
                    }
                }
                else if (root.TryGetProperty("StarSystem", out var ss) && ss.GetString() is { } arrivedSystem)
                {
                    // Both the composite Arrived/Cooldown step and the later status-clear are
                    // themselves scheduled - see the field comments above for the timing. If no
                    // usable timestamp is found (shouldn't happen for a real journal, but
                    // defensively possible), fall back to firing Arrived immediately rather than
                    // losing the event entirely - there is nothing to base a schedule on in that
                    // case, so CooldownElapsed is skipped too (matches the existing "can't
                    // schedule what we can't compute" pattern Jumping already has when
                    // DepartureTime is missing).
                    //
                    // isLive is captured here, at read time, and carried through to whichever
                    // RowEvent eventually fires - live or replay is a property of the *line*
                    // (was it seen while genuinely tailing the journal, or during the one-off
                    // historical catch-up a fresh Captain assignment does), not of whether its
                    // derived transition happens to fire immediately or later. RouteSequencer
                    // uses it to gate whether Arrived sets Cooldown on the next row at all - see
                    // RowEventKind.Arrived.
                    var arrivedAtUtc = TryReadTimestampUtc(root, "timestamp");
                    if (arrivedAtUtc.HasValue)
                    {
                        // Arrived's PhaseEndUtc is when Cooldown (which it may put the *next*
                        // row into - see RowEventKind.Arrived) will itself clear - purely
                        // cosmetic, same as Plotted/Jumping's above; RouteSequencer only ever
                        // applies it when it actually sets Cooldown (a live arrival with a next
                        // row), never for the arrived-at row itself.
                        var cooldownEndUtc = arrivedAtUtc.Value + ArrivalToCooldownDelay + CooldownDuration;
                        ScheduleRowEvent(RowEventKind.Arrived, arrivedSystem, arrivedAtUtc.Value + ArrivalToCooldownDelay, isLive, cooldownEndUtc);
                        ScheduleRowEvent(RowEventKind.CooldownElapsed, arrivedSystem, cooldownEndUtc, isLive);
                    }
                    else
                    {
                        _onRowEvent(RowEventKind.Arrived, arrivedSystem, isLive, null);
                    }

                    // Deliberately immediate and unscheduled, unlike Arrived above - the
                    // "Auto Copy To Clipboard" feature this drives wants the next
                    // system ready to paste as soon as the carrier's actual arrival is observed,
                    // not delayed to match the (intentionally lagged) UI transition. Only for a
                    // genuinely live-tailed line - never during the one-off historical catch-up a
                    // fresh Captain assignment does, which would otherwise fire a burst of these
                    // for old, already-resolved jumps.
                    if (isLive)
                    {
                        _onRowEvent(RowEventKind.LiveCarrierLocation, arrivedSystem, isLive, null);
                    }
                }
            }
        }

        /// <summary>
        /// Raises Plotted for <paramref name="systemName"/> and, if <paramref name="departureTimeUtc"/>
        /// is known, schedules the derived Jumping transition - shared by a CarrierJumpRequest
        /// line actually read live (ProcessLine) and the one derived from the initial catch-up
        /// scan (StartAsync), since both need to seed the exact same Plotted/Jumping state either
        /// way.
        /// </summary>
        private void FirePlotted(string systemName, DateTime? departureTimeUtc, bool isLive)
        {
            // Plotted's own PhaseEndUtc is the instant Jumping will start (below). Jumping's is an
            // estimate of when the row will actually leave Jumping - not DepartureTime itself, but
            // ArrivalToCooldownDelay (1 minute) after it: the row doesn't move on until the
            // composite Arrived step actually fires, which SPEC §5.7 fires 1 minute after the
            // carrier's own CarrierLocation (arrival) timestamp, not at DepartureTime. Arrival
            // isn't known yet at CarrierJumpRequest time, but it's effectively instantaneous at
            // DepartureTime in practice, so DepartureTime + ArrivalToCooldownDelay is the best
            // available estimate (Arrived's own PhaseEndUtc, set once CarrierLocation is actually
            // observed, is exact). Both PhaseEndUtc values are purely cosmetic (Route tab §4.4's
            // countdown progress bar); null if DepartureTime couldn't be parsed, same as the
            // existing "can't schedule what we can't compute" fallback for Jumping itself.
            var jumpingStartUtc = departureTimeUtc.HasValue ? departureTimeUtc.Value - JumpingLeadTime : (DateTime?)null;
            var jumpingEndEstimateUtc = departureTimeUtc.HasValue ? departureTimeUtc.Value + ArrivalToCooldownDelay : (DateTime?)null;

            _onRowEvent(RowEventKind.Plotted, systemName, isLive, jumpingStartUtc);

            if (departureTimeUtc.HasValue)
            {
                _pendingJumpingTimer = ScheduleRowEvent(RowEventKind.Jumping, systemName, jumpingStartUtc!.Value, isLive, jumpingEndEstimateUtc);
            }
        }

        /// <summary>
        /// Fires a derived row event at (or as soon as possible after) a real-world UTC
        /// instant: immediately if that instant has already passed (e.g. while catching up on
        /// old journal history), otherwise via a one-shot, non-blocking <see cref="Timer"/> -
        /// never a blocking wait, and never inside RouteSequencer. Returns the created Timer (or
        /// null if it fired immediately instead, or the watcher is disposed), so a caller that
        /// needs to cancel a specific scheduled event later - see the CarrierJumpCancelled
        /// handling in <see cref="ProcessLine"/> - can hold onto the right one. <paramref name="isLive"/>
        /// is carried straight through to the fired RowEvent unchanged, whether it fires
        /// immediately here or later from the Timer callback - it describes the *line* that
        /// triggered the schedule (genuinely live-tailed, or historical catch-up), not the
        /// scheduling outcome. <paramref name="phaseEndUtc"/> is likewise carried straight through
        /// unchanged - see RowEvent.PhaseEndUtc.
        /// </summary>
        private Timer? ScheduleRowEvent(RowEventKind kind, string systemName, DateTime whenUtc, bool isLive, DateTime? phaseEndUtc = null)
        {
            var delay = whenUtc - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                _onRowEvent(kind, systemName, isLive, phaseEndUtc);
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
                        _onRowEvent(kind, systemName, isLive, phaseEndUtc);
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
