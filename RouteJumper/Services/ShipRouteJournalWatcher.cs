using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using RouteJumper.Sequencing;

namespace RouteJumper.Services
{
    /// <summary>
    /// Watches a solo commander's own journal file for FSDTarget/StartJump/FSDJump/Music events
    /// (Ship mode's equivalent of CarrierRouteJournalWatcher, which this deliberately mirrors the
    /// shape of), and reports each one via <paramref name="onRowEvent"/> as it's read. No
    /// CarrierID/Type filtering is needed - these are always first-person "this commander's own
    /// ship" events, a structurally separate family from CarrierJumpRequest/CarrierLocation, so
    /// there's no ambiguity even if the same journal also contains fleet carrier events.
    ///
    /// FSDTarget (selecting/locking a jump target, e.g. in the galaxy map) raises
    /// <see cref="RowEventKind.Targeted"/> directly - see that value's own doc comment for why
    /// RouteSequencer, not this class, decides whether to apply it immediately or defer it.
    ///
    /// Unlike the carrier path, nothing here needs real-world-clock *lead-time* scheduling for
    /// Jumping: StartJump only ever fires once a jump is genuinely, imminently underway (confirmed
    /// against real journal data - the gap to the matching FSDJump is consistently ~18 seconds,
    /// not carrier-style minutes), so Jumping fires immediately, the instant StartJump is read.
    ///
    /// Arrived is *not* fired directly off FSDJump, though, even though FSDJump carries the
    /// arrival system name - confirmed against real journal data that FSDJump fires while the
    /// game is still mid-transition (loading/settling), too early to treat as "arrived" for
    /// Cooldown-timing purposes. The more accurate signal is the "Music" event that follows it:
    /// its MusicTrack is "DestinationFromHyperspace" or "Supercruise" once the game has actually
    /// settled into normal flight at the destination (which of the two depends on some other
    /// in-game factor, e.g. whether a further in-system target is already queued - both were seen
    /// firing at 100% coverage - 6 of 6 jumps - in one real sample session, split roughly
    /// one-third/two-thirds between the two tracks). So FSDJump instead records a *pending*
    /// arrival (<see cref="BeginPendingArrival"/>); the next qualifying Music event resolves it
    /// into the real Arrived event (<see cref="ProcessLine"/>'s "Music" case). A short
    /// <see cref="ArrivalSettleTimeout"/> fallback timer resolves it anyway if neither Music track
    /// ever shows up - a pure backstop, not the expected path, given the 100% coverage observed.
    /// CooldownElapsed is then scheduled a fixed <see cref="ProvisionalCooldownDuration"/> after
    /// whichever of those two actually resolved the arrival - using the same non-blocking,
    /// one-shot Timer pattern CarrierRouteJournalWatcher.ScheduleRowEvent already uses, never a
    /// blocking wait, and never inside RouteSequencer (CLAUDE.md's rule).
    ///
    /// On <see cref="StartAsync"/>, the whole file's history is read first (see
    /// <see cref="ComputeCatchUpState"/>) to work out a single authoritative answer - a target
    /// selected but not yet engaged, a jump currently underway (an open StartJump not yet
    /// followed by a matching FSDJump), or the ship just sitting wherever it last arrived - and
    /// only that one derived event is applied, not one event per historical line. Same rationale
    /// as the carrier path: a commander's real
    /// journal reflects their *entire* play session, not just this route, and may revisit a
    /// system more than once (e.g. a tritium depot stop) - computing the single final state up
    /// front, rather than replaying every line, avoids RouteSequencer's own "complete everything
    /// before the matched row" catch-up rule prematurely completing a row on its first, wrong
    /// occurrence. After that one-off catch-up, new lines are live-tailed via a FileSystemWatcher
    /// and applied incrementally through the ordinary <see cref="ProcessLine"/> path.
    ///
    /// Every RowEvent this class raises carries whether the *line* that triggered it was seen
    /// live or during the one-off historical catch-up - see <see cref="RowEvent.IsLive"/> -
    /// exactly the same convention the carrier path uses, and for the same reason: RouteSequencer
    /// only ever puts a row into Cooldown for a genuinely live-observed arrival.
    ///
    /// A genuinely live-tailed FSDJump also raises <see cref="RowEventKind.LiveCarrierLocation"/>
    /// immediately - this is what makes "Auto Copy To Clipboard" work in Ship mode with zero
    /// RouteViewModel changes, since that feature's own handler only checks the event kind, never
    /// its source.
    ///
    /// There is no known journal event for an aborted/interrupted ship jump (Elite's schema has
    /// no StartJumpCancelled), so this class does not attempt to handle that case - a row left
    /// stuck on "Jumping" by an interrupted charge needs the existing manual "Set next system"
    /// override (§4.2) until/unless a real cancellation signal is identified.
    /// </summary>
    public sealed class ShipRouteJournalWatcher : IDisposable
    {
        /// <summary>
        /// PROVISIONAL - the real post-jump FSD cooldown for a player-flown ship has not yet been
        /// measured in-game (confirmed to be a real cooldown, not merely cosmetic, but the exact
        /// duration is unknown as of writing). This is a placeholder guess only - replace it once
        /// the real duration is known (see SPEC.md's Ship mode section).
        /// </summary>
        // TODO(ship-cooldown): replace with a measured value.
        public static readonly TimeSpan ProvisionalCooldownDuration = TimeSpan.FromSeconds(10);

        /// <summary>Which Music track(s) confirm a pending arrival has actually settled - see this class's own doc comment.</summary>
        private static readonly HashSet<string> ArrivalConfirmedMusicTracks = new() { "DestinationFromHyperspace", "Supercruise" };

        /// <summary>
        /// Backstop only, not the expected path (real journal data showed 100% coverage from the
        /// Music cue itself) - how long to wait for it before firing Arrived anyway, in case a
        /// future session ever shows it missing.
        /// </summary>
        private static readonly TimeSpan ArrivalSettleTimeout = TimeSpan.FromSeconds(15);

        private readonly string _journalPath;
        private readonly Action<RowEventKind, string, bool, DateTime?> _onRowEvent;
        private readonly object _readLock = new();
        private readonly List<Timer> _scheduledTimers = new();

        private FileSystemWatcher? _watcher;
        private long _readOffset;
        private bool _disposed;

        /// <summary>The arrival system FSDJump most recently reported, still awaiting Music confirmation - see BeginPendingArrival/ResolvePendingArrival.</summary>
        private string? _pendingArrivalSystem;

        /// <summary>The fallback timer for the current pending arrival, if any - used to detect (via reference equality) whether a fallback firing has already been superseded by the Music cue resolving it first.</summary>
        private Timer? _pendingArrivalTimer;

        public ShipRouteJournalWatcher(string journalPath, Action<RowEventKind, string, bool, DateTime?> onRowEvent)
        {
            _journalPath = journalPath;
            _onRowEvent = onRowEvent;
        }

        public Task StartAsync() => Task.Run(() =>
        {
            var initialLines = ReadNewCompleteLines();
            var catchUp = ComputeCatchUpState(initialLines);

            if (_disposed)
            {
                return;
            }

            switch (catchUp)
            {
                case { Phase: CatchUpPhase.Targeted } targeted:
                    _onRowEvent(RowEventKind.Targeted, targeted.SystemName, false, null);
                    break;
                case { Phase: CatchUpPhase.Jumping } jumping:
                    _onRowEvent(RowEventKind.Jumping, jumping.SystemName, false, null);
                    break;
                case { Phase: CatchUpPhase.Arrived } arrived:
                    _onRowEvent(RowEventKind.Arrived, arrived.SystemName, false, null);
                    break;
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
        /// advancing it past them - identical mechanism to CarrierRouteJournalWatcher's own method
        /// of the same name (see there for the full rationale): shared by the initial catch-up
        /// scan and ongoing live tailing, only ever consuming complete (newline-terminated) lines.
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

        private enum CatchUpPhase { Targeted, Jumping, Arrived }

        /// <summary>
        /// Works out this ship's single, authoritative state as of the end of
        /// <paramref name="lines"/> - a target selected but not yet engaged (FSDTarget), a jump
        /// genuinely underway (a Hyperspace StartJump not yet followed by a matching FSDJump), or
        /// else wherever its most recent FSDJump placed it - or null if there's no relevant
        /// history in these lines at all. Decided purely by line order, exactly like
        /// CarrierRouteJournalWatcher.ComputeCatchUpState - a ship can only ever be in one of
        /// these three phases at a time, so whichever of the three event kinds was seen last is
        /// definitionally the current word on its status. Unlike live processing (ProcessLine), a
        /// caught-up Targeted result is applied directly, never deferred - RouteSequencer's own
        /// defer logic only matters for distinguishing a genuinely live "next hop pre-targeted
        /// mid-jump" situation from a settled one, and catch-up by definition only ever produces
        /// one single settled result.
        /// </summary>
        private static (CatchUpPhase Phase, string SystemName)? ComputeCatchUpState(IReadOnlyList<string> lines)
        {
            (CatchUpPhase Phase, string SystemName)? latest = null;

            foreach (var line in lines)
            {
                var eventName = JournalEventName.Extract(line);
                if (eventName is not ("FSDTarget" or "StartJump" or "FSDJump"))
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

                    if (eventName == "FSDTarget")
                    {
                        if (root.TryGetProperty("Name", out var nm) && nm.GetString() is { } targetSystem)
                        {
                            latest = (CatchUpPhase.Targeted, targetSystem);
                        }
                    }
                    else if (eventName == "StartJump")
                    {
                        // Supercharge (a neutron/white-dwarf FSD boost) doesn't change system and
                        // isn't a route-relevant jump - only Hyperspace is.
                        var jumpType = root.TryGetProperty("JumpType", out var jt) ? jt.GetString() : null;
                        if (jumpType != "Hyperspace")
                        {
                            continue;
                        }

                        if (root.TryGetProperty("StarSystem", out var sn) && sn.GetString() is { } systemName)
                        {
                            latest = (CatchUpPhase.Jumping, systemName);
                        }
                    }
                    else if (root.TryGetProperty("StarSystem", out var ss) && ss.GetString() is { } arrivedSystem)
                    {
                        latest = (CatchUpPhase.Arrived, arrivedSystem);
                    }
                }
            }

            return latest;
        }

        private static readonly HashSet<string> RelevantEvents = new() { "FSDTarget", "StartJump", "FSDJump", "Music" };

        /// <summary>Internal (not private) so tests can exercise the live-tailed path deterministically, without depending on real FileSystemWatcher timing - same testability precedent as EliteInstanceScanner.ReadJournalSummary.</summary>
        internal void ProcessLine(string line, bool isLive)
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

                if (eventName == "FSDTarget")
                {
                    // Unlike StartJump/FSDJump, FSDTarget names the system via "Name", not
                    // "StarSystem". No filtering needed here (unlike StartJump's Hyperspace/
                    // Supercharge distinction) - every FSDTarget line reflects a genuine
                    // hyperspace-jump target selection. RouteSequencer itself decides whether to
                    // apply this immediately or defer it - see RowEventKind.Targeted.
                    if (root.TryGetProperty("Name", out var nm) && nm.GetString() is { } targetSystem)
                    {
                        _onRowEvent(RowEventKind.Targeted, targetSystem, isLive, null);
                    }
                }
                else if (eventName == "StartJump")
                {
                    var jumpType = root.TryGetProperty("JumpType", out var jt) ? jt.GetString() : null;
                    if (jumpType != "Hyperspace")
                    {
                        return;
                    }

                    if (root.TryGetProperty("StarSystem", out var sn) && sn.GetString() is { } systemName)
                    {
                        // No PhaseEndUtc - there's no known jump duration to count down against.
                        // RouteSequencer accepts a row already "Targeted" (the normal case, off
                        // FSDTarget above), "Plotted" (Fleet Carrier mode's own predecessor - not
                        // applicable here), or still blank (a fallback for when FSDTarget was
                        // never observed for this specific hop, e.g. it was set in a previous,
                        // now-rotated-out journal file) - see RouteSequencer.FindTargetIndex.
                        _onRowEvent(RowEventKind.Jumping, systemName, isLive, null);
                    }
                }
                else if (eventName == "FSDJump")
                {
                    if (root.TryGetProperty("StarSystem", out var ss) && ss.GetString() is { } arrivedSystem)
                    {
                        // Deliberately immediate and unscheduled, unlike Arrived below - see this
                        // class's own doc comment and RowEventKind.LiveCarrierLocation. Only for
                        // a genuinely live-tailed line, never during the one-off historical
                        // catch-up.
                        if (isLive)
                        {
                            _onRowEvent(RowEventKind.LiveCarrierLocation, arrivedSystem, isLive, null);
                        }

                        // FSDJump itself is *not* treated as the confirmed arrival - see this
                        // class's own doc comment for why. This just records the pending arrival;
                        // ResolvePendingArrival (via the "Music" case below, or the fallback
                        // timer) fires the real Arrived event.
                        BeginPendingArrival(arrivedSystem, isLive);
                    }
                }
                else if (eventName == "Music")
                {
                    var musicTrack = root.TryGetProperty("MusicTrack", out var mt) ? mt.GetString() : null;
                    if (musicTrack != null && ArrivalConfirmedMusicTracks.Contains(musicTrack))
                    {
                        ResolvePendingArrivalFromMusic();
                    }
                }
            }
        }

        /// <summary>
        /// Records <paramref name="systemName"/> as the arrival awaiting Music confirmation (see
        /// this class's own doc comment), superseding whatever was previously pending - in
        /// practice this can't actually happen given real jump cadence (StartJump/FSDJump pairs
        /// are seconds apart, far longer than <see cref="ArrivalSettleTimeout"/>), but is handled
        /// safely regardless: the superseded pending arrival's own fallback timer is disposed
        /// before it can fire, and its Arrived event is simply never raised. Starts a fallback
        /// timer that resolves the arrival anyway if no qualifying Music event shows up in time.
        /// </summary>
        private void BeginPendingArrival(string systemName, bool isLive)
        {
            lock (_readLock)
            {
                if (_disposed)
                {
                    return;
                }

                _pendingArrivalTimer?.Dispose();
                _pendingArrivalSystem = systemName;

                Timer? timer = null;
                timer = new Timer(
                    _ => ResolvePendingArrivalFromTimeout(systemName, isLive, timer),
                    null,
                    ArrivalSettleTimeout,
                    Timeout.InfiniteTimeSpan);
                _pendingArrivalTimer = timer;
            }
        }

        /// <summary>Resolves the current pending arrival (if any) the instant a qualifying Music event is observed - the expected, common path (see this class's own doc comment).</summary>
        private void ResolvePendingArrivalFromMusic()
        {
            string? systemName;
            Timer? timer;
            lock (_readLock)
            {
                systemName = _pendingArrivalSystem;
                timer = _pendingArrivalTimer;
                _pendingArrivalSystem = null;
                _pendingArrivalTimer = null;
            }

            timer?.Dispose();
            if (systemName != null)
            {
                FireArrived(systemName, isLive: true);
            }
        }

        /// <summary>Fallback-only resolution (see ArrivalSettleTimeout) - a no-op if the Music cue already resolved this pending arrival first (detected via reference equality against the timer that's still current).</summary>
        private void ResolvePendingArrivalFromTimeout(string systemName, bool isLive, Timer? expectedTimer)
        {
            lock (_readLock)
            {
                if (!ReferenceEquals(_pendingArrivalTimer, expectedTimer))
                {
                    return;
                }

                _pendingArrivalSystem = null;
                _pendingArrivalTimer = null;
            }

            FireArrived(systemName, isLive);
        }

        /// <summary>Raises the real Arrived event (and schedules CooldownElapsed a fixed ProvisionalCooldownDuration later) - shared by both ways a pending arrival can resolve.</summary>
        private void FireArrived(string systemName, bool isLive)
        {
            var cooldownEndUtc = DateTime.UtcNow + ProvisionalCooldownDuration;
            _onRowEvent(RowEventKind.Arrived, systemName, isLive, cooldownEndUtc);
            ScheduleRowEvent(RowEventKind.CooldownElapsed, systemName, cooldownEndUtc, isLive);
        }

        /// <summary>
        /// Fires a derived row event at (or as soon as possible after) a real-world UTC instant -
        /// identical mechanism to CarrierRouteJournalWatcher.ScheduleRowEvent (see there for the
        /// full rationale): immediately if that instant has already passed, otherwise via a
        /// one-shot, non-blocking Timer, never a blocking wait, and never inside RouteSequencer.
        /// </summary>
        private void ScheduleRowEvent(RowEventKind kind, string systemName, DateTime whenUtc, bool isLive, DateTime? phaseEndUtc = null)
        {
            var delay = whenUtc - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                _onRowEvent(kind, systemName, isLive, phaseEndUtc);
                return;
            }

            lock (_readLock)
            {
                if (_disposed)
                {
                    return;
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
                            }
                        }
                    },
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);

                _scheduledTimers.Add(timer);
            }
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

                _pendingArrivalTimer?.Dispose();
                _pendingArrivalTimer = null;
                _pendingArrivalSystem = null;
            }
        }
    }
}
