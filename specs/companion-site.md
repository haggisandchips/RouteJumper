[← Back to spec index](../SPEC.md)

## 13. Companion Site

A small, mobile-friendly companion website (Angular, statically hosted
via GitHub Pages alongside the docs — `docs/` at `/docs`, the companion
app at `/app`, both deployed together by `.github/workflows/pages.yml`)
showing a live feed of an in-progress Auto Pilot run (§4.7), so the CMDR
can check in from a phone - read-only apart from a per-event **Delete**
button (a plain `mat-icon-button` on each feed row, no confirmation)
that permanently removes that event doc from Firestore, letting the
CMDR tidy an event they don't want cluttering the feed; every other
viewer of the same link sees the removal live via the same
`onSnapshot` subscription that shows new events.

- **A single, shared Firebase project**: every install publishes to, and
  every site visit reads from, the same Firestore project — no
  per-install setup. Its public web config is already committed
  (`app/src/environments/environment*.ts`, and the matching `ProjectId`
  constant in `CompanionSessionPublisher.cs`), safe to commit since it
  identifies rather than grants access to the project (see Privacy
  model). Isolation between CMDRs is entirely by each session's own
  unguessable id. Only relevant to swap when forking this project
  against a different Firebase project (`app/README.md`).
- **Base URL**: `{CompanionSiteBaseUrl}/#/session/{sessionId}`, where
  `CompanionSiteBaseUrl` (`routejumper.conf`) defaults to the deployed
  site — pointed at a local `ng serve` address while developing the
  Angular app itself, no desktop rebuild required. Re-read fresh every
  time Auto Pilot is engaged.
- **Privacy model**: just an unguessable session UUID in the URL, not
  real authentication. The Firestore collection can never be
  listed/enumerated (`app/firestore.rules`), so network access alone
  doesn't reveal a session id you weren't given. No sign-in on either
  side — the .NET app writes via plain unauthenticated HTTPS to the
  Firestore REST API; the Angular app reads via the public `firebase` JS
  SDK.
- **Trigger**: a QR-code/link button appears beside Auto Pilot the
  instant a session starts successfully — automatic on engaging Auto
  Pilot, never a separate opt-in. Hidden otherwise, including a failed
  start (logged, not surfaced — cosmetic, non-critical feature), and
  hidden (not merely disabled) for the duration of any macro execution —
  Auto Pilot's own Captain plot/Engineer refuel, or a manual Play/Step
  from the Controls tab, since all three share the same playback channel
  (§4.7) — because clicking "Open in browser" mid-script risks stealing
  focus from the target Elite Dangerous window, which counts as a
  playback failure there. An already-open popup closes automatically the
  instant the button would hide. Clicking it opens a popup with the QR
  code and a "Copy Link" button (same copy-with-confirmation-sound
  convention as §4.6/§5.3).
- **Session lifecycle**: the session id is deterministic, not a fresh
  random UUID every engage — derived from a UUID generated once when the
  app starts plus a hash of the route's own system names. Re-engaging
  Auto Pilot on an unchanged route reuses the same id (so the same QR
  code/link keeps working across a Stop/re-engage cycle — useful since
  these routes can take days), reactivating the header doc's `status`
  back to `active` and leaving any previously-published events in place,
  so the feed reads as one continuous history for that route. A route
  edit (a different hash) or an app restart (a fresh startup UUID)
  always produces a fresh session/QR code. A session's header doc
  tracks `status`: `active` while running, `completed` on a normal stop
  (manual or route-complete), `panicked` on a panic-mode stop (§4.7) —
  shown on the companion site so a viewer can tell a session ended.
- **Housekeeping**: retention is deliberately short — self-managed by the
  desktop app (`CompanionSessionPublisher`/`CompanionSessionStore`), not
  Firestore's own TTL (which needs the paid Blaze plan even for a single
  delete, unlike an ordinary client-triggered delete on the free Spark
  plan). Two windows, tracked in one local SQLite record (the desktop app
  is the only writer, so the only thing that can know which sessions
  exist):
  - `StartSessionAsync` records a **fixed, unconditional 72-hour**
    deletion deadline at session creation — not configurable, applying
    regardless of whether the run ever properly ends (covers an
    abandoned session: crash, force-quit, `EndSession` never reached).
  - `EndSession` shortens that deadline to
    `CompanionSessionRetentionHours` (`routejumper.conf`, default **1
    hour**) after the run ends, clamped to never exceed the 72h ceiling.

  Once per launch, every session actually due is deleted via plain
  Firestore `delete` calls (free daily quota) and only then forgotten
  locally — a failed attempt retries in full next launch. Cleanup
  deletes every event doc first, then the header (deleting a header
  doesn't cascade), counting the session cleaned up only once both
  succeed.
- **Published events**: four kinds, each best-effort fire-and-forget
  (a dropped publish is logged, category `Companion`, never retried or
  surfaced):
  - **Plotted** — a jump was plotted (`RowEventKind.Plotted`).
  - **Arrived** — the carrier arrived (`RowEventKind.Arrived`).
  - **Refueled** — the Engineer's refuel completed and a fresh deposit
    was confirmed (`AutoPilotController.EngineerRefuelSucceeded`).
  - **Panic** — a panic-mode stop (`AutoPilotController.PanicOccurred`),
    carrying the same message as the desktop app's own warning banner.
- **Architecture constraint**: the publisher
  (`CompanionSessionPublisher`, `RouteJumper/Services/Companion/`) is
  wired from `MainViewModel` as an independent subscriber to the same
  `IRowEventTrigger.RowTriggered` stream `RouteSequencer` consumes (for
  Plotted/Arrived) and to two dedicated `AutoPilotController` events (for
  Refueled/Panic) — it never touches `Sequencing/` itself; that engine's
  event-driven design (CLAUDE.md) is entirely unaffected.
- **The Angular app** (`/app` at the repo root) is a fully separate
  deployable (Node/npm/Angular CLI) — not part of the WPF app's own
  build/release pipeline. It fetches a session's header doc and
  live-subscribes to its events subcollection (newest first), showing
  the header (start/end system, status) fixed at top with the event feed
  (styled per kind) below. Routed with `HashLocationStrategy`
  (`/app/#/session/<uuid>`) for a deep link to work on a static host with
  no server-side rewrite — a `session/:sessionId` route plus a landing
  route and a wildcard not-found route.
