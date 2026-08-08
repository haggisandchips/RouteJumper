using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Applies row-addressable events (see <see cref="RowEvent"/>) - originating from a
    /// Captain's journal (see CarrierRouteJournalWatcher) - to a route's rows. Event-driven by
    /// design: every state change is a direct response to an <see cref="IRowEventTrigger"/>
    /// event, never a timer or other hardcoded delay.
    /// </summary>
    public class RouteSequencer
    {
        private IReadOnlyList<RouteRowViewModel>? _rows;

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
        /// <see cref="ApplyCooldownElapsed"/>) since the Cooldown status it clears lives on the
        /// row *after* the one its SystemName names, not on that row itself. Reset is not
        /// row-targeted at all - it clears every row unconditionally and skips the rest of this
        /// method entirely. JumpCancelled is also not name-targeted - see
        /// <see cref="ApplyJumpCancelled"/>. LiveCarrierLocation is not a route-mutating event at
        /// all - it's ignored here; RouteViewModel has its own separate subscription to the same
        /// trigger for it (see that value's doc comment).
        /// </summary>
        private static void ApplyRowEvent(IReadOnlyList<RouteRowViewModel> rows, RowEvent e)
        {
            if (e.Kind == RowEventKind.Reset)
            {
                foreach (var eachRow in rows)
                {
                    eachRow.Icon = RowIcon.None;
                    eachRow.Status = string.Empty;
                }
                return;
            }

            if (e.Kind == RowEventKind.LiveCarrierLocation)
            {
                return;
            }

            if (e.Kind == RowEventKind.CooldownElapsed)
            {
                ApplyCooldownElapsed(rows, e.SystemName);
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
                    }
                }
            }

            var row = rows[targetIndex];
            switch (e.Kind)
            {
                case RowEventKind.Plotted:
                    if (row.Icon != RowIcon.Complete)
                    {
                        row.Icon = RowIcon.InProgress;
                    }
                    row.Status = "Plotted";
                    break;

                case RowEventKind.Jumping:
                    row.Status = "Jumping";
                    break;

                case RowEventKind.Arrived:
                    // Cooldown belongs to the row that's actually waiting on it - the next one -
                    // not the row that just finished. The just-arrived row goes straight to
                    // Complete with a blank status; if there's no next row, nothing is put into
                    // Cooldown at all.
                    row.Icon = RowIcon.Complete;
                    row.Status = string.Empty;
                    if (targetIndex + 1 < rows.Count)
                    {
                        var nextRow = rows[targetIndex + 1];
                        nextRow.Icon = RowIcon.InProgress;
                        nextRow.Status = "Cooldown";
                    }
                    break;
            }
        }

        /// <summary>
        /// CooldownElapsed's SystemName names the row the carrier arrived *at* (the same name
        /// Arrived above used) - but the Cooldown status it needs to clear was put on the row
        /// *after* that one, not on the arrived-at row itself. So this looks up the (by now
        /// Complete) arrived-at row by name first, then clears Cooldown on the row immediately
        /// after it, if that row is still showing it. A safe no-op - same stale/duplicate-event
        /// tolerance <see cref="FindTargetIndex"/> documents - if no such row is found, or the
        /// row after it has already moved past Cooldown (e.g. a manual "Set next system"
        /// override ran in between).
        /// </summary>
        private static void ApplyCooldownElapsed(IReadOnlyList<RouteRowViewModel> rows, string arrivedSystemName)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Icon != RowIcon.Complete ||
                    !string.Equals(rows[i].SystemText, arrivedSystemName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < rows.Count && rows[i + 1].Icon == RowIcon.InProgress && rows[i + 1].Status == "Cooldown")
                {
                    rows[i + 1].Status = string.Empty;
                }

                return;
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
                    return;
                }
            }
        }

        /// <summary>
        /// Finds which row a row-addressable event targets. Plotted/Arrived match by System
        /// text against any not-yet-complete row (any current status - matches the row a
        /// catch-up would otherwise skip past). Jumping is a derived follow-up and is matched
        /// more precisely - by System text *and* the exact status Plotted left behind - so a
        /// stale/duplicate event firing after the row has already moved on is a safe no-op
        /// rather than corrupting a later state. CooldownElapsed does not use this method at
        /// all - see <see cref="ApplyCooldownElapsed"/>.
        /// </summary>
        private static int FindTargetIndex(IReadOnlyList<RouteRowViewModel> rows, RowEventKind kind, string systemName)
        {
            var requireStatus = kind == RowEventKind.Jumping ? "Plotted" : null;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Icon == RowIcon.Complete)
                {
                    continue;
                }

                if (requireStatus != null && row.Status != requireStatus)
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
