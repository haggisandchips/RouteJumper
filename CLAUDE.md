# CLAUDE.md

See @SPEC.md for the full functional/technical specification.

## Non-negotiable architecture rule
The route-progress logic (Sequencing/) is event-driven by design: RouteSequencer
executes one action per IRowEventTrigger.RowTriggered event. Do not reintroduce
hardcoded delays (Task.Delay, Thread.Sleep) into this logic.
