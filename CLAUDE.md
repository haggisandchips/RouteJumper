# CLAUDE.md

See @SPEC.md for the full functional/technical specification — it's an
index into the numbered sub-specs under `specs/`, with section numbers
(§N) preserved across files.

## Non-negotiable architecture rule
The route-progress logic (Sequencing/) is event-driven by design: RouteSequencer
executes one action per IRowEventTrigger.RowTriggered event. Do not reintroduce
hardcoded delays (Task.Delay, Thread.Sleep) into this logic.

## Non-negotiable git rule
Never run `git push` unless the user's current message explicitly asks for a
push in that turn. Committing is separately authorized and does not imply
push authorization.

## GitHub
When merging a branch, don't force a merge commit — let the merge fast-forward
whenever the target is a direct ancestor (plain `git merge`, not `--no-ff`).

## Documentation
Every feature update must be documented in the relevant section of @SPEC.md,
and the user documentation. Keep both terse and to the point and for user
documentation especially stick to what/how but not why.