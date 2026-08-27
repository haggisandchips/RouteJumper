[← Back to spec index](../SPEC.md)

## 4. Route Tab

Two mutually exclusive states: **Edit** and **Table**.

### 4.1 Edit state (default)
- Single multi-line text box fills the tab. Enter/Tab are literal input.
  No line/character limit. Placeholder when empty: "Paste route here, one
  system per line." Receives focus automatically whenever this state
  becomes visible (launch, or Edit clicked from Table state).
- **Save** (enabled once non-whitespace text exists): splits into rows
  (§4.3), switches to Table state, doesn't clear the text box.
- **Cancel** (always enabled): if a route was saved before, restores the
  last-saved text and returns to Table state without ever rebuilding
  `Rows` — the table (icon/status/tracked progress) is left exactly as
  it was. If nothing has ever been saved, just clears the box and stays
  in Edit state.

### 4.2 Table state (after Save)
Read-only `DataGrid`, columns:

| # | Header | Content |
|---|--------|---------|
| 1 | *(blank)* | Row icon — see §4.4 |
| 2 | `#` | Row number, 1-based, save order |
| 3 | `System` | Row's system text, plus a clipboard icon when on the clipboard (§4.6) |
| 4 | `Distance` | Leg distance in ly, previous row → this row — see §4.9 |
| 5 | `Jumps` | **Neutron Plotter routes only** — ordinary hops since the previous waypoint (§4.12) |
| 5 | `Refuel` / `Inject` / `Neutron` | **Galaxy Plotter routes only** — checkmark per row per Spansh flag (§4.12) |
| 6 | `Star Type` | Row's system's main star type — see §4.9 |
| 7 | `Status` | Current status text — see §4.4 |

Column 5's two groups never both show; driven by which route type is
saved (§4.12) — neither shows for a hand-typed/imported/trimmed/Fleet
Carrier-tab route.
- All columns except `Status` are user-resizable and persisted per
  column (§7), restored next launch until first resized —
  `Jumps`/`Refuel`/`Inject`/`Neutron` persist even while not currently
  showing. `Status` always fills remaining width instead (never
  persisted at a fixed size), so its progress bar (§4.4) has real room
  regardless of window size.
- Clicking a row copies its system name to the clipboard, plays a
  confirmation sound (§4.6 sets the clipboard-source icon).
- Right-click → **"Set next system"**: every row before the clicked one
  → Complete, clicked row → current in-progress, every row after →
  not-yet-started — regardless of prior state. Manual correction for
  when automatic detection (§5.7) is wrong.
- Above the table, right-aligned, hidden in Edit state: **"Auto Copy To
  Clipboard"** toggle (§4.6).
- Below the table, left-aligned in click order: **Import Current
  Route**, **Trim for FC**, **Auto Pilot** (§4.10/§4.11, mode-gated as
  described there); right-aligned: **Edit**.
  - **Edit**: returns to Edit state, text unchanged, focus restored.
    Re-Save always produces a completely fresh table, discarding prior
    icon/status — re-derived from the assigned Captain's journal if one
    is assigned, else row 1 defaults to in-progress. Clicking Edit on a
    Neutron/Galaxy route (§4.12) first shows a Proceed/Cancel warning
    that continuing converts it back to Plain, discarding its extra
    column data — declining leaves the table unchanged; the conversion
    itself only happens on the next Save. No warning for a Plain route,
    or for Import Current Route/Trim for FC on a Neutron/Galaxy route
    (they already show their own replace/trim confirmation).
  - **Auto Pilot**: label "Auto Pilot"/"Stop"; **Edit** disabled while
    engaged. Drives the route via the Captain's macro (§4.7); re-Saving
    resets it to idle. Enabled only with Captain assigned + a macro
    selected for them, and — only if Engineer is also assigned — a macro
    selected for Engineer too (§5.5); re-evaluated immediately on any
    Roles-tab change, which also stops an active run if requirements
    drop out. **Hidden entirely** in Ship mode (§8) — switching into Ship
    mode force-stops an active run.

### 4.3 Line-to-row conversion (on Save)
- `\r\n` normalized to `\n`; each line trimmed. Blank lines (anywhere)
  discarded, never becoming rows. Every remaining line → a row, numbered
  from 1 in order (counting kept lines, not original positions).
  `System` = the trimmed text. Row `#` is fixed at Save time forever.

### 4.4 Row icon and status

| Icon state | `Status` | Glyph | Meaning |
|---|---|---|---|
| None | — | *(hidden)* | Not yet reached |
| In-progress | *(blank)* | Play (triangle) | Current row, no status yet |
| In-progress | `Targeted` | Target | Ship mode only (§8.3): `FSDTarget` observed for this row |
| In-progress | `Plotting` | ProgressClock | Fleet Carrier mode only (§4.7): Auto Pilot playing the Captain's macro |
| In-progress | `Plotted` | Hourglass | Fleet Carrier mode only: `CarrierJumpRequest` received |
| In-progress | `Jumping` | RocketLaunch | Fleet Carrier: 3 min before `DepartureTime`. Ship mode: `StartJump` observed, immediately |
| In-progress | `Cooldown` | Play (triangle) | Waiting on the row before it |
| Complete | *(blank)* | Check | Reached, permanently |

- Icons: toolkit `PackIcon`, Material Secondary brush, never hardcoded
  colors. Never removed once shown (`Visibility.Hidden`, space reserved
  — row height never shifts). A completed row's `Status` is always
  blank (§5.7: `Cooldown` shows on the *next* row, not the arrived-at
  one).
- A thin progress bar below `Status` text for `Plotted`/`Jumping`/
  `Cooldown` only (`Visibility.Hidden` otherwise, same reserved-space
  convention) — a cosmetic countdown on a lightweight UI timer, entirely
  separate from the event-driven Sequencing/ engine (CLAUDE.md); never
  affects, or is affected by, anything but its own redraw. Full at
  status-start, draining to empty at the real-world end instant (§5.7
  computes it, except `Jumping` — see below). No bar if the relevant
  timestamp couldn't be parsed.
  `Plotting` shows the bar too, but as an indeterminate sweep (no known
  end) — `StatusDisplay` is the bare word "Plotting", no countdown.
  `Jumping`'s end is estimated at `CarrierJumpRequest` time as
  `DepartureTime` + 1 min (`ArrivalToCooldownDelay`, §5.7) — not
  `DepartureTime` itself, since the row doesn't leave `Jumping` until the
  composite Arrived step, 1 min after real arrival; a close
  approximation since the jump itself is effectively instantaneous.
  Ship mode's `Targeted`/`Jumping` never show a bar (no known duration:
  a target can sit indefinitely, jump charge time isn't tracked). Ship
  mode's `Cooldown` does show the normal countdown bar, counting the
  provisional duration §8.3 describes instead of the fixed 4 minutes.
- Alongside the bar, the same countdown as text: `Status` reads `Plotted
  (0:11:32)` (status word, space, `H:MM:SS` — hours unpadded, minutes/
  seconds always two digits), ticking live. Omitted under the same
  conditions the bar is hidden.

### 4.5 Persistence
See §7.

### 4.6 "Auto Copy To Clipboard"
- Labelled toggle, right-aligned above the table, hidden in Edit state.
  Session-only — always off on a fresh launch (survives Edit/Save
  cycles, never persisted).
- While on: the instant a **live** `CarrierLocation` event (never
  historical replay or a passive snapshot — same gating as §5.7's
  catch-up) is observed for the assigned Captain's carrier, the row
  *after* the one just arrived at has its `System` text copied
  automatically — ahead of the table's own delayed status update. No-op
  if the arrived-at row is last, or isn't found.
- Turning the toggle **on** also immediately copies the current
  in-progress row (no-op if none). Turning it off copies nothing.
- Whichever row's text was most recently copied — by any of the above,
  or a row click — shows a small clipboard icon after its `System` text.
  At most one row at a time; disappears the instant the clipboard's
  contents actually change (any cause), detected via a real Windows
  clipboard-change notification, not polling.

### 4.7 Auto Pilot execution

While engaged, plots each row's jump via the Captain's selected macro
(§5.5) against the Captain's instance, and — if Engineer is assigned —
refuels via the Engineer's macro against the Engineer's instance, until
Complete or stopped:

- For the in-progress row: if `Status` is blank and not `Cooldown`, the
  Captain's macro plays immediately — `Status` → `Plotting` (§4.4) the
  instant it starts, staying there until a real `CarrierJumpRequest`
  moves it to `Plotted` (§5.7). `Plotting` is set by Auto Pilot itself
  (no journal event backs it); if the macro never results in a real jump
  request, panic mode (below) reacts rather than leaving it stuck.
- If showing `Cooldown`, the macro instead plays once Cooldown clears
  (§5.7) plus the configured Auto Pilot delay (§6.1) — letting the
  game's UI settle first.
- If already `Plotting`/`Plotted`/`Jumping` — whether that predates Auto
  Pilot engaging or Auto Pilot itself triggered it moments ago — the
  macro does **not** play again; Auto Pilot just waits for journal
  tracking (§5.7) to advance the row.
- These same rules apply the instant Auto Pilot is engaged, to the first
  row exactly as to any later one.
- Once a row completes and tracking advances, the same rule repeats for
  the next row, until none is left in-progress.
- Independently: the instant a row becomes `Jumping`, Auto Pilot reads
  its `PhaseEndUtc` (§4.4 — `DepartureTime` + 1 min). Once the configured
  delay has passed beyond that instant, and only if Engineer is
  assigned, the Engineer's macro plays (see §4.8 for the announcements
  this schedules) — once per row, never for the route's **last row**
  (nothing to fuel for; also suppresses both Engineer announcements for
  it, §4.8).
- The Captain's plot and Engineer's refuel share the same single
  playback mechanism as a manual Play (§6.5) — `IsPlaying`, **Stop**, the
  same closeable focus-lost warning banner (§6.5). Only one playback runs
  at a time: if one trigger fires while the other is still playing,
  starting the new one cancels the running one, same as two manual
  Plays.
- **Panic mode**: deliberately paranoid — the failure mode it guards
  against is a CMDR who's stopped watching closely while Auto Pilot
  keeps sending the carrier further away on a macro that's silently not
  working. *Anything* unexpected panics, not only the case first noticed:
  - A macro that doesn't run to its own end, for *any* reason — including
    being cancelled/superseded by the *other* Auto Pilot trigger (they
    share one playback channel) — panics immediately. A script cut off
    mid-way can leave the game facing an unknown panel/selection; no
    further automated input is safe to attempt.
  - **Captain's plot**: even a macro that completes still panics unless
    the row has actually left `Plotting` (a real `CarrierJumpRequest`
    was observed, §5.7).
  - **Engineer's refuel**: even a completed macro still panics unless a
    fresh rescan of the Engineer's instance shows a genuine
    `CarrierDepositFuel` for this carrier timestamped after the macro
    started (a small grace window absorbs whole-second journal
    timestamps). Deliberately **not** a before/after fuel-level
    comparison (§5.3): Elite never logs a jump's own fuel consumption —
    only `CarrierStats`/a fresh deposit report an absolute level — so
    the last known level often predates the very jump this refuel
    recovers from; a depot refilled back to that stale (often exactly
    1000) ceiling would show no numeric increase despite a real deposit,
    wrongly panicking a successful run. A confirmed-fresh deposit
    timestamp has no such blind spot, with no exception for a depot
    already believed full or an unknown prior fuel level — a rescan
    showing no fresh deposit at all is itself the anomaly worth
    surfacing.
  - Any panic stops Auto Pilot the same way route-completion does
    (below), reporting via the same closeable, tab-independent warning
    banner a manual macro failure uses (§6.5).
- Auto Pilot stops itself once every row reaches Complete (label reverts
  to "Auto Pilot"), or if Captain/Engineer stop meeting requirements
  (§4.2) mid-run, or on either panic-mode failure. Only route-completion
  (not a manual Stop, not panic) speaks a one-off "You have arrived at
  your destination. Thank you for flying with ED F.C. Auto Pilot."
  announcement (§4.8's muting/volume/voice apply), at the instant the
  row after the last one would otherwise have started `Cooldown`.
- A `CarrierJumpCancelled` reverting a row's `Status` to blank (§5.7)
  does not itself trigger a further macro play for that row — recovery
  requires manual intervention. No bearing on the Engineer's
  `Jumping`-keyed refuel trigger.

### 4.8 Spoken announcements
- Ahead of each §4.7 trigger, ED:FC Auto Pilot speaks via the OS speech
  engine (Preferences voice/volume, §3.5): "Plotting in 30 seconds" then
  "Plotting in 5 seconds" before the Captain's macro; "Refueling in 30
  seconds"/"Refueling in 5 seconds" the same way for the Engineer's.
- The Captain's announcement is scheduled the moment `Cooldown` starts,
  against its precomputed clear time. The Engineer's is scheduled the
  moment the row becomes `Jumping`, against `PhaseEndUtc` (§4.4) plus the
  configured Auto Pilot delay — since `Jumping` itself starts 3 min
  before `DepartureTime`, this gives real minutes of lead time. Neither
  is spoken for a jump firing immediately with no Cooldown waited on.
- The 30-second one is silently skipped if that much lead time isn't
  actually available when scheduled. The 5-second one is more
  forgiving: with less than 5 seconds left but the trigger still
  pending, it's spoken immediately instead of skipped.
- The Engineer's announcements only speak if Engineer is still assigned
  with a macro by the time each is due (checked fresh, not just at
  scheduling) — never scheduled at all for the route's last row.
- The final route-completion announcement (§4.7) is spoken only for that
  stop reason, never a manual Stop or panic stop (both self-explanatory
  already).
- Muting (§3.4) silently suppresses every announcement here; no effect
  on Preferences' Test control.

### 4.9 Distance and Star Type

Every row's `Distance`/`Star Type` (§4.2) populate asynchronously against
[EDSM](https://www.edsm.net) — this app's first outbound third-party
network call. Only system names are sent, never commander/journal data.
Applies identically in both modes.

- **`Distance`**: leg distance, previous row → this row, never live
  "distance from wherever the CMDR is now." Row 1's "previous" is the
  CMDR's own ship position (Fleet Carrier mode: the Captain's; Ship
  mode: the tracked instance's — never the carrier's own position) at
  the moment the route was last saved/restored. Computed once per Save
  (plus once more in the background once a restored Captain/tracked
  instance's own startup scan resolves) and never recomputed afterward —
  a planning aid, computed outside the event-driven progress engine
  (§5.7/§8.3, CLAUDE.md).
- **`Star Type`**: the row's system's main star's EDSM `subType`, with
  its redundant trailing "Star" word dropped before display/caching.
- Both populate **progressively** in the background — the table appears
  immediately, blank, filling in as lookups resolve. A system EDSM can't
  resolve, or a lookup that fails outright, leaves that cell blank
  permanently (never blocks, retries forever, or guesses).
- `Distance` is chained, so a row whose own coordinates are unavailable
  also blanks the *next* row's `Distance` (even if that row's own data is
  fine). To make the culprit obvious, `Distance` shows light "Plot
  needed" text whenever *this row's own* coordinates are confirmed
  unavailable (this session, or within the retry cooldown) — never for a
  row blanked only because the row before it (or, for row 1, the CMDR's
  origin) is the problem. `Star Type` shows "Target needed" the same way
  whenever coordinates are known but star type isn't (a single
  `FSDTarget` fixes that — faster than `Distance`'s "Plot needed" fix).
  Neither placeholder replaces a still-mid-resolution blank cell. Both
  are a UI signal recomputed fresh per pass — though the underlying
  "confirmed unavailable" fact is persisted (below), so it keeps showing
  across a Save/restore or restart until the cooldown lapses.
- Once an enrichment pass completes and at least one row still shows
  "Plot needed"/"Target needed", a dismissible advisory banner appears
  above the table (below "Auto Copy To Clipboard") pointing at those
  hints. Dismissing hides only the banner, not the per-row placeholders.
  Not shown mid-pass or once fully resolved; a fresh Save re-evaluates
  from scratch, resetting any earlier dismissal.
- Once resolved, a system's coordinates/star type cache locally for the
  session (instant reuse on a later Save/restore, no repeat lookup).
  Persisting to the next session (`ResolvedLookups` table, §7) depends
  on how it resolved: a value EDSM itself resolved is **not** persisted
  (its lookup is cheap to repeat next session). A journal/Spansh seed
  (below) *is* persisted, but only if it fills a gap EDSM already
  confirmed it can't fill on its own (almost always a procedurally-
  generated name) — keeping `ResolvedLookups` bounded to "systems EDSM
  genuinely can't help with."
- An **unresolved** name (EDSM's response genuinely omitting it — never
  a transient failure, which always retries next attempt) is remembered
  too, so it isn't re-queried for `EdsmUnresolvedRetryHours`
  (`routejumper.conf`, default 12h) — in-memory and on disk (§7),
  surviving a restart. Resolving early (a later EDSM success, or a local
  seed) clears the cooldown immediately.
- **Coordinates and star type resolve together, in one request**: EDSM's
  `api-v1/systems` endpoint returns both with `showCoordinates=1&
  showPrimaryStar=1`, chunked (`EdsmCoordinatesBatchSize`, default 100).
  Resolved via two independent batched passes regardless — Distance
  first, Star Type second — since a system whose coordinates are already
  cached never goes through the coordinates pass' network fetch, so the
  star-type pass batch-resolves every such remaining system together
  rather than falling back to one request per system.
- On a normal launch with a saved route, `Distance` populates twice:
  once immediately (whatever origin is already known), and once more
  once the restored Captain's/tracked instance's startup scan resolves.
- **Both modes opportunistically seed the same cache for free**, from a
  commander's own ship journal events (Ship mode: the tracked instance's;
  Fleet Carrier mode: the *Captain's own ship* journal, watched
  independently of carrier events — a Captain can fly their own ship
  while the carrier jumps on its own schedule):
  - `FSDTarget`'s `StarClass` field seeds `Star Type` for the targeted
    system. The raw code (e.g. `K`, `TTS`) is cached (§7); display
    formatting (matching EDSM's naming, minus "Star") is computed fresh
    each time via a single shared mapping (also used to recover a code
    from EDSM's own text, since EDSM never returns a code) — so both
    sources render identically. Reformatted only for well-established
    classes (main-sequence, brown dwarfs, neutron stars, black holes,
    white dwarf subclasses, T Tauri, Herbig Ae/Be, Wolf-Rayet, carbon
    stars); anything more exotic is cached as the raw code.
  - The `NavRoute` journal event (a marker confirming `NavRoute.json` was
    (re)written) or, as a fallback, `FSDTarget`'s `RemainingJumpsInRoute`
    field (catches a still-current route without a fresh `NavRoute`
    line, e.g. after a restart) triggers reading `NavRoute.json` (beside
    the journal) and seeds `Distance`'s coordinates + `Star Type` for
    every system in the currently-plotted in-game route — including
    procedurally-generated systems EDSM has no record of, since it's the
    game's own exact calculation. Looked up by name, so it benefits the
    pasted route regardless of whether it matches the in-game one.
  - Purely opportunistic — a system neither path fills still falls back
    to EDSM; Auto Pilot/row tracking is entirely unaffected (Captain
    FSDTarget lines fire no row event, only this cache seed).
  - A seed always updates the session cache immediately, but only
    persists to `ResolvedLookups` (§7) if EDSM had already confirmed no
    record of that exact system — a `NavRoute.json`/Spansh-only system
    isn't persisted the first time, just supplied fresh again next
    session, keeping the cache bounded.
- **A newly-seeded system refreshes already-displayed rows live** —
  updates within about a second, no restart needed. A row already fully
  resolvable from cache at seed time updates immediately; only the
  remaining genuinely-uncached rows are debounced (a single
  `NavRoute.json` read seeding many systems in a tight loop collapses
  into one catch-up pass after the burst quiets).

### 4.10 Import Current Route

- **"Import Current Route"** button, leftmost of the left-aligned group
  (§4.2) — the sole left-hand button in Ship mode. Available
  unconditionally in both modes, no Captain/tracked instance required.
- After confirming (below), reads `NavRoute.json` directly from the
  configured journal folder (`JournalDirectory`, §5.2) — never a specific
  instance's own path — and replaces the saved route with the in-game
  plotted route, one system per line, applying immediately (equivalent
  to pasting + Save, no Edit-state review step).
- Deliberately unconditional: `NavRoute.json` is a per-*installation*
  file like `Status.json`/`Cargo.json` (§5.2), not tied to one running
  instance, so gating it on an assignment wouldn't guarantee correctness
  anyway — the CMDR judges from the imported list itself.
- `NavRoute.json`'s `Route` array starts with the CMDR's departure
  system, not a system to travel to — that first entry is skipped.
- Every entry's coordinates/star type (including the skipped one) seed
  the Distance/Star Type cache (§4.9) along the way, so the newly-saved
  route's columns populate instantly from cache rather than a fresh EDSM
  round trip.
- **Confirmation**: plain Yes/No first (declining leaves the route
  untouched); once confirmed, applied unconditionally with no further
  dialog, including on failure (no route plotted, or the file
  missing/unreadable) — a failure is logged (§3.8) rather than shown.

### 4.11 Trim for FC (Fleet Carrier mode)

- **"Trim for FC"** button, immediately after Import Current Route and
  before Auto Pilot (§4.2) — **Fleet Carrier mode only** (hidden in Ship
  mode) — a real carrier's max jump range is a fixed 500ly, unlike a
  solo ship's variable range.
- After confirming, collapses the saved route down to hops ≤500ly each,
  dropping rows not needed to stay within reach — useful for a
  closely-spaced route (e.g. a neutron-highway output) simplified to
  only the systems a carrier genuinely needs to jump via. A greedy
  "farthest-reachable waypoint" walk from the carrier's own real current
  location (always kept), repeatedly advancing to the farthest row still
  within 500ly of the last kept one — never skipping a genuine >500ly
  gap (that row is kept regardless). The last row is always kept.
- **Requires a Captain assigned with their carrier's current location
  known** (§5.3) — the pasted route's row 1 is only where the CMDR
  *started planning*, not necessarily the carrier's real position (it
  may be mid-route, or detoured). No Captain assigned: message box
  explaining this, no confirmation reached. Captain assigned but
  location not yet known this session (resolved from `CarrierJump`/
  `CarrierLocation`, confirmed against `CarrierStats`, which needs
  Carrier Management opened once): message box telling the CMDR to open
  it in-game.
- The carrier's real location becomes the trimmed route's new first
  entry, and the walk is anchored from it instead of row 1 — unless the
  carrier already sits at row 1's system, in which case nothing is
  prepended. This is what keeps the first jump efficient: an
  anchor-from-carrier walk can discover row 1 itself is skippable.
- Applies **immediately** once confirmed (equivalent to pasting the
  trimmed list + Save, no review step) — a deterministic, purely
  mechanical calculation.
- **Confirmation**: same plain Yes/No as Import Current Route; declining
  leaves the route untouched. Once confirmed, applied with no further
  dialog either way (including "nothing removed") — logged, not shown.
- Requires every row's coordinates, and the carrier's own current
  system's coordinates, already known (§4.9) — any row still resolving
  or confirmed unavailable blocks the trim (logged, route untouched).

### 4.12 Spansh Integration

- **Spansh > Fleet Carrier…**/**Neutron Plotter…**/**Galaxy Plotter…**
  (§3.4) each open the same small modal dialog to calculate a route via
  [Spansh](https://spansh.co.uk)'s route-plotting API and import it into
  the Route tab — available regardless of active tab, in either tracking
  mode, differing only in which of the dialog's own three tabs starts
  selected. Used with Spansh author Gareth Harper's express permission
  to use undocumented endpoints, on condition the app never spams the
  site — credited in a footer shared by all three tabs (visible
  regardless of which is active; EDSM, §4.9, has no equivalent credit
  yet).
- Each tab has its own **Source**/**Destination** field (debounced
  autocomplete against Spansh, `SpanshAutocompleteDebounceMs`, default
  250ms) and its own **Calculate** button; the three tabs' Calculate
  operations don't cancel each other. All three tabs stay reachable
  regardless of which menu item opened the dialog.
- A custom autocomplete control is used (not a stock `ComboBox`) — a
  bound `ComboBox` was confirmed to auto-select-and-highlight the whole
  box on an exact text match (wiping further typing) and to close its
  dropdown after the first arrow-key press.
- **Calculate** enables once Source/Destination each have an actual
  selected suggestion (not just typed text) — Neutron Plotter also
  requires Range/Efficiency non-blank; Galaxy Plotter also requires the
  ship-build resolution to have completed and Cargo non-blank. Clicking
  it requests a route and polls every 5 seconds (indeterminate progress +
  status text: "Requesting route from Spansh…", "Queued…", Spansh's own
  in-progress state, ...), independently per tab. A new Calculate on a
  tab while its own previous one is in flight cancels the previous one
  first; closing the dialog cancels all three in-flight calculations.
- **On success** (any tab): every returned jump's coordinates/system
  address (id64) seed the Distance/Star Type cache (§4.9) before the
  route replaces the saved one (every jump's System text, one per line,
  including the source — Spansh always lists it first) and is Saved —
  no Edit-state review, no confirmation dialog (opening Calculate is
  already the deliberate action). An empty jump list is logged, route
  left untouched.
- **On failure** (job failed, request/poll unreachable, or — Neutron/
  Galaxy only — outright rejected, e.g. out-of-range Range or an unknown
  Source/Destination): status message explains why (Spansh's own
  verbatim reason for an outright rejection); dialog stays open,
  unchanged, Calculate retryable.
- **Fleet Carrier tab**: a single, direct source→destination hop — no
  tritium/restock planning. Its own footnote (dialog's full width, below
  the tab area, conditional on this tab being active) links to Spansh's
  hosted **Fleet Carrier Router** (`https://spansh.co.uk/fleet-carrier`)
  for that heavier planning (paste the result by hand, or Import Current
  Route once plotted in-game).
  - **Source** pre-fills, when known, from the Captain's carrier's real
    current location (Fleet Carrier mode only) — since this tab posts a
    Spansh-assigned id, not a name, opening the dialog kicks off a
    background search applying the result only if a suggestion exactly
    matches and the CMDR hasn't already picked/typed something. Silently
    unfilled if the search fails or finds no exact match.
- **Neutron Plotter tab**: same Source/Destination, plus editable
  **Range** (ly) and **Efficiency** (1-100, defaults to Spansh's own 60),
  and a **Regular**/**Overcharge** supercharge radio choice. Neither
  Range nor Efficiency is validated client-side (Spansh's own rejection
  reason is shown via the status message). On success, only the route's
  own waypoints (neutron stops + destination) are kept, not every
  ordinary hop — for feeding into Trim for FC or flying manually. Each
  waypoint's own `jumps` (ordinary hops since the previous waypoint)
  imports alongside, shown as the `Jumps` column (§4.2), tagging the
  route Neutron-typed until next Saved otherwise.
  - **Range** always starts blank (the CMDR's own call — not pre-filled
    from `Loadout`'s `MaxJumpRange`, which reflects only whatever
    fuel/cargo was aboard when last logged).
  - **Source** pre-fills, when known, from that instance's current
    system (same field §4.9's row-1 Distance uses), as an
    already-selected suggestion — still fully editable; typing clears
    the selection, requiring a fresh pick.
  - **Regular**/**Overcharge** picks Spansh's `supercharge_multiplier` (4
    or 6, confirmed against Spansh's own web client). Defaults to
    Overcharge only when that instance's latest `Loadout` event's
    `FrameShiftDrive` slot module `Item` ends in
    `_overchargebooster_mkii` (case-insensitive, suffix-matched so a
    future variant is still recognised); Regular otherwise — a plain,
    freely-editable choice from there on.
  - Its own footnote links to Spansh's hosted **Neutron Plotter**
    (`https://spansh.co.uk/plotter`) for waypoint/via planning this tab
    doesn't cover.
- **Galaxy Plotter tab**: an *exact* route accounting for the CMDR's real
  ship build (fuel usage, jump range, supercharge/injection), unlike
  Neutron Plotter's flat Range. Source/Destination, plus editable
  **Cargo** (t, pre-filled from the instance's tracked cargo total) and
  **Reserve tank size** (t, starts at Spansh's default 0), an
  **Algorithm** dropdown (`fuel`/`fuel_jumps`/`guided`/`optimistic`/
  `pessimistic`, defaults to `optimistic`), and six checkboxes for
  Spansh's route options — already supercharged at start, use
  supercharge boosts, use FSD injections, use injections only when
  required, exclude secondary-economy systems, refuel at every scoopable
  star (Spansh defaults: off, on, off, off, off, on) — all freely
  editable.
  - Needs the CMDR's ship build, resolved by re-reading the relevant
    instance's journal (latest `Loadout`'s `Ship`, `Modules` incl.
    `Engineering`, `UnladenMass`, `FuelCapacity`) the moment the dialog's
    ViewModel is constructed. While resolving or on failure, the status
    message explains why ("Reading ship loadout…", "No ship loadout
    logged yet this session - open the in-game Outfitting or Ship screen
    once, then reopen this dialog.", or a specific reason like "No Frame
    Shift Drive found in this ship's loadout."), and Calculate stays
    disabled until it resolves.
  - The resolved build feeds Spansh purely as derived numbers (fuel
    range/mass/tank-capacity, reproducing Spansh's own client-side
    calculation) — confirmed live that Spansh's server-side computation
    never uses anything else, so no separate ship-build upload is sent.
  - On success, only the route's own waypoints are kept (as Neutron
    Plotter). Each waypoint's `must_refuel`/`must_inject`/`has_neutron`
    flags import alongside, shown as `Refuel`/`Inject`/`Neutron` columns
    (§4.2), tagging the route Galaxy-typed until next Saved otherwise.
  - Its own footnote explains this plots an exact route using the real
    ship build, and links to Spansh's hosted **Galaxy Plotter**
    (`https://spansh.co.uk/exact-plotter`) for following it in-game and
    detailed parameter/algorithm explanations.
