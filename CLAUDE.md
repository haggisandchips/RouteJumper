# CLAUDE.md

See @SPEC.md for the full functional/technical specification.

## Non-negotiable architecture rule
The sequencing engine (Sequencing/) is event-driven by design: RouteSequencer
executes one action per ISequenceTrigger.Triggered event. Do not reintroduce
hardcoded delays (Task.Delay, Thread.Sleep) into the sequencing logic.

## Current work
Restyling the UI with MaterialDesignInXamlToolkit. See SPEC.md "Styling" section.