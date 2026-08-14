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
        /// <see cref="FindTargetIndex"/>), and - for a Plotted/Arrived event - silently completes
        /// every earlier not-yet-complete row along the way, live or replayed alike. Plotted/
        /// Arrived are the two kinds that represent authoritative, confirmed progress (a real
        /// carrier jump request, or - in Ship mode - a real Location/FSDJump-derived position; see
        /// RowEventKind.Arrived) rather than a mere intention, so sweeping every earlier row into
        /// Complete is correct whether that confirmation came from history or from something that
        /// just happened. Targeted, by contrast, never sweeps anything - see FindTargetIndex's own
        /// "not found" handling below and SetTargeted's Icon-reverting behaviour: a locked jump
        /// target is only ever an intention, never proof anything was actually reached. Jumping
        /// never needs to catch up earlier rows either - by construction it only ever targets the
        /// row a prior Plotted/Targeted event already brought current. CooldownElapsed is handled
        /// entirely separately (see <see cref="ApplyCooldownElapsed"/>) - not name-targeted at
        /// all, since the row it clears isn't the one its own SystemName names (see
        /// RowEventKind.CooldownElapsed). Reset is not row-targeted at all - it clears every row
        /// unconditionally and skips the rest of this method entirely. JumpCancelled/TargetCleared
        /// are likewise not name-targeted - see <see cref="ApplyJumpCancelled"/> and
        /// <see cref="ClearAnyTargeted"/>. LiveCarrierLocation is not a route-mutating event at
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

            if (e.Kind == RowEventKind.TargetCleared)
            {
                // Not name-targeted (NavRouteClear carries no system name) - the CMDR explicitly
                // cleared their plotted route, so whatever row was showing "Targeted" no longer
                // reflects reality.
                _deferredTargetedSystemName = null;
                ClearAnyTargeted(rows);
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
                // reflects where the ship is actually pointed. Clear that stale Status (and any
                // Icon it was only showing because it was Targeted) rather than leaving it stuck -
                // see ClearAnyTargeted's own doc comment.
                if (e.Kind == RowEventKind.Targeted)
                {
                    ClearAnyTargeted(rows);
                }

                return;
            }

            // Plotted/Arrived both represent authoritative, confirmed progress - a real carrier
            // jump request, or (Ship mode) a real Location/FSDJump-derived position - never a mere
            // intention (that's what Targeted is for, and it never reaches this branch). Whichever
            // row that confirmation names, every not-yet-complete row before it is swept into
            // Complete too, live or replayed alike: the pasted route's own order is the only
            // ordering RouteSequencer has to go on, and a real jump/request landing further along
            // it than expected is still real, confirmed proof the CMDR passed that point in the
            // route - not an invented history. A genuine off-route deviation (skipping rows that
            // were never really intended) is what the manual "Set next system" override (§4.2)
            // exists to correct explicitly.
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
                    SetTargeted(rows, row);
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
                    // carrier/ship actually is must work the same way whether that evidence is
                    // fresh or from history.
                    //
                    // Cooldown itself, though, is only ever set for a genuinely live arrival
                    // (e.IsLive) *with* a known clear time (e.PhaseEndUtc) - never as a side
                    // effect of replaying history (e.g. the fresh catch-up a Captain/tracked-
                    // instance assignment does), and never for Ship mode's Location-derived
                    // Arrived (see ShipRouteJournalWatcher), which - unlike a real just-completed
                    // hyperspace jump (FSDJump) - carries no PhaseEndUtc at all: a mere positional
                    // snapshot (session start, a relog, docking somewhere) isn't proof a jump just
                    // happened, so it must never imply a cooldown is now counting down. A row
                    // that's merely being caught up to "already arrived, some time ago" has no
                    // real cooldown left to show - its window is either already over or,
                    // worse, impossible to place reliably against wall-clock time this long after
                    // the fact. Cooldown is reserved for the one situation it can be shown
                    // correctly: watching an actual jump completion happen in real time.
                    row.Icon = RowIcon.Complete;
                    row.Status = string.Empty;
                    row.PhaseEndUtc = null;
                    if (targetIndex + 1 < rows.Count)
                    {
                        var nextRow = rows[targetIndex + 1];
                        nextRow.Icon = RowIcon.InProgress;
                        if (e.IsLive && e.PhaseEndUtc.HasValue)
                        {
                            nextRow.Status = "Cooldown";
                            nextRow.PhaseEndUtc = e.PhaseEndUtc;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Sets Status = "Targeted" on <paramref name="row"/> - shared by ApplyRowEvent's own
        /// Targeted case and FlushDeferredTargeted, which both need the identical mutation. There
        /// can only ever be one Targeted row at a time: FindTargetIndex's own Targeted matching has
        /// no status precondition (unlike Jumping/Plotting), so it can land on a *different* row
        /// than whichever one currently holds "Targeted" (e.g. a repeated system name, or the CMDR
        /// re-targeting a different in-route waypoint without the old target ever resolving) -
        /// clearing every other Targeted row first, unconditionally, is what actually guarantees
        /// the invariant, rather than relying on each caller to have already done so (ClearAnyTargeted's
        /// own "not found in route" case was one gap; this closes the "found, but a different row"
        /// one too). See ClearAnyTargeted's own doc comment for why clearing a *different* row's
        /// Targeted status here also reverts its Icon rather than leaving it InProgress.
        /// </summary>
        private static void SetTargeted(IReadOnlyList<RouteRowViewModel> rows, RouteRowViewModel row)
        {
            ClearOtherTargeted(rows, row);

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
                SetTargeted(rows, rows[targetIndex]);
            }
            else
            {
                ClearAnyTargeted(rows);
            }
        }

        /// <summary>
        /// The route's one genuine "current" row - the first not-yet-complete row in route order.
        /// This is the only row whose InProgress Icon is ever legitimate on its own: real,
        /// confirmed progress (RowEventKind.Arrived's own sweep, see ApplyRowEvent) always
        /// advances Complete as a contiguous prefix, so "first non-Complete row" and "the row
        /// progress has actually reached" are definitionally always the same row - recomputed on
        /// demand here rather than tracked as separate mutable state. Every *other* not-yet-
        /// complete row's Icon is None unless something else (Targeted) is separately showing on
        /// it - see ClearOtherTargeted.
        /// </summary>
        private static RouteRowViewModel? GenuineCurrentRow(IReadOnlyList<RouteRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.Icon != RowIcon.Complete)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>
        /// Clears Status back to blank on every row currently showing "Targeted" except
        /// <paramref name="except"/> (if given) - shared by SetTargeted (clearing every *other*
        /// Targeted row before setting a new one) and ClearAnyTargeted (clearing all of them, with
        /// no exception, e.g. an off-route re-target or a NavRouteClear). There should only ever
        /// be at most one Targeted row to begin with (see SetTargeted's own doc comment for how
        /// that's enforced going forward), but this deliberately doesn't stop at the first match,
        /// purely as defense-in-depth against that invariant ever slipping.
        ///
        /// A row that loses "Targeted" this way also has its Icon reverted to None, *unless* it's
        /// also <see cref="GenuineCurrentRow"/> - a row was only ever showing InProgress in the
        /// first place *because* it was Targeted is not, on its own, proof the CMDR actually
        /// reached it; only real, confirmed progress earns a row its own InProgress Icon. This is
        /// what stops a row that was targeted and then abandoned (re-targeted elsewhere, targeted
        /// something off-route, or the route explicitly cleared) from being left looking like "the
        /// next system the route expects to reach" (the Play triangle) once it no longer is - the
        /// genuine current row's own Icon, if it's a *different* row, is completely unaffected.
        /// </summary>
        private static void ClearOtherTargeted(IReadOnlyList<RouteRowViewModel> rows, RouteRowViewModel? except)
        {
            var genuineCurrent = GenuineCurrentRow(rows);

            foreach (var row in rows)
            {
                if (ReferenceEquals(row, except) || row.Status != "Targeted")
                {
                    continue;
                }

                row.Status = string.Empty;
                row.PhaseEndUtc = null;

                if (!ReferenceEquals(row, genuineCurrent) && row.Icon != RowIcon.Complete)
                {
                    row.Icon = RowIcon.None;
                }
            }
        }

        /// <summary>Clears every row currently showing "Targeted", with no exception - see ClearOtherTargeted. A safe no-op if no row is currently showing it.</summary>
        private static void ClearAnyTargeted(IReadOnlyList<RouteRowViewModel> rows) => ClearOtherTargeted(rows, except: null);

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
