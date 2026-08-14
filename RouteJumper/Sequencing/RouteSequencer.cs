using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Applies row-addressable events (see <see cref="RowEvent"/>) - almost all originating from
    /// a Captain's journal (see CarrierRouteJournalWatcher); the sole exception is
    /// RowEventKind.Plotting, raised by AutoPilotController itself the instant it starts playing
    /// the Captain's macro - to a route's rows. Event-driven by design either way: every state
    /// change is a direct response to an <see cref="IRowEventTrigger"/> event, never a timer or
    /// other hardcoded delay.
    /// </summary>
    public class RouteSequencer
    {
        private IReadOnlyList<RouteRowViewModel>? _rows;

        /// <summary>A Targeted event's system name, held back because it arrived while some row was still Jumping/Cooldown - see ApplyRowEvent's own Targeted case and FlushDeferredTargeted.</summary>
        private string? _deferredTargetedSystemName;

        /// <summary>
        /// Wires a row-addressable trigger into this instance. Any number of triggers can be
        /// attached; each applies directly to whichever rows were last passed to
        /// <see cref="SetRows"/>.
        /// </summary>
        public void AttachRowTrigger(IRowEventTrigger trigger) => trigger.RowTriggered += OnRowTriggered;

        /// <summary>Tells this instance which rows row-addressable events should apply to.</summary>
        public void SetRows(IReadOnlyList<RouteRowViewModel> rows) => _rows = rows;

        private void OnRowTriggered(object? sender, RowEvent e)
        {
            if (_rows is null)
            {
                return;
            }

            ApplyRowEvent(_rows, e);
        }

        /// <summary>
        /// Applies a single row-addressable event: finds the row it targets (see
        /// <see cref="FindTargetIndex"/>), and - for Plotted/Arrived only - silently completes
        /// every earlier not-yet-complete row along the way. That catch-up is what lets one
        /// event bring the whole route up to date in a single step - e.g. after the app
        /// restarts mid-journey and several rows must be marked complete at once, rather than
        /// replaying each one individually. Jumping never needs to catch up earlier rows - by
        /// construction it only ever targets the row a prior Plotted event already brought
        /// current. CooldownElapsed is handled entirely separately (see
        /// <see cref="ApplyCooldownElapsed"/>) - not name-targeted at all, since the row it clears
        /// isn't the one its own SystemName names (see RowEventKind.CooldownElapsed). Reset is not
        /// row-targeted at all - it clears every row unconditionally and skips the rest of this
        /// method entirely. JumpCancelled is also not name-targeted - see
        /// <see cref="ApplyJumpCancelled"/>. LiveCarrierLocation is not a route-mutating event at
        /// all - it's ignored here; RouteViewModel has its own separate subscription to the same
        /// trigger for it (see that value's doc comment).
        /// </summary>
        private void ApplyRowEvent(IReadOnlyList<RouteRowViewModel> rows, RowEvent e)
        {
            if (e.Kind == RowEventKind.Reset)
            {
                foreach (var eachRow in rows)
                {
                    eachRow.Icon = RowIcon.None;
                    eachRow.Status = string.Empty;
                    eachRow.PhaseEndUtc = null;
                }
                _deferredTargetedSystemName = null;
                return;
            }

            if (e.Kind == RowEventKind.LiveCarrierLocation)
            {
                return;
            }

            if (e.Kind == RowEventKind.Targeted && rows.Any(r => r.Status is "Jumping" or "Cooldown"))
            {
                // See RowEventKind.Targeted's own doc comment - held back until the in-flight
                // cycle actually finishes, rather than potentially painting the wrong row.
                _deferredTargetedSystemName = e.SystemName;
                return;
            }

            if (e.Kind == RowEventKind.CooldownElapsed)
            {
                ApplyCooldownElapsed(rows);
                FlushDeferredTargeted(rows);
                return;
            }

            if (e.Kind == RowEventKind.JumpCancelled)
            {
                ApplyJumpCancelled(rows);
                return;
            }

            var targetIndex = FindTargetIndex(rows, e.Kind, e.SystemName);
            if (targetIndex < 0)
            {
                // A Targeted event whose system isn't in the route at all (e.g. the CMDR manually
                // targeted an off-route system, then plotted a multi-jump in-game route to
                // somewhere else entirely - the new FSDTarget for that route's first hop won't
                // match any row) means whatever row was previously shown as "Targeted" no longer
                // reflects where the ship is actually pointed. Clear that stale Status rather than
                // leaving it stuck - see ClearAnyTargeted's own doc comment for why the row's Icon
                // (still the correct next system to reach) is left untouched.
                if (e.Kind == RowEventKind.Targeted)
                {
                    ClearAnyTargeted(rows);
                }

                return;
            }

            if (e.Kind is RowEventKind.Plotted or RowEventKind.Arrived)
            {
                for (var i = 0; i < targetIndex; i++)
                {
                    if (rows[i].Icon != RowIcon.Complete)
                    {
                        rows[i].Icon = RowIcon.Complete;
                        rows[i].Status = string.Empty;
                        rows[i].PhaseEndUtc = null;
                    }
                }
            }

            var row = rows[targetIndex];
            switch (e.Kind)
            {
                case RowEventKind.Targeted:
                    SetTargeted(row);
                    break;

                case RowEventKind.Plotting:
                    if (row.Icon != RowIcon.Complete)
                    {
                        row.Icon = RowIcon.InProgress;
                    }
                    row.Status = "Plotting";
                    row.PhaseEndUtc = null;
                    break;

                case RowEventKind.Plotted:
                    if (row.Icon != RowIcon.Complete)
                    {
                        row.Icon = RowIcon.InProgress;
                    }
                    row.Status = "Plotted";
                    row.PhaseEndUtc = e.PhaseEndUtc;
                    break;

                case RowEventKind.Jumping:
                    row.Status = "Jumping";
                    row.PhaseEndUtc = e.PhaseEndUtc;
                    break;

                case RowEventKind.Arrived:
                    // The just-arrived row always goes straight to Complete with a blank status,
                    // and (if a next row exists) that row becomes the current in-progress one -
                    // both regardless of live vs. replay, since catching a route up to where the
                    // carrier actually is must work the same way whether that evidence is fresh
                    // or from history.
                    //
                    // Cooldown itself, though, is only ever set for a genuinely live arrival
                    // (e.IsLive) - never as a side effect of replaying history (e.g. the fresh
                    // catch-up a Captain assignment does). A row that's merely being caught up
                    // to "already arrived, some time ago" has no real cooldown left to show -
                    // its 5-minute window is either already over or, worse, impossible to place
                    // reliably against wall-clock time this long after the fact. Cooldown is
                    // reserved for the one situation it can be shown correctly: watching an
                    // actual jump happen in real time.
                    row.Icon = RowIcon.Complete;
                    row.Status = string.Empty;
                    row.PhaseEndUtc = null;
                    if (targetIndex + 1 < rows.Count)
                    {
                        var nextRow = rows[targetIndex + 1];
                        nextRow.Icon = RowIcon.InProgress;
                        if (e.IsLive)
                        {
                            nextRow.Status = "Cooldown";
                            nextRow.PhaseEndUtc = e.PhaseEndUtc;
                        }
                    }
                    break;
            }
        }

        /// <summary>Sets Status = "Targeted" on <paramref name="row"/> - shared by ApplyRowEvent's own Targeted case and FlushDeferredTargeted, which both need the identical mutation.</summary>
        private static void SetTargeted(RouteRowViewModel row)
        {
            if (row.Icon != RowIcon.Complete)
            {
                row.Icon = RowIcon.InProgress;
            }
            row.Status = "Targeted";
            row.PhaseEndUtc = null;
        }

        /// <summary>
        /// Applies whatever Targeted event was held back by ApplyRowEvent's own Targeted case,
        /// now that the in-flight cycle has actually finished (called immediately after
        /// CooldownElapsed is processed, regardless of whether that specific call found a row to
        /// clear - a route's very last row never gets a Cooldown to clear at all, but a deferred
        /// Targeted should still flush once its own CooldownElapsed timer fires). A safe no-op if
        /// nothing was deferred. If the deferred system name doesn't match any row (e.g. a manual
        /// "Set next system" override ran in the meantime, or the held-back target was itself an
        /// off-route system - see ClearAnyTargeted), any stale "Targeted" Status is cleared the
        /// same way the immediate (non-deferred) path does.
        /// </summary>
        private void FlushDeferredTargeted(IReadOnlyList<RouteRowViewModel> rows)
        {
            if (_deferredTargetedSystemName is not { } systemName)
            {
                return;
            }

            _deferredTargetedSystemName = null;

            var targetIndex = FindTargetIndex(rows, RowEventKind.Targeted, systemName);
            if (targetIndex >= 0)
            {
                SetTargeted(rows[targetIndex]);
            }
            else
            {
                ClearAnyTargeted(rows);
            }
        }

        /// <summary>
        /// Clears Status back to blank on whichever row currently shows "Targeted" - there is at
        /// most one at a time, the same single-row invariant ApplyCooldownElapsed/
        /// ApplyJumpCancelled rely on for their own searches. Icon is deliberately left untouched
        /// (unlike Reset): the row is still genuinely the next system the route expects to reach,
        /// even though the CMDR's most recent FSDTarget no longer points at it - only the stale
        /// "Targeted" label is wrong, not the row's own place in the route. A safe no-op if no row
        /// is currently showing it.
        /// </summary>
        private static void ClearAnyTargeted(IReadOnlyList<RouteRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.Status == "Targeted")
                {
                    row.Status = string.Empty;
                    row.PhaseEndUtc = null;
                    return;
                }
            }
        }

        /// <summary>
        /// Clears whichever row is currently showing "Cooldown" - there is only ever one such row
        /// at a time (set exclusively by Arrived's own "next row" step above), so this searches
        /// directly for it rather than looking it up via CooldownElapsed's own SystemName (the row
        /// the carrier arrived *at*, not the one Cooldown itself is showing on - see
        /// RowEventKind.CooldownElapsed). Deliberately *not* matched by name: a route that
        /// revisits the same system more than once would otherwise risk matching an earlier,
        /// already-completed visit's occurrence of that name instead of the current one, leaving
        /// the real Cooldown row stuck forever once its own timer fired and found the wrong
        /// target. A safe no-op - same stale/duplicate-event tolerance <see cref="FindTargetIndex"/>
        /// documents - if no row is currently showing Cooldown (e.g. a manual "Set next system"
        /// override already cleared it).
        /// </summary>
        private static void ApplyCooldownElapsed(IReadOnlyList<RouteRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.Icon == RowIcon.InProgress && row.Status == "Cooldown")
                {
                    row.Status = string.Empty;
                    row.PhaseEndUtc = null;
                    return;
                }
            }
        }

        /// <summary>
        /// JumpCancelled carries no SystemName (CarrierJumpCancelled has none in the journal),
        /// so it can't be matched by name like the other kinds - instead it reverts whichever
        /// row is currently in-progress with a Status of "Plotted" or "Jumping" (there is only
        /// ever one such row at a time) back to a blank status, leaving its Icon as InProgress
        /// so it's ready for a fresh CarrierJumpRequest. A safe no-op if no row currently
        /// matches - e.g. a stale/duplicate cancellation, or one arriving after the row already
        /// moved on some other way (a manual "Set next system" override, for instance).
        /// </summary>
        private static void ApplyJumpCancelled(IReadOnlyList<RouteRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.Icon == RowIcon.InProgress && row.Status is "Plotted" or "Jumping")
                {
                    row.Status = string.Empty;
                    row.PhaseEndUtc = null;
                    return;
                }
            }
        }

        /// <summary>
        /// Finds which row a row-addressable event targets. Plotted/Arrived/Targeted match by
        /// System text against any not-yet-complete row (any current status - matches the row a
        /// catch-up would otherwise skip past). Jumping and Plotting are both derived/precise
        /// follow-ups and are matched more strictly - by System text *and* the status their own
        /// predecessor left behind - so a stale/duplicate event firing after the row has already
        /// moved on is a safe no-op rather than corrupting a later state. Plotting requires blank
        /// (it's AutoPilotController's own first move for a row). Jumping accepts "Plotted"
        /// (Fleet Carrier mode's own predecessor), "Targeted" (Ship mode's normal predecessor,
        /// off FSDTarget), or blank (Ship mode fallback, in case FSDTarget was never observed for
        /// this hop - a jump should still be trackable even without it). CooldownElapsed does not
        /// use this method at all - see <see cref="ApplyCooldownElapsed"/>.
        /// </summary>
        private static int FindTargetIndex(IReadOnlyList<RouteRowViewModel> rows, RowEventKind kind, string systemName)
        {
            var allowedStatuses = kind switch
            {
                RowEventKind.Jumping => new[] { "Plotted", "Targeted", string.Empty },
                RowEventKind.Plotting => new[] { string.Empty },
                _ => null
            };

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Icon == RowIcon.Complete)
                {
                    continue;
                }

                if (allowedStatuses != null && Array.IndexOf(allowedStatuses, row.Status) < 0)
                {
                    continue;
                }

                if (string.Equals(row.SystemText, systemName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
