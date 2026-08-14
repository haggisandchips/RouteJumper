using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.Sequencing
{
    public class RouteSequencerTests
    {
        private static RouteRowViewModel Row(int number, string system) => new()
        {
            Number = number,
            SystemText = system
        };

        private static (RouteSequencer Sequencer, ManualRowEventTrigger Trigger, List<RouteRowViewModel> Rows) CreateWithRows(params string[] systems)
        {
            var rows = systems.Select((s, i) => Row(i + 1, s)).ToList();
            var sequencer = new RouteSequencer();
            sequencer.SetRows(rows);
            var trigger = new ManualRowEventTrigger();
            sequencer.AttachRowTrigger(trigger);
            return (sequencer, trigger, rows);
        }

        [Fact]
        public void NoRowsSet_EventIsIgnoredWithoutThrowing()
        {
            var sequencer = new RouteSequencer();
            var trigger = new ManualRowEventTrigger();
            sequencer.AttachRowTrigger(trigger);

            trigger.Fire(RowEventKind.Plotted, "Sol");
        }

        [Fact]
        public void Reset_ClearsEveryRowRegardlessOfPriorState()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri", "Wolf 359");
            rows[0].Icon = RowIcon.Complete;
            rows[1].Icon = RowIcon.InProgress;
            rows[1].Status = "Cooldown";
            rows[1].PhaseEndUtc = DateTime.UtcNow;
            rows[2].Icon = RowIcon.InProgress;

            trigger.Fire(RowEventKind.Reset, string.Empty);

            Assert.All(rows, r =>
            {
                Assert.Equal(RowIcon.None, r.Icon);
                Assert.Equal(string.Empty, r.Status);
                Assert.Null(r.PhaseEndUtc);
            });
        }

        [Fact]
        public void LiveCarrierLocation_IsIgnoredByRouteSequencer()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            rows[0].Icon = RowIcon.InProgress;

            trigger.Fire(RowEventKind.LiveCarrierLocation, "Sol", isLive: true);

            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
            Assert.Equal(string.Empty, rows[0].Status);
        }

        [Fact]
        public void Targeted_SetsTargetRowInProgressAndTargetedStatus()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");

            trigger.Fire(RowEventKind.Targeted, "Sol");

            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
            Assert.Equal("Targeted", rows[0].Status);
        }

        [Fact]
        public void Targeted_WhileAnotherRowIsJumping_IsDeferredNotAppliedImmediately()
        {
            // Real journal data: the game's own multi-route auto-target behaviour typically
            // fires the *next* hop's FSDTarget while the *current* hop is still charging.
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Jumping";

            trigger.Fire(RowEventKind.Targeted, "Deciat");

            Assert.Equal(RowIcon.None, rows[1].Icon);
            Assert.Equal(string.Empty, rows[1].Status);
        }

        [Fact]
        public void Targeted_WhileAnotherRowShowingCooldown_IsDeferred()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat", "Wolf 359");
            rows[1].Icon = RowIcon.InProgress;
            rows[1].Status = "Cooldown";

            trigger.Fire(RowEventKind.Targeted, "Wolf 359");

            Assert.Equal(RowIcon.None, rows[2].Icon);
            Assert.Equal(string.Empty, rows[2].Status);
        }

        [Fact]
        public void Targeted_DeferredWhileJumping_AppliesOnceCooldownElapsedFires()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Jumping";
            trigger.Fire(RowEventKind.Targeted, "Deciat"); // deferred - row 0 still Jumping

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: true);
            Assert.Equal("Cooldown", rows[1].Status); // still not "Targeted" yet

            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal("Targeted", rows[1].Status);
        }

        [Fact]
        public void Targeted_SystemNotInRoute_ClearsExistingTargetedStatusButLeavesIconInProgress()
        {
            // Real-world scenario: the CMDR manually targets an off-route system, realises it's
            // too far for one jump, and plots a multi-jump in-game route to somewhere else
            // instead - the new route's own first-hop FSDTarget names an intermediate system the
            // pasted route never mentions at all. The row previously shown "Targeted" must not be
            // left stuck showing a target the ship is no longer actually pointed at.
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");
            trigger.Fire(RowEventKind.Targeted, "Sol");
            Assert.Equal("Targeted", rows[0].Status);

            trigger.Fire(RowEventKind.Targeted, "Some Intermediate System");

            Assert.Equal(string.Empty, rows[0].Status);
            // Still the correct next system to reach - only the stale label was wrong.
            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
        }

        [Fact]
        public void Targeted_SystemNotInRoute_NoRowCurrentlyTargeted_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");

            trigger.Fire(RowEventKind.Targeted, "Some Intermediate System");

            Assert.All(rows, r =>
            {
                Assert.Equal(RowIcon.None, r.Icon);
                Assert.Equal(string.Empty, r.Status);
            });
        }

        [Fact]
        public void Targeted_DeferredSystemNotInRoute_ClearsExistingTargetedOnceFlushed()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Targeted";
            rows[1].Icon = RowIcon.InProgress;
            rows[1].Status = "Jumping"; // holds the Targeted event back until CooldownElapsed fires
            trigger.Fire(RowEventKind.Targeted, "Some Intermediate System"); // deferred

            trigger.Fire(RowEventKind.CooldownElapsed, "Deciat");

            Assert.Equal(string.Empty, rows[0].Status);
            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
        }

        [Fact]
        public void Reset_ClearsAnyDeferredTargeted()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Jumping";
            trigger.Fire(RowEventKind.Targeted, "Deciat"); // deferred

            trigger.Fire(RowEventKind.Reset, string.Empty);
            // Would have flushed the stale deferred Targeted onto row 1 if Reset hadn't cleared it.
            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal(string.Empty, rows[1].Status);
        }

        [Fact]
        public void Plotting_SetsTargetRowInProgressAndPlottingStatusWithNoPhaseEnd()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");

            trigger.Fire(RowEventKind.Plotting, "Sol");

            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
            Assert.Equal("Plotting", rows[0].Status);
            Assert.Null(rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Plotting_RequiresRowToCurrentlyBeBlank()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Cooldown";

            trigger.Fire(RowEventKind.Plotting, "Sol");

            Assert.Equal("Cooldown", rows[0].Status);
        }

        [Fact]
        public void Plotting_StaleEventForRowAlreadyPlotted_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Plotted";
            var phaseEnd = DateTime.UtcNow.AddMinutes(3);
            rows[0].PhaseEndUtc = phaseEnd;

            trigger.Fire(RowEventKind.Plotting, "Sol");

            Assert.Equal("Plotted", rows[0].Status);
            Assert.Equal(phaseEnd, rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Plotting_ThenPlotted_OverwritesPlottingStatus()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            var phaseEnd = DateTime.UtcNow.AddMinutes(3);

            trigger.Fire(RowEventKind.Plotting, "Sol");
            trigger.Fire(RowEventKind.Plotted, "Sol", phaseEndUtc: phaseEnd);

            Assert.Equal("Plotted", rows[0].Status);
            Assert.Equal(phaseEnd, rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Plotted_SetsTargetRowInProgressAndPlottedStatus()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            var phaseEnd = DateTime.UtcNow.AddMinutes(3);

            trigger.Fire(RowEventKind.Plotted, "Sol", phaseEndUtc: phaseEnd);

            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
            Assert.Equal("Plotted", rows[0].Status);
            Assert.Equal(phaseEnd, rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Plotted_MatchesSystemNameCaseInsensitively()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");

            trigger.Fire(RowEventKind.Plotted, "sOL");

            Assert.Equal("Plotted", rows[0].Status);
        }

        [Fact]
        public void Plotted_CompletesEveryEarlierNotYetCompleteRow()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri", "Wolf 359");

            trigger.Fire(RowEventKind.Plotted, "Wolf 359");

            Assert.Equal(RowIcon.Complete, rows[0].Icon);
            Assert.Equal(string.Empty, rows[0].Status);
            Assert.Equal(RowIcon.Complete, rows[1].Icon);
            Assert.Equal(RowIcon.InProgress, rows[2].Icon);
            Assert.Equal("Plotted", rows[2].Status);
        }

        [Fact]
        public void Plotted_UnknownSystemName_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");

            trigger.Fire(RowEventKind.Plotted, "Nowhere");

            Assert.All(rows, r => Assert.Equal(RowIcon.None, r.Icon));
        }

        [Fact]
        public void Plotted_RepeatedSystemName_MatchesFirstNotCompleteOccurrence()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Jameson Memorial", "Sol");

            // First Plotted for "Sol" should hit row 0, not row 2.
            trigger.Fire(RowEventKind.Plotted, "Sol");

            Assert.Equal("Plotted", rows[0].Status);
            Assert.Equal(string.Empty, rows[2].Status);
        }

        [Fact]
        public void Jumping_RequiresRowToCurrentlyBePlotted()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Plotted";

            var jumpingEnd = DateTime.UtcNow.AddHours(1);
            trigger.Fire(RowEventKind.Jumping, "Sol", phaseEndUtc: jumpingEnd);

            Assert.Equal("Jumping", rows[0].Status);
            Assert.Equal(jumpingEnd, rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Jumping_StaleEventForRowInCooldown_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Cooldown"; // already moved on to a different phase

            trigger.Fire(RowEventKind.Jumping, "Sol");

            Assert.Equal("Cooldown", rows[0].Status);
        }

        [Fact]
        public void Jumping_BlankRow_AppliesAsShipModeFallback()
        {
            // Ship mode: StartJump can legitimately fire without a preceding Targeted event
            // having been observed for this specific hop (e.g. the target was set in a
            // now-rotated-out previous journal file) - Jumping must still apply rather than
            // leaving the row stuck forever.
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = string.Empty;

            trigger.Fire(RowEventKind.Jumping, "Sol");

            Assert.Equal("Jumping", rows[0].Status);
        }

        [Fact]
        public void Jumping_RowAlreadyTargeted_Applies()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Targeted";

            trigger.Fire(RowEventKind.Jumping, "Sol");

            Assert.Equal("Jumping", rows[0].Status);
        }

        [Fact]
        public void Jumping_RowAlreadyComplete_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.Complete;
            rows[0].Status = string.Empty;

            trigger.Fire(RowEventKind.Jumping, "Sol");

            Assert.Equal(string.Empty, rows[0].Status);
        }

        [Fact]
        public void Arrived_MarksArrivedRowCompleteAndBlank()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Jumping";
            rows[0].PhaseEndUtc = DateTime.UtcNow;

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: false);

            Assert.Equal(RowIcon.Complete, rows[0].Icon);
            Assert.Equal(string.Empty, rows[0].Status);
            Assert.Null(rows[0].PhaseEndUtc);
        }

        [Fact]
        public void Arrived_MakesNextRowInProgress()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: false);

            Assert.Equal(RowIcon.InProgress, rows[1].Icon);
        }

        [Fact]
        public void Arrived_Live_SetsCooldownOnNextRow()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            var cooldownEnd = DateTime.UtcNow.AddMinutes(5);

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: true, phaseEndUtc: cooldownEnd);

            Assert.Equal("Cooldown", rows[1].Status);
            Assert.Equal(cooldownEnd, rows[1].PhaseEndUtc);
        }

        [Fact]
        public void Arrived_Replayed_DoesNotSetCooldownOnNextRow()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: false, phaseEndUtc: DateTime.UtcNow.AddMinutes(5));

            Assert.Equal(RowIcon.InProgress, rows[1].Icon);
            Assert.Equal(string.Empty, rows[1].Status);
            Assert.Null(rows[1].PhaseEndUtc);
        }

        [Fact]
        public void Arrived_NoNextRow_LeavesNothingInProgress()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");

            trigger.Fire(RowEventKind.Arrived, "Sol", isLive: true);

            Assert.Equal(RowIcon.Complete, rows[0].Icon);
        }

        [Fact]
        public void CooldownElapsed_ClearsCooldownOnRowAfterArrivedRow()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            rows[0].Icon = RowIcon.Complete;
            rows[1].Icon = RowIcon.InProgress;
            rows[1].Status = "Cooldown";
            rows[1].PhaseEndUtc = DateTime.UtcNow;

            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal(string.Empty, rows[1].Status);
            Assert.Null(rows[1].PhaseEndUtc);
            Assert.Equal(RowIcon.InProgress, rows[1].Icon);
        }

        [Fact]
        public void CooldownElapsed_NoRowShowingCooldown_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            rows[0].Icon = RowIcon.Complete;
            rows[1].Icon = RowIcon.InProgress;
            rows[1].Status = string.Empty;

            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal(string.Empty, rows[1].Status);
            Assert.Equal(RowIcon.InProgress, rows[1].Icon);
        }

        [Fact]
        public void CooldownElapsed_RepeatedSystemName_ClearsTheRowActuallyShowingCooldown()
        {
            // Regression test: matching CooldownElapsed's own SystemName against the *first*
            // Complete row with that name (rather than searching for whichever row is actually
            // showing "Cooldown" directly) could match a much earlier, already-finished visit,
            // leaving the real Cooldown row stuck forever once its one-shot timer fired against
            // the wrong target.
            var (_, trigger, rows) = CreateWithRows("Sol", "Deciat", "Sol", "Wolf 359");
            rows[0].Icon = RowIcon.Complete; // first (earlier) visit to Sol, long done
            rows[1].Icon = RowIcon.Complete; // Deciat, also done
            rows[2].Icon = RowIcon.Complete; // second visit to Sol, just arrived
            rows[3].Icon = RowIcon.InProgress;
            rows[3].Status = "Cooldown"; // the real Cooldown row, after the *second* Sol

            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal(string.Empty, rows[3].Status);
        }

        [Fact]
        public void CooldownElapsed_NextRowNoLongerShowingCooldown_IsNoOp()
        {
            // e.g. a manual "Set next system" override ran in between.
            var (_, trigger, rows) = CreateWithRows("Sol", "Alpha Centauri");
            rows[0].Icon = RowIcon.Complete;
            rows[1].Icon = RowIcon.Complete; // moved on past Cooldown some other way
            rows[1].Status = string.Empty;

            trigger.Fire(RowEventKind.CooldownElapsed, "Sol");

            Assert.Equal(RowIcon.Complete, rows[1].Icon); // unaffected, still Complete
            Assert.Equal(string.Empty, rows[1].Status);
        }

        [Fact]
        public void JumpCancelled_RevertsPlottedRowToBlankStatus()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Plotted";
            rows[0].PhaseEndUtc = DateTime.UtcNow;

            trigger.Fire(RowEventKind.JumpCancelled, string.Empty);

            Assert.Equal(RowIcon.InProgress, rows[0].Icon);
            Assert.Equal(string.Empty, rows[0].Status);
            Assert.Null(rows[0].PhaseEndUtc);
        }

        [Fact]
        public void JumpCancelled_RevertsJumpingRowToBlankStatus()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Jumping";

            trigger.Fire(RowEventKind.JumpCancelled, string.Empty);

            Assert.Equal(string.Empty, rows[0].Status);
        }

        [Fact]
        public void JumpCancelled_NoMatchingRow_IsNoOp()
        {
            var (_, trigger, rows) = CreateWithRows("Sol");
            rows[0].Icon = RowIcon.InProgress;
            rows[0].Status = "Cooldown";

            trigger.Fire(RowEventKind.JumpCancelled, string.Empty);

            Assert.Equal("Cooldown", rows[0].Status);
        }

        [Fact]
        public void MultipleTriggersAttached_BothApplyToSameRows()
        {
            var rows = new List<RouteRowViewModel> { Row(1, "Sol") };
            var sequencer = new RouteSequencer();
            sequencer.SetRows(rows);
            var triggerA = new ManualRowEventTrigger();
            var triggerB = new ManualRowEventTrigger();
            sequencer.AttachRowTrigger(triggerA);
            sequencer.AttachRowTrigger(triggerB);

            triggerA.Fire(RowEventKind.Plotted, "Sol");
            triggerB.Fire(RowEventKind.Jumping, "Sol");

            Assert.Equal("Jumping", rows[0].Status);
        }
    }

    public class ManualRowEventTriggerTests
    {
        [Fact]
        public void Fire_RaisesRowTriggeredWithGivenValues()
        {
            var trigger = new ManualRowEventTrigger();
            RowEvent? received = null;
            trigger.RowTriggered += (_, e) => received = e;

            var phaseEnd = DateTime.UtcNow;
            trigger.Fire(RowEventKind.Plotted, "Sol", isLive: true, phaseEndUtc: phaseEnd);

            Assert.NotNull(received);
            Assert.Equal(RowEventKind.Plotted, received!.Kind);
            Assert.Equal("Sol", received.SystemName);
            Assert.True(received.IsLive);
            Assert.Equal(phaseEnd, received.PhaseEndUtc);
        }

        [Fact]
        public void Fire_DefaultsIsLiveFalseAndPhaseEndNull()
        {
            var trigger = new ManualRowEventTrigger();
            RowEvent? received = null;
            trigger.RowTriggered += (_, e) => received = e;

            trigger.Fire(RowEventKind.Reset, string.Empty);

            Assert.False(received!.IsLive);
            Assert.Null(received.PhaseEndUtc);
        }

        [Fact]
        public void Fire_NoSubscribers_DoesNotThrow()
        {
            var trigger = new ManualRowEventTrigger();
            trigger.Fire(RowEventKind.Reset, string.Empty);
        }
    }
}
