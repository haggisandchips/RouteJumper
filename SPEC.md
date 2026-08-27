# ED:FC Auto Pilot — Application Specification

**Version:** 2.0
**Platform:** Windows desktop, WPF, .NET 8, MVVM

---

## 1. Purpose

ED:FC Auto Pilot is a desktop utility for Elite Dangerous commanders, with two
mutually exclusive tracking modes (§3.4). In **Fleet Carrier mode** (the
default), a user pastes a list of star systems (one per line) to form a
route; once a Captain's game instance is assigned on the Roles tab,
ED:FC Auto Pilot watches that commander's journal file and automatically tracks
the carrier's progress along the route, updating each row's status as the
carrier plots, jumps, arrives, and cools down — with an optional Auto
Pilot that drives the whole route via recorded macros. In **Ship mode**
(§8), a solo commander instead pastes a manually hand-picked route (e.g.
from a neutron-highway plotter) and assigns their own running instance on
the Track tab; ED:FC Auto Pilot tracks the same row-by-row progress against
that commander's own ship journal, purely passively — there is no
automation, since the CMDR must plot and fly every jump themselves.

---

## 2. Scope

| In scope | Out of scope |
|---|---|
| Route tab: paste a route, save it to a table, track progress against a real journal | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Roles tab: detect running Elite Dangerous instances; assign Captain/Engineer roles | Configurable journal-match tolerance or cooldown timing |
| Event-driven row-progress engine, driven by a Captain's journal (Fleet Carrier mode) or a commander's own ship journal (Ship mode, §8) | Persisting per-row progress (icon/status), or the last-selected tab |
| Manual "Set next system" override for correcting automatic detection | |
| Selecting a macro for each of Captain/Engineer (§5.5), gating Auto Pilot on that selection, and Auto Pilot playing the Captain's macro to plot each jump and (if Engineer is assigned) the Engineer's to refuel each Cooldown (§4.7) | Auto Pilot retrying a jump after `CarrierJumpCancelled` (§4.7) |
| Persistence of route text, window bounds, Captain/Engineer role assignment, tracking mode, and tracked instance (§7) | |
| "Auto Copy To Clipboard" for the next system, plus a clipboard-source indicator | |
| Controls tab: key bindings, running-instance scan, and recording/playback of macros (§6) | |
| Spoken lead-time announcements before Auto Pilot plots/refuels (§4.8), a File menu (§3.4) with Preferences (voice/volume/test, plus an update-check opt-out) and Exit, a Help menu with Logs (§3.8), Check for Updates (§3.7), and About (§3.6), a Fleet Carrier/Ship mode toggle, and an always-visible mute button | |
| **Ship mode** (§8): a Track tab for picking a single instance to passively track via that commander's own ship journal, with no Auto Pilot/macro automation at all | Fuel management (warning how many jumps remain before running dry) — a planned future enhancement, not built yet |
| A top-level **Spansh** menu (§3.4) holding **Fleet Carrier…**/**Neutron Plotter…**/**Galaxy Plotter…** (§4.12): calculate a route between two systems via Spansh's own route-plotting API and import it straight into the Route tab, available in both tracking modes - the Galaxy Plotter tab additionally uses the CMDR's own real ship build (re-read from that instance's journal in the background) to plot an exact route accounting for fuel usage and supercharge/injection options | Proper on-screen credit for EDSM specifically (§4.9) — a planned future enhancement, not built yet. (Spansh, §4.12, already carries its own in-dialog credit.) |
| Material Design styling | |
| Importing whatever route is currently plotted in-game ("Import Current Route", §4.10), unconditionally, in both tracking modes; trimming a saved route down to a max-500ly-per-hop series of jumps (§4.11, "Trim for FC", Fleet Carrier mode only) - both apply immediately after a Yes/No confirmation, with no in-between review step | |
| A companion site (§13): a QR code/link next to the Auto Pilot button opens a small, mobile-friendly Angular site (hosted alongside the docs via GitHub Pages) showing a live, read-only feed of an in-progress Auto Pilot run - route/jump plotted, arrived in system, refueled, panic-mode stop | Any authentication beyond a session's own unguessable URL, and any companion-site feature beyond the live event feed itself (e.g. two-way control) - a planned future enhancement, not built yet |

---

## 3. Window Structure

### 3.1 Main Window
- Single top-level window, `TabControl` with left-side tab headings
  (`TabStripPlacement="Left"`). Route is always first and selected by
  default. Roles/Controls show in Fleet Carrier mode (default); Track (§8)
  shows in Ship mode instead — switching modes (§3.4) swaps them
  immediately, whichever tab is currently showing.
- Resizable, content reflows (no fixed-pixel layouts).
- Title: **"ED:FC Auto Pilot"**. Icon: `Resources/AppIcon.ico`, a
  `RocketLaunch` glyph at 16/32/48/256px, matching row-icon style/color.

### 3.2 Startup window placement
- On launch: positioned against the right edge of the physically
  rightmost connected monitor (10px margin, vertically centered) — the
  monitor with the greatest `Left` coordinate, determined fresh each
  launch — unless a persisted, still-reachable position exists (§7), in
  which case that's restored instead.
- Positioned at `SourceInitialized` (HWND exists, not yet painted), so no
  visible jump on launch. Monitor bounds are converted to WPF DIUs via the
  window's own composition-target transform, correct under per-monitor DPI.

### 3.3 Controls tab
See §6.

### 3.4 Menu bar, mode toggle, and mute button
A `Menu` above the tab area, always visible, with **File**, **Spansh**,
**Help**:
- **File**: **Preferences…** (modal, §3.5). **Exit** (persists bounds,
  §7, and quits).
- **Spansh** (its own top-level entry, not nested — the app's only such
  integration): **Fleet Carrier…**/**Neutron Plotter…**/**Galaxy
  Plotter…** each open the same modal dialog (§4.12) with the
  corresponding tab pre-selected; all three tabs stay reachable from
  inside regardless, in either tracking mode, from either main tab.
- **Help**: **Logs** opens the non-modal Logs window (§3.8) — stays open
  and live-updating unlike Preferences/About; a second click re-focuses
  the existing window rather than opening a duplicate. **Check for
  Updates** manually triggers the same download-and-apply-on-next-exit
  check run silently on launch (§3.7), always regardless of the
  Preferences opt-out, reporting the outcome via a message box (the
  silent check never does); disabled while a check it started is in
  flight. **About** opens the About dialog (§3.6, modal).

Between the menu and the mute button, centred: a **Fleet Carrier / Ship**
choice-chip toggle (§8). Switching swaps visible tabs (§3.1), shows/hides
and force-stops Auto Pilot (§4.2), and switches which journal watcher runs
(Roles' Captain watcher or Track's ship watcher — exactly one at a time).
Persists immediately; defaults to Fleet Carrier until first changed. Each
chip shows its own tooltip (explaining what switching to it does) only
while it is *not* the currently-selected chip.
- The **Fleet Carrier** chip is disabled for as long as the saved route
  is Neutron- or Galaxy Plotter-typed (§4.12) — a carrier's own Auto
  Pilot has no notion of neutron boosts/FSD injections. The instant such
  a route is saved or restored on launch, Ship mode is forced silently
  (no confirmation), overriding any previously-active or persisted Fleet
  Carrier mode; the chip re-enables the instant the route reverts to
  Plain (Edit's confirmation, Import Current Route, or Trim for FC)
  without auto-switching back — the CMDR chooses that. While disabled,
  its tooltip reads "Not appropriate for Neutron Plotter routes." or
  "Not appropriate for Galaxy Plotter routes." as appropriate, followed
  by "Edit the route to revert it to a plain route, then you can switch
  back to Fleet Carrier mode." in place of its usual text.

Right-aligned beside the menu: an always-visible mute icon button toggles
the spoken announcements (§4.8) — icon swaps plain/crossed-out speaker.
Has no effect on the Preferences dialog's own Test control (§3.5), which
always plays.

### 3.5 Preferences dialog
Modal (File > Preferences…) for announcement voice/volume plus the
update-check opt-out — the mute toggle itself lives only on the main
window (§3.4).
- **Voice**: dropdown of every installed OS voice, cleaned-up display
  name (the "Microsoft " prefix stripped; a duplicate "... Desktop" entry
  for the same voice dropped entirely), plus a clear button resetting to
  blank (the engine's own default — the dropdown has no blank entry of
  its own).
- **Test** (icon button): speaks a fixed sample phrase at the current
  voice/volume, always audible even while muted.
- **Volume**: slider, 0-100.
- **Automatically check for updates on startup**: checkbox, checked by
  default, gates only the *silent* startup check (§3.7) — no effect on
  Help > Check for Updates.
- Every change saves immediately (§7, same "edits save as they're made"
  convention as Controls' Options); **Close** is pure navigation.

### 3.6 About dialog
Modal (Help > About), purely informational: app icon, "ED:FC Auto
Pilot", installed version (§3.7) or "Development build" for an
unpackaged run, a one-line description, the fan-made/not-affiliated
disclaimer, a GitHub repo link (default browser), copyright/license line.
Single **Close** button.

### 3.7 Automatic updates
- On every launch, silently checks GitHub Releases for a newer version
  (via Velopack); downloads in the background and applies on the *next*
  normal exit. No-ops for an unpackaged run (`dotnet run`/F5).
- Turned off entirely via Preferences' "Automatically check for updates
  on startup" checkbox (§3.5, on by default) — persists immediately,
  re-checked fresh every launch.
- Help > **Check for Updates** (§3.4) runs the identical check on demand,
  always regardless of that checkbox, and reports via a message box: up
  to date / downloaded (installs next exit) / not available (unpackaged
  build) / check failed (network) — the only case a failure surfaces at
  all, since the silent check never reports anything.

### 3.8 Logs window
Non-modal (Help > Logs), live-tails the logging stream (§12) — the
on-disk file is the durable record; this window is a live in-memory view.
- Opens **empty** — shows only entries logged while open, nothing
  earlier; closing/reopening always starts blank. **Clear** empties the
  visible text without touching the on-disk file.
- **Wrap text** checkbox, off by default (unwrapped, matching raw log
  layout; scrollbar hidden while wrapped). Session-only.
- **Open Logs Folder** opens the on-disk Logs directory (§12) in
  Explorer.
- **Close** dismisses it. Owned by the main window, closes alongside it.

---

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

---

## 5. Roles Tab

### 5.1 Process discovery
- Every refresh (initial load, or Refresh button) enumerates running
  `EliteDangerous64.exe` processes. Zero → centered italic empty-state
  message. Refresh runs on a background thread (button disables for the
  duration; app stays interactive); runs automatically on startup and on
  every click.
- While Captain is assigned, a live `CarrierStats` event for their
  carrier (logged on opening Carrier Management) also triggers a
  background refresh automatically.

### 5.2 Journal matching
- No OS-level link between a process and its journal file. Every
  candidate (`Journal.*.log`, in the configured journal folder) is
  timestamped via its own `Fileheader` event.
- The journal folder comes from `routejumper.conf`'s `JournalDirectory`
  entry (plain-text `Key=Value`, beside `routejumper.db` in
  `%LocalAppData%\EDFCAutoPilot`) — created on first read defaulting to
  Frontier's standard Saved Games location, hand-editable with no
  restart required (re-read fresh every Roles refresh).
- Each process matches the journal whose `Fileheader` timestamp is
  closest to its start time, within 5 minutes — greedy, one-to-one. No
  match within tolerance → "Not found"/"Unknown" for journal-derived
  fields.
- Live `Status.json`/`Cargo.json` are never used (per-installation, not
  per-instance) — everything instance-specific comes from that
  instance's own journal.

### 5.3 Per-instance card

| Field | Source |
|---|---|
| Commander name, FID | Latest `Commander` event |
| Cargo | Latest `Loadout` (capacity) + latest ship `Cargo` (current, defaulting to 0 if none seen this session), shown as `current / capacity`t, tritium included in the total with a separate `Nt tritium` sub-line (omitted when zero) |
| Current location | Latest of `Location`/`Docked`/`Undocked`/`FSDJump`/`CarrierJump` (while docked) |
| Fleet carrier | Name from `CarrierStats` ("Unnamed carrier" if Carrier Management never opened); location from `CarrierJump`/`CarrierLocation` matched by `CarrierID`, `FleetCarrier` type only |
| Carrier fuel | Tritium tank level (0–1000t), `Nt fuel` sub-line, omitted until known — from whichever of `CarrierStats.FuelLevel`/`CarrierDepositFuel.Total` was most recent for the commander's own resolved `CarrierID` |
| Journal file | Filename; click copies + confirmation sound (no visual indicator afterward, unlike §4.6) |
| Window handle | `Process.MainWindowHandle`, hex |
| Window position | `GetWindowRect` |
| Monitor | `MonitorFromWindow` + `GetMonitorInfo` |

- All fields found in one sequential journal pass, taking the latest
  occurrence of each event.
- Cargo excludes tritium for Engineer eligibility (§5.6) but includes it
  in the displayed total. Tritium tracked incrementally across `Cargo`/
  `CargoTransfer`/`CarrierDepositFuel`/`MarketBuy`/`MarketSell`,
  resyncing from a `Cargo` event's `Inventory` breakdown when present.
- Frontier only logs `Cargo` on an actual change, so a ship that's
  carried nothing this session never gets one — current cargo/tritium
  default to 0, not "Unknown"; Engineer eligibility follows from
  `Loadout`'s capacity alone even before any `Cargo` event.
- Carrier fuel is an absolute reading (latest wins per `CarrierID`), but
  still resolved against the commander's own confirmed `CarrierID` first
  — a squadron-carrier deposit is never shown as theirs.
- No main window yet, or exits mid-scan → window/monitor fields degrade
  to "Unknown" without failing the whole scan.
- Cards stretch full tab width, vertically-stacked scrollable list.

### 5.4 Role assignment
- **Captain**/**Engineer**, each assignable to at most one instance (not
  necessarily the same one); toggle buttons per card.
- **Engineer** can't be assigned to zero/unknown cargo capacity.
- Assigning **Captain** resets the whole route, then replays that
  instance's journal (§5.7). Clearing Captain (explicit, or its instance
  stopping) leaves the route as displayed. A new instance never disturbs
  assignments already held elsewhere. Assigning Captain also triggers a
  background rescan of all instances.

### 5.5 Role macros
Above the instance cards: **Captain plots via**/**Engineer refuels via**
— each a dropdown of every recorded macro (§6.5) by name, plus a clear
button. Belongs to the role itself, not whichever instance holds it —
persists across role reassignment, independent of whether the role is
currently assigned. A deleted selected macro clears back to unset.

Auto Pilot (§4.2) enables once Captain is assigned with a macro selected
here, and — only if Engineer is *also* assigned — a macro for Engineer
too; an unassigned Engineer imposes no macro requirement.

### 5.6 Persistence of role assignment
See §7.

### 5.7 Journal-driven route updates

Once Captain is assigned, watches that commander's journal for
`CarrierJumpRequest`/`CarrierLocation` belonging to their own fleet
carrier (filtered by `CarrierID` + `CarrierType == "FleetCarrier"`, so a
shared squadron carrier is never mistaken for theirs).

- On assignment, the whole journal is read first to derive a single
  authoritative "current status" — **not** by replaying every line as
  its own event. Decided by journal **line order** (not each event's
  `timestamp`), walking oldest→newest, tracking whichever of
  `CarrierJumpRequest`/`CarrierLocation`/`CarrierJumpCancelled` was seen
  *last*: an unresolved `CarrierJumpRequest` → its target is current,
  `Status` → `Plotted` (and `Jumping` if due). Otherwise, the carrier is
  wherever the most recent `CarrierLocation` placed it — that row (and
  everything before) → Complete, next row → in-progress. Only this one
  derived result applies, as a single event; after that, new lines
  live-tail via `FileSystemWatcher`, one at a time.
- This matters because a carrier's real path isn't necessarily a clean
  forward walk — a system can be revisited (e.g. a tritium depot).
  Replaying every historical line oldest-to-newest would complete that
  row on its *first* match, permanently excluding later genuine visits.
  Computing the single final state first avoids this.
- Matching a row to an event is always by System name
  (case-insensitive), the row's *first* occurrence (the route is always
  freshly Reset first). A match silently completes every earlier
  not-yet-complete row — how one result catches up several rows at once.
  A genuinely repeated waypoint (a circular route) is the one case
  "first occurrence" can point wrong — the manual "Set next system"
  override (§4.2) exists for exactly that.
- This "complete every earlier row" applies equally to the one-off
  catch-up and to an ordinary live `Plotted`/`Arrived` event — even one
  targeting a row further ahead than expected (the carrier's real path
  skipped a pasted row). Both are authoritative confirmed progress, live
  or replayed alike. `Targeted` (Ship mode, §8.3) never sweeps anything —
  only ever an intention.

**Transitions and timing** (measured from each journal event's own
timestamp, never read-time; fires immediately if already passed during
history replay, otherwise via a non-blocking timer):

1. `CarrierJumpRequest` → `Status` → `Plotted` immediately.
2. 3 minutes before that event's `DepartureTime` → `Status` → `Jumping`.
3. 1 minute after the matching `CarrierLocation`'s timestamp → arrived-at
   row → Complete, blank status; next row (if any) → in-progress.
   **Only if this `CarrierLocation` was observed live** does the next
   row's `Status` also → `Cooldown` (never as a side effect of
   catch-up/replay — no reliable way to place a stale cooldown window
   against the current clock). Nothing put into Cooldown with no next
   row.
4. A further 4 minutes later (5 min after `CarrierLocation` total) →
   next row's `Cooldown` clears, if showing one.

`CarrierJumpCancelled` (filtered by `CarrierID` only — no `CarrierType`
field) reverts whatever the cancelled request did: whichever row is
in-progress with `Status` `Plotted`/`Jumping` (at most one) → `Status`
clears to blank, icon stays in-progress. Found by state, not name (the
event carries none). Any already-scheduled `Jumping` transition (step 2)
is cancelled too, so a stale timer can't re-fire it.

- Assigning Captain resets every row (`Icon → None`, `Status →` blank)
  before replaying — no previous Captain's progress lingers.
- Event-driven by design (CLAUDE.md): every row mutation is a direct
  response to an event, never a hardcoded delay in the row-update logic
  itself — real-world scheduling lives in the journal watcher, which
  only ever reacts to "now".

### 5.8 Persistence of role assignment (FID-based)
See §7.

---

## 6. Controls Tab

Manages named key-binding sets for the app's automation: record
keypresses/clicks against a running instance, capture as an editable
human-readable script, replay later. A macro selected for a role (§5.5)
is what Auto Pilot (§4.2) plays; nothing else here is auto-wired.

Four vertically-stacked sections: Options, Key Bindings, Running
Instances (fixed height at top); Recorded Macros fills the rest (each
section scrolls internally, no whole-page scroll).

### 6.1 Options

Two settings, side by side:
- **Auto Pilot delay (ms)**, default `5000`. Auto Pilot (§4.2) waits
  this long — on top of Cooldown itself — both after Cooldown clears
  before the Captain plots, and after Cooldown starts before the
  Engineer refuels (§4.7), letting the game's UI settle. No effect on
  Cooldown's own timing (§5.7) or a manual Play (§6.5).
- **Auto wait (ms)**, default `300`. Applied automatically after every
  leaf instruction (played, stepped, or Auto-Pilot-triggered) unless the
  next instruction is itself a `WAIT`, or (when stepping) there's no next
  instruction.

A second row, purely a testing aid (never consulted for an Auto
Pilot-triggered run, which always resolves both placeholders live):
- **Test {NEXT_SYSTEM}**, default `Sol` — what `PASTE {NEXT_SYSTEM}`
  resolves to for a manual Play/Step from this tab.
- **Test {TRITIUM_LOOPS}**, default `1` — what `REPEAT {TRITIUM_LOOPS}`
  resolves to for a manual Play/Step, skipping the CMDR rescan (§6.4)
  entirely.

**Play**/**Step** (§6.3, §6.5) are disabled while either field is blank.
Session-only, like "Auto Copy To Clipboard" (§4.6) — never persisted.

### 6.2 Key Bindings

Nine named actions, each bound to a key (optionally with modifiers, e.g.
`Ctrl+Shift+J`) — a recorded script's token vocabulary (§6.4).

| Action | Default Key Binding |
|---|---|
| UP | Up Arrow |
| DOWN | Down Arrow |
| LEFT | Left Arrow |
| RIGHT | Right Arrow |
| SELECT | Space |
| PREV_PANEL | Del |
| NEXT_PANEL | End |
| EXIT | Backspace |
| RIGHT_PANEL | 4 |

- Each shown as a button labelled with its current binding. Clicking
  enters capture mode ("Press a key…"); the next key becomes its new
  binding (a bare modifier alone doesn't complete capture — only a full
  chord ending in a non-modifier does). `Escape` cancels capture.
- Persisted individually, restored on launch, defaulting to the table
  above until rebound.

### 6.3 Running Instances

Lists running instances — scanned independently of Roles (its own
process/journal scan), Roles-styled cards limited to commander name/
window position/monitor. **Refresh** rescans on demand (also runs once
on tab construction); same empty-state message as Roles.

One shared selection for both recording and playback (§6.4, §6.5). With
exactly one instance it's auto-selected (left alone if a second later
appears); with more than one, click a card to select (clicking a
different card moves, doesn't add to, the selection).

Button row: **Refresh** (left), **New Script** (§6.5) + media-style
**Record**/**Stop**/**Play** (right, New Script icon-only). A single Stop
covers both recording and playback (only one active at a time):
- **New Script**: creates a new empty macro, opens it in the editor —
  for writing/pasting a script by hand. Enabled under the same
  nothing-else-recording/playing/stepping condition as Record, but needs
  no selected instance and works even with the editor already open for
  another macro.
- **Record**: enabled only with an instance selected, nothing already
  recording/playing, and the editor closed.
- **Play**: enabled with both an instance and a macro selected, nothing
  recording. Any instance can play any macro. Stays enabled while
  playing: pressing again cancels the running playback, starts the new
  one.
- **Stop**: enabled while recording or playing; cancels whichever is
  active immediately, wherever in the script.

### 6.4 Recording

**Record** foregrounds the selected instance's window (same as Play),
then captures keyboard/mouse system-wide, filtered to moments the target
window is foreground (input elsewhere, including back at ED:FC Auto
Pilot's own window, isn't recorded). **Stop** ends capture into a new
named macro (§6.5). Only one recording at a time.

Each captured key/left-click is classified by hold duration: ≤400ms is a
tap/click; longer is a hold, recorded with duration. Click position is
window-client-relative (`0,0` top-left), taken from button-down (drags
aren't recorded; other buttons/scrolling aren't either). A ≥150ms gap
between inputs records as an explicit wait, preserving original pacing.

Script grammar:

```
UP                    # tap the action bound to UP
KEY Control+A         # tap a raw key with no bound action
HOLD RIGHT 800        # hold an action's key for 800ms
CLICK 240,160         # left-click at a position relative to the window
HOLD CLICK 240,160 600 # left-click-and-hold at a position for 600ms
CLICK {CENTRE},{CENTRE} # left-click the exact centre of the window
WAIT 500              # pause 500ms before the next step
PASTE {NEXT_SYSTEM}   # paste text via the clipboard + Ctrl+V
REPEAT 3
    UP
    WAIT 200
END
MACRO refuel
    RIGHT_PANEL
    SELECT
END
CALL refuel
```

- A bound action name taps its current key; `KEY <chord>` taps an
  arbitrary key/chord. `HOLD` takes the same token plus a ms duration.
- `CLICK <x>,<y>` clicks that position; `HOLD CLICK <x>,<y> <ms>` clicks
  and holds. Either coordinate may be `{CENTRE}`, resolved at play time
  to that axis' current window-client midpoint — reliable even after a
  resize since it was recorded/written.
- `REPEAT <n> … END`/`MACRO <name> … END` nest; `MACRO` blocks are
  top-level only (never inside a `REPEAT`) and never play inline — `CALL
  <name>` invokes one anywhere, including inside a `REPEAT`.
- `PASTE <text>` sets the clipboard and sends `Ctrl+V`. May contain the
  literal `{NEXT_SYSTEM}`, resolved at play time to the Route tab's
  in-progress row's system (empty text if none) for an Auto Pilot run,
  or the Options **Test {NEXT_SYSTEM}** field for a manual Play/Step.
  Never produced by recording — manual `Ctrl+V` during recording is
  captured as an ordinary key chord — only ever hand-added.
- `{TRITIUM_LOOPS}` is usable anywhere a number is expected (most usefully
  `REPEAT {TRITIUM_LOOPS}`). Unlike `{NEXT_SYSTEM}`/`{CENTRE}` (resolved
  live per-instruction), it's resolved once, textually, before parsing —
  a REPEAT count must be known before the script splits into steps.

  For an **Auto Pilot-triggered run** of a script containing this
  placeholder, ED:FC Auto Pilot first refreshes CMDR info for every
  running instance, then resolves it against: this tab's own selected
  instance if one is selected, otherwise the currently-assigned Engineer
  (the normal case). The value is the number of full ship-loads of
  tritium that instance still needs to fill its carrier's fuel depot to
  1000t *and* top off its own cargo hold, net of tritium already carried
  (cargo capacity, carrier fuel level, tracked onboard tritium — §5.3).
  Unknown/zero cargo capacity, or unknown carrier fuel level, resolves to
  `0` rather than risk dividing by an unknown capacity or treating an
  unknown fuel level as an empty depot. A script without the placeholder
  skips the rescan entirely and plays immediately — the rescan is a real,
  sometimes multi-second delay, and paying it unconditionally risks the
  target's in-game state drifting before the script starts.

  That value is then capped down (never up) so the refuel script can't
  take longer than **4 minutes 45 seconds** to play — Cooldown's own
  window is 5 minutes (§5.7), triggered right around its start (§4.7);
  this margin keeps a refuel from still running when the Captain's next
  plot cancels it out from under itself (panic mode, §4.7, treats that
  as an immediate hard stop). The cap is derived by estimating the
  script's own execution time (leaf durations, `AutoWaitMs` pacing,
  `REPEAT`/`CALL` expanded as playback would) at 1 loop and at 2,
  extrapolating linearly — exact for the standard `REPEAT
  {TRITIUM_LOOPS}` shape. If a full refill's loops wouldn't fit, this
  deliberately plays however many *do* fit rather than the full amount —
  a gradually depleting depot is an accepted, safe outcome (eventually a
  jump can't be requested at all, which panic mode already catches).

  For a **manual Play or Step**, the CMDR rescan never happens — the
  placeholder resolves directly to the Options **Test {TRITIUM_LOOPS}**
  field instead.
- Blank lines and `#`-prefixed lines are ignored; malformed lines are
  skipped, not rejected (the text is meant to be hand-edited).
- Multiple steps on one line, separated by `;` (whitespace around it
  ignored), e.g. `UP; WAIT 200; DOWN` — a readability convenience with no
  effect on playback order. Never produced by recording, only by hand.

### 6.5 Recorded Macros

Lists every macro recorded (or hand-written via **New Script**, newest
last). Compact list by default, one row per macro:
- **Name**, with the rest of the row's background also clickable
  (except the pencil/Delete icons) — selects it as the Play target
  (§6.3), highlighting the row, without opening it for editing.
- A **pencil** icon opens the full-size editor for that macro.
- A **Delete** icon, always available; deleting a currently-selected/
  edited macro clears that state.

Empty state (no macro yet): italic hint — "No macros recorded yet. See
the docs' sample scripts for ready-to-use examples, or click New Script
above to write one by hand." — "sample scripts" links to the docs site's
[Sample scripts](https://haggisandchips.github.io/RouteJumper/docs/macro-scripting.html#sample-scripts)
section. Disappears for good once the first macro exists.

The source commander a macro was recorded against shows as a caption
under its name — display only, no restriction on which instance plays
it. Omitted (no blank line) for a **New Script**-created macro (no source
commander).

**New Script** (§6.3's button row) creates a new empty macro (blank
script, no source commander), opens it in the editor. Persists and
behaves identically to a recorded macro from that point (Play, Step,
rename, delete, role-macro selection all work the same).

Clicking the pencil replaces the list with a full-size **editor**,
opening also selects that macro as the Play target:
- Editable **name** field.
- A large editable **script text** box beside a narrow fixed-width
  grammar reference panel (§6.4), grouped **Actions** (`ACTION_NAME`,
  `KEY`, `HOLD`, `CLICK`, `HOLD CLICK`, `PASTE`, `# comment`),
  **Controls** (`WAIT`, `REPEAT`, `MACRO`, `CALL`), **Variables**
  (`{CENTRE}`, `{NEXT_SYSTEM}`, `{TRITIUM_LOOPS}`).
- **Save** below the script box returns to the list — edits to either
  field save as they're made, independent of Save (pure navigation).
- Key Bindings (§6.2) stays visible above the editor. **Record** is
  disabled while the editor is open.
- A **Step** button beside Save, labelled with the next leaf instruction
  (e.g. "Next: RIGHT" — a REPEAT counted by unrolled iterations, a CALL
  by the macro it inlines). Runs just that instruction against the
  selected instance, then stops. `WAIT` steps are skipped entirely
  (nothing to observe). Re-foregrounds the target window each time
  (since it won't stay foreground after clicking back on the app).
  Wraps to the beginning at the end; editing the script restarts
  stepping from the beginning. Enabled under the same conditions as
  Play, plus a macro open in the editor with at least one step. **Stop**
  also cancels a step in progress.

**Record** again while macros exist always starts a brand-new recording
as an additional macro — never overwrites or appends to an existing one.

Playing (§6.3) foregrounds the instance, then executes steps in order
via simulated system-wide input (not window messages, since a DirectX
game reads actual input device state) — resolving `PASTE {NEXT_SYSTEM}`
against the Route tab's live state. The configured Auto wait (§6.1)
applies after each step unless the next is itself a `WAIT` — same
whether started manually or by Auto Pilot (§4.7). A new playback cancels
a previous in-progress one, same as **Stop** — cancellable at any point,
not just between steps.

If the target window loses focus mid-playback, playback aborts
immediately and a closeable, solid-fill warning banner shows above the
tab area (visible regardless of active tab) until dismissed or another
playback starts. A user-initiated Stop is not treated as this failure
and shows no message.

### 6.6 Persistence
See §7.

---

## 7. Persistence

A single generic `Settings(Key TEXT PRIMARY KEY, Value TEXT)` table in a
local SQLite database at `%LocalAppData%\EDFCAutoPilot\routejumper.db`.
Every operation opens/closes its own short-lived connection. A
persistence failure degrades to "nothing persisted" rather than the app
failing to start.

A second table, `ResolvedLookups (Kind TEXT, SystemKey TEXT, Value TEXT,
PRIMARY KEY (Kind, SystemKey))`, holds the subset of the coordinate/
star-type cache (§4.9) worth keeping forever — `Kind` one of `Coords`/
`StarType`. A value EDSM itself resolves is never written here
(session-only, §4.9) — only a journal/Spansh seed filling a
confirmed-unresolvable-by-EDSM gap is. System address (id64) is
session-only, not persisted, until a feature needs it. Its own table
rather than more `Settings` rows (a different access pattern — a point
lookup per route row). `StarType`'s `Value` is a canonical `StarClass`
code, never pre-formatted display text (§4.9).

A third table, `UnresolvedLookups (Kind TEXT, SystemKey TEXT,
LastAttemptUtc TEXT, PRIMARY KEY (Kind, SystemKey))`, backs the EDSM
retry cooldown (§4.9) — its own table for the same reason.

A fourth table, `CompanionSessions (SessionId TEXT PRIMARY KEY,
DeleteAfterUtc TEXT NOT NULL)`, backs the companion site's self-managed
housekeeping (§13) — records a finished session once its retention
window is known, deleted (from both this table and Firestore) once due.

A separate, hand-editable `routejumper.conf` sits beside the database
(§5.2) for user-editable configuration: the journal folder, §12's log
housekeeping settings, the EDSM retry cooldown
(`EdsmUnresolvedRetryHours`) and batch size (`EdsmCoordinatesBatchSize`,
§4.9), the Spansh autocomplete debounce (`SpanshAutocompleteDebounceMs`,
§4.12), the companion site's base URL (`CompanionSiteBaseUrl`, §13), and
its session retention (`CompanionSessionRetentionHours`, §13) — rather
than internal app state like the table below.

| What | Persisted when | Restored when |
|---|---|---|
| Route text | Every Save (first time or after Edit) | App startup, rebuilt via the normal Save path |
| Route type (Plain/Neutron/Galaxy) and per-row Jumps/Refuel/Inject/Neutron data (§4.2/§4.12) | Every ImportFromSpansh with a Neutron/Galaxy route type | App startup, re-applied after the normal Save path rebuilds Rows — reset to Plain/cleared by every Save (manual, Import Current Route, Trim for FC, or this same restore), so only a Save immediately following a Spansh import can leave it non-Plain |
| Window position, size, maximized state | Window closing | App startup — in place of default rightmost-monitor placement, unless no longer reachable |
| Route table column widths (icon, `#`, `System`, `Distance`, `Jumps`, `Refuel`, `Inject`, `Neutron`, `Star Type` — not `Status`) | Window closing | App startup, per column — default width until first resized |
| Journal/Spansh-seeded coordinates/star type filling a confirmed EDSM gap, in `ResolvedLookups` | First seed after EDSM confirms no record | Every later Save/restore, indefinitely — never re-fetched from EDSM once cached |
| EDSM unresolved-lookup cooldown, by system name and kind | Every lookup EDSM confirms has no record | Every later lookup, until `EdsmUnresolvedRetryHours` lapses — cleared immediately on resolution |
| Companion session id + delete-after instant, in `CompanionSessions` | Every `StartSessionAsync` (fixed 72h deadline), shortened by every `EndSession` (`CompanionSessionRetentionHours` after run ends, clamped to the 72h ceiling) | Every app launch — a session past its delete-after instant is deleted from Firestore and forgotten; one not yet due is left alone |
| Captain/Engineer role, by commander FID | Assigned/explicitly unassigned | Every Roles refresh, while unassigned in memory |
| Captain/Engineer role macro, by macro Id | Selected/cleared | Every Roles refresh, while unselected in memory |
| Controls tab options (Auto Pilot delay, Auto wait) | Every change | App startup, 5000ms/300ms defaults until first changed |
| Controls tab key bindings | Every successful capture | App startup, per action — default until rebound |
| Controls tab recorded macros | Every new recording, edit, or delete | App startup, as a single collection |
| Announcement voice, volume, muted | Every change | App startup — engine default voice, 100 volume, unmuted until changed |
| Automatic update check enabled | Every change | Checked fresh every launch before the silent check — enabled until changed |
| Tracking mode (Fleet Carrier/Ship) | Every toggle | App startup — Fleet Carrier until changed |
| Tracked instance, by commander FID | Assigned/explicitly unassigned | Every Track tab refresh, while unassigned in memory |

- Role assignment restores by FID (not `ProcessId`, which doesn't survive
  a process restart) — covers both "app just launched" and "role
  holder's game process restarted mid-session" identically; a restored
  Captain match goes through the same reset-and-replay as a fresh manual
  assignment.
- Explicitly unassigning clears the persisted FID (no auto-reassignment
  next refresh); a role holder's process stopping clears only the
  in-memory assignment, leaving the FID to pick back up automatically if
  that instance reappears.
- A role's macro persists by the macro's stable Id, not its editable
  name — renaming never loses the selection; deleting the selected macro
  clears the role's selection to unset.
- "Auto Copy To Clipboard" (§4.6) is deliberately **not** persisted.
- Not persisted: per-row progress (icon/status), the last-selected tab.

---

## 8. Track Tab (Ship Mode)

Only visible in Ship mode (§3.4) — Roles/Controls hidden while shown, and
vice versa. Leaner than Roles: one role ("the tracked instance"), no
macros, no cargo gating — Ship mode has no Auto Pilot to feed.

### 8.1 Instance discovery
Reuses §5.1 verbatim — same process discovery, background-thread
Refresh, empty-state message. Cards show only commander name, current
location, journal filename (no cargo/carrier fields). Clicking the
journal filename copies + confirmation sound, same as §5.3 (no visual
indicator afterward).

### 8.2 Tracking a single instance
- A single **Track** toggle per card (in place of Captain/Engineer)
  assigns/unassigns that instance — only one tracked at a time; assigning
  a new one unassigns whichever held it.
- Assigning resets the whole route (icon → None, Status → blank), then
  applies that instance's own ship's *current* status as a single
  catch-up result (§8.3) — explicitly the CMDR's own ship, never their
  fleet carrier even if the same journal has both event families. Also
  triggers a background rescan of this tab.
- Unassigning (explicit, or the instance stopping) leaves the table
  exactly as displayed — tracking simply stops. A stopped process clears
  only the in-memory assignment; the persisted FID (§7) picks it back up
  automatically if it reappears.
- Persisted by commander FID, not `ProcessId` (§7) — a restored match
  goes through the same reset-and-replay as a fresh assignment.
- Switching away from Ship mode stops watching the journal but doesn't
  clear the assignment — switching back resumes with a fresh
  reset-and-replay.

### 8.3 Journal-driven route updates
Analogous to §5.7, driven by the ship, different event vocabulary/
timing. Unlike §5.7's single combined stream, Ship mode keeps two things
independent (real play can change one without the other — repeatedly
targeting/re-targeting without jumping):

- **Current system** (drives Complete/in-progress, like §5.7's Arrived
  step) comes **only** from `Location`/`FSDJump` — both authoritative
  "ship is now at X," never `FSDTarget`/`StartJump` (intentions/in-flight
  only). Whichever fires later wins. Named system not found in the route
  at all → **nothing marked Complete** — left as-is rather than guessed.
  - `FSDJump` isn't applied immediately (too early — see Arrived below).
  - `Location` (session start, relog, or another definite-position
    snapshot) *is* applied immediately, with no wait, and never puts the
    next row into `Cooldown` (not proof a jump just completed) — it
    supersedes an `FSDJump` arrival still awaiting Music confirmation.
- **`Targeted`** is a separate overlay, never proof of arrival, never
  completing anything: `FSDTarget` sets `Targeted` on whichever
  not-yet-complete row it names (only one at a time). Elite's own
  multi-route auto-target often fires the *next* hop's `FSDTarget` while
  the *current* hop is still `Jumping`/`Cooldown` — applying it
  immediately then would mark the wrong row, so it's deferred until that
  cycle finishes (`Cooldown` clears), then applied to whichever row is
  genuinely current. `Targeted` clears — by a fresh `FSDTarget` for a
  different/off-route system, or `NavRouteClear` — without ever leaving
  its old row looking current; a row that only showed an icon *because*
  it was `Targeted` reverts fully to none. Only the one genuine current
  row keeps its icon regardless of targeting churn elsewhere.
- **`Jumping`**: `StartJump` with `JumpType` `"Hyperspace"` sets it
  immediately (`"Supercharge"`, an in-system boost, is ignored) — no lead
  time to derive, unlike Fleet Carrier mode.
- **Arrived's `FSDJump` path**: not applied off `FSDJump` directly (fires
  while still mid-transition) — the actual signal is the following
  `Music` event (`MusicTrack` `DestinationFromHyperspace` or
  `Supercruise`, which depends on some other in-game factor). `FSDJump`
  records a pending arrival; the next qualifying `Music` resolves it. A
  generous fallback timer resolves it anyway if neither track shows up
  in time — a backstop, not the expected path.
- **`Cooldown`** (shown on the row after the arrived-at one, same as
  §5.7): clears a **provisional** fixed duration after a genuinely live
  `FSDJump`/Music resolution specifically — never off `Location`. Ships
  do have a real FSD cooldown (not cosmetic dressing) — its exact
  duration hasn't been measured yet, so a placeholder is used.
- No known journal event for an aborted ship jump (no
  `StartJumpCancelled`) — an interrupted jump leaves its row stuck on
  `Jumping` until the manual "Set next system" override (§4.2); not
  auto-recovered.
- Same catch-up principle as §5.7: on assignment (or resuming after a
  mode switch), the whole journal is read first, oldest→newest, to
  compute a single result for *each* independent piece — current system
  (last of `Location`/`FSDJump`), an in-flight jump (a Hyperspace
  `StartJump` not yet superseded), a still-open target (an `FSDTarget`
  not yet superseded by `NavRouteClear` or a since-jump/arrival) — and
  only those results apply, never one event per line. Only after this
  does live tailing begin.

### 8.4 Distinguishing the ship from a fleet carrier
A commander with both a ship and a fleet carrier has both event
families in the same journal. Never confused: the ship family
(`FSDTarget`/`StartJump`/`FSDJump`) carries no `CarrierID`/`CarrierType`
at all — structurally separate, no filtering needed.

### 8.5 Auto Copy To Clipboard
Identical to §4.6 — a live ship arrival (`FSDJump`, ahead of the delayed
Arrived/Cooldown UI transition) copies the next row's system if on.

### 8.6 No Auto Pilot
No macro automation at all — the CMDR flies every jump manually. The
Route tab's Auto Pilot button is hidden (not disabled) while Ship mode
is active (§4.2); any run in progress stops outright the instant Ship
mode is switched on.

---

## 9. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (`net8.0-windows`), MVVM throughout — no
  business logic in code-behind. The window-placement, focus-management,
  clipboard-monitoring, and Preferences-dialog-opening code in
  `MainWindow.xaml.cs`/`RouteView.xaml.cs` is the sole deliberate
  exception (needs direct HWND/message-pump access a ViewModel lacks).
- **Responsiveness:** the UI stays interactive throughout a Roles tab
  refresh (background-thread scan).
- **No external dependencies** beyond .NET/WPF base libraries,
  `MaterialDesignThemes`/`MaterialDesignColors`, `Microsoft.Data.Sqlite`,
  `System.Speech` (§4.8), and `QRCoder` (companion QR code, §13).
- **Network:** Distance/Star Type (§4.9) is the first outbound
  third-party call — [EDSM](https://www.edsm.net), free, no API key,
  best-effort, cached for the session (persisted indefinitely only for
  the rarer EDSM-can't-resolve case, §4.9/§7), sending only system names.
  The Spansh dialog (§4.12) is the second — used with the author's
  express permission against undocumented endpoints; the Fleet
  Carrier/Neutron Plotter tabs send only the two chosen system
  names/ids; Galaxy Plotter also sends derived fuel/mass/tank-capacity
  numbers from `Loadout` (§4.12), never raw journal data or anything
  commander-identifying. The companion site's Firestore REST calls
  (§13) are the third — unauthenticated, sending only route/row system
  names, status text, and timestamps — never commander/journal data.
  Every call here is best-effort/fire-and-forget; a failure never blocks
  Auto Pilot or tracking, only logged (category `Companion`, §12).

---

## 10. Styling — Material Design

- `App.xaml` merges a `materialDesign:BundledTheme` (`PrimaryColor="Blue"`,
  `SecondaryColor="Cyan"`, `BaseTheme="Light"`) plus
  `MaterialDesign2.Defaults.xaml`. No dark/light theme toggle.
- `MainWindow` uses `MaterialDesignWindow` so the title bar is themed too.
- Row/status icons and the "Auto Copy To Clipboard" switch use the
  toolkit's `PackIcon`/`ToggleButton` (`MaterialDesignSwitchToggleButton`),
  colored from `MaterialDesign.Brush.Secondary`, never hardcoded colors.

---

## 11. Acceptance Criteria

Each item below is a short testable checklist entry; see the referenced
section for the full mechanism/exact timing/wording.

1. Launch shows Route/Roles/Controls tabs, left-edge headings; Route
   selected by default (§3.1).
2. Route tab starts in Edit state: empty text box, Save disabled, Cancel
   enabled, focus on the box (§4.1).
3. Typing enables Save; on a never-saved launch, Cancel clears the box
   (§4.1).
4. Saving N non-blank lines produces N numbered rows, `System` matching
   input verbatim, `Status` empty, only row 1 shows an icon (§4.3).
5. Edit restores the text unchanged with focus; re-Save always produces
   a fresh table (re-derived from the assigned Captain's journal, or row
   1 defaults in-progress); Cancel instead discards changes and leaves
   the table exactly as it was (§4.1, §4.2).
6. Auto Pilot flips its label and disables/enables Edit; drives the
   route via the Captain's macro while engaged (§4.2, §4.7).
7. Clicking a row copies its system, plays a confirmation sound, shows
   the clipboard icon (§4.2, §4.6).
8. "Set next system" marks earlier rows Complete, the clicked row
   current, later rows reset — regardless of prior state (§4.2).
9. Zero/N running `EliteDangerous64.exe` processes shows the correct
   empty state or N correctly-attributed cards (§5.1).
10. Restarting one instance updates only its own card on refresh (§5.1).
11. Captain/Engineer each assign to exactly one instance independently;
    both can share an instance (§5.4).
12. Assigning Captain resets the route, then applies a single
    catch-up result from the whole journal — including skipped rows —
    per §5.7's algorithm, never a naive line-by-line replay.
13. `Jumping` starts 3 min before `DepartureTime`; the composite Arrived
    step fires 1 min after arrival, on the *next* row; `Cooldown` only
    ever appears for a live-observed arrival, clearing 4 min later
    (§5.7, §4.4).
14. Engineer is disabled for zero/unknown cargo capacity (§5.4).
15. "Auto Copy To Clipboard" copies the in-progress row on toggle-on, and
    the next row on each live arrival (§4.6).
16. Route, window bounds, and Captain assignment all persist and
    restore on relaunch (with a fresh journal replay); "Auto Copy To
    Clipboard" always resets off (§7).
17. Explicitly unassigning a role clears its persisted FID (§7).
18. `CarrierJumpCancelled` clears `Status` on the in-progress row and
    cancels any already-scheduled `Jumping` transition for it (§5.7).
19. A live `CarrierStats` event triggers an automatic Roles refresh; the
    same event during historical replay does not (§5.1).
20. Carrier fuel shows as a `Nt fuel` sub-line once known, resolved
    against the commander's own carrier only (§5.3).
21. `routejumper.conf` is created on first launch with journal-folder
    defaults; a hand-edit takes effect on the next Refresh with no
    restart (§5.2).
22. A ship with no `Cargo` event this session shows 0, not "Unknown";
    Engineer eligibility follows capacity alone (§5.3).
23. Clicking a card's journal filename copies it + confirmation sound,
    no clipboard-source icon (§5.3).
24. Controls tab shows all nine key-binding defaults; capture mode
    rebinds on the next chord, `Escape` cancels (§6.2).
25. Running Instances scans/lists independently of Roles; auto-selects
    with one instance; Record requires a selection and nothing else
    active (§6.3).
26. Record captures tap/hold and click/held-click (400ms threshold),
    window-relative click position, and ≥150ms gaps as waits, producing
    the documented grammar on Stop as a new macro (§6.4).
27. `{CENTRE}` resolves at play time against the target window's
    then-current client-area size (§6.4).
28. The macro list's row (minus pencil/delete) selects the Play target;
    Play works for any instance/macro pairing (§6.5).
29. The pencil opens an editor (name, script box, grammar reference)
    that saves edits as made; Record disables while open (§6.5).
30. Playing resolves `PASTE {NEXT_SYSTEM}` against the current
    in-progress row; a new playback cancels one in progress, same as
    Stop (§6.5).
31. Key bindings and recorded macros both survive a restart (§6.2, §6.6).
32. Losing focus mid-playback aborts it and shows the closeable
    solid-red warning banner; Stop/a replacement playback doesn't
    (§6.5).
33. Roles shows Captain/Engineer macro pickers; Auto Pilot's enablement
    re-evaluates immediately on any change (§5.5, §4.2).
34. Deleting a macro selected for a role clears that selection; renaming
    doesn't (§7).
35. Controls shows Auto Pilot delay (default 5000) and Auto wait
    (default 300), persisted (§6.1).
36. Auto Pilot plays the Captain's macro per §4.7's exact rule (blank →
    immediate, Cooldown → after clear+delay, already in flight → no
    replay) for the first row and every later one, until Complete or
    stopped (§4.7).
37. Step runs one leaf instruction, re-foregrounding first, labelled with
    what's next; `WAIT` steps are skipped; wraps at the end; Stop
    cancels it; Auto wait applies between steps per the same rule as
    Play (§6.5, §6.1).
38. `{TRITIUM_LOOPS}` resolution for an Auto Pilot run rescans CMDR info
    and resolves per §6.4's formula, `0` on unknown capacity/fuel; no
    rescan for a script without the placeholder (§6.4).
39. The Engineer's refuel (independent of the Captain's plot) triggers
    once per row's Cooldown, delay-gated the same way, only if Engineer
    is assigned (§4.7).
40. Plotted/Jumping/Cooldown rows show a live countdown bar and
    `Status (H:MM:SS)` text; blank/Complete show neither, same row
    height either way (§4.4).
41. Route table column widths persist per column (except `Status`,
    which always fills remaining width) (§4.2, §7).
42. Auto wait applies after every script instruction unless the next is
    a `WAIT`, uniformly regardless of trigger (§6.4, §6.5).
43. Test {NEXT_SYSTEM}/{TRITIUM_LOOPS} (defaults `Sol`/`1`) resolve a
    manual Play/Step with no rescan; Play/Step disable while either is
    blank; neither persists (§6.1).
44. New Script creates and opens an empty, persisted macro under the
    same enablement rule as Record, but without needing a selected
    instance (§6.5).
45. File menu (Preferences/Exit) and the always-visible mute button are
    present regardless of active tab (§3.4).
46. Auto Pilot speaks the 30s/5s Plotting/Refueling announcements per
    §4.8's exact scheduling and skip/forgiveness rules; muting
    suppresses both.
47. Preferences' Voice dropdown lists every installed voice (cleaned
    display name, deduped Desktop entries); Test always plays; Volume
    persists immediately (§3.5).
48. Voice/volume/muted all survive a restart with documented defaults
    (§3.5, §7).
49. Auto Pilot's `Plotting` status shows an indeterminate bar and no
    countdown text until a real `CarrierJumpRequest` arrives (§4.4,
    §4.7).
50. Help > About shows the documented content; Help > Check for Updates
    reports the outcome via message box (§3.6, §3.7).
51. The Fleet Carrier/Ship toggle defaults to Fleet Carrier and swaps
    tabs/Auto Pilot visibility/journal watcher on toggle, persisting
    across restarts (§3.4).
52. Switching to Ship mode while Auto Pilot is engaged stops it outright
    (§4.2, §8.6).
53. The Track tab scans/lists instances like Roles (commander, location,
    journal only); exactly one Track assignment at a time (§8.1, §8.2).
54. Assigning a tracked instance resets the route, then applies a single
    catch-up result from that ship's own journal, never a fleet carrier
    it might also own (§8.2, §8.3).
55. Ship mode's Status progression (`Targeted` → `Jumping` → arrived →
    `Cooldown` → blank) follows §8.3's exact event vocabulary/deferral
    rules, confirmed against real journal data.
56. The composite Arrived step is driven by the following `Music` event,
    not `FSDJump` itself; Auto Copy To Clipboard still fires off the raw
    `FSDJump` line (§8.3, §8.5).
57. An off-route `FSDTarget` clears whichever row was showing `Targeted`
    back to blank, never leaving it stuck (§8.3).
58. Unassigning the tracked instance, or switching away from Ship mode,
    leaves the table exactly as displayed; reassigning/switching back
    always re-derives via a fresh catch-up (§8.2).
59. Tracking mode and tracked instance (by FID) both survive a restart
    (§7, §8.2).
60. Distance/Star Type populate asynchronously without blocking the
    table; row 1's Distance measures from the correct mode-appropriate
    ship position (§4.9).
61. An EDSM miss/failure blanks the cell rather than blocking; a
    resolved system is reused all session; across a restart, an
    EDSM-resolved value re-queries (cheap) while a persisted
    journal/Spansh seed reuses instantly (§4.9).
62. `FSDTarget`/`NavRoute` events opportunistically seed the cache in
    both modes; an already-displayed row refreshes live once seeded
    (§4.9).
63. A `Plotted`/`Arrived` event targeting a row further ahead than
    expected completes the skipped rows too, live or replayed;
    `Targeted` never completes anything (§5.7, §8.3).
64. Ship mode's current-system tracking is driven only by
    `Location`/`FSDJump`; repeated targeting/re-targeting never marks a
    row Complete and never leaves more than one row with an icon
    (§8.3).
65. `NavRouteClear` clears whichever row shows `Targeted`, the same as a
    fresh off-route `FSDTarget` (§8.3).
66. `Location` updates current-system tracking immediately but never
    starts `Cooldown`, even live; it supersedes a pending `FSDJump`
    arrival awaiting Music confirmation (§8.3).
67. Ship mode's catch-up resolves current-system/in-flight-jump/
    still-open-target as three independent results, not one
    most-recent-wins answer (§8.3).
68. Panic mode stops Auto Pilot and shows a banner per §4.7's exact
    conditions — including a completed-but-unconfirmed Captain's plot
    or Engineer's refuel deposit.
69. `{TRITIUM_LOOPS}` for an Auto Pilot run never resolves to a script
    that would exceed 4:45 playback, estimated per §6.4's formula; a
    manual Test value is never capped this way.
70. Help menu shows Logs above About (§3.4).
71. Logs window starts empty and shows entries logged from that point,
    within about a second, without blocking other tabs (§3.8).
72. Every log line is timestamped with level/category; logging never
    performs file I/O on the calling thread (§12).
73. The Logs folder holds one date-stamped file per day, tailable live,
    rolling to a new numbered segment past the size cap (§12).
74. Log housekeeping (7 days / 10MB / 100MB defaults) keeps the folder
    bounded automatically, oldest-first, never touching the active file
    (§12).
75. Distance and Star Type each resolve via one batched EDSM request per
    chunk, never one request per row, even when only one column is
    missing (§4.9).
76. An EDSM-confirmed-unresolved system isn't re-queried for
    `EdsmUnresolvedRetryHours` (default 12h), surviving a restart within
    that window; an early resolution clears the cooldown immediately
    (§4.9).
77. Help > Check for Updates always runs regardless of the Preferences
    opt-out, which gates only the silent startup check (§3.7).
78. Import Current Route shows a Yes/No confirmation, then replaces the
    route from `NavRoute.json` (skipping the departure entry), seeding
    the cache along the way; failure is logged, not dialoged (§4.10).
79. Trim for FC requires a Captain with a known carrier location
    (else an explanatory message box, no confirmation reached);
    otherwise a Yes/No confirmation, then the greedy 500ly-hop trim
    anchored from the carrier's real location, applied immediately;
    Ship-mode-hidden; missing coordinates block it (logged) (§4.11).
80. Each Spansh tab's Calculate is independently gated/polled/
    cancellable per §4.12; success seeds the cache and replaces the
    route with no confirmation; failure leaves it untouched with an
    explanatory status message.
81. The three Spansh menu items open the correspondingly-preselected
    tab of the same dialog; the Fleet Carrier tab's Source pre-fills
    from the Captain's real carrier location when resolvable (§4.12).
82. Neutron Plotter's Range always starts blank; Source pre-fills from
    the current system; the supercharge radio defaults per the
    `_overchargebooster_mkii` suffix rule (§4.12).
83. Each Spansh tab's footnote shows only while that tab is active, at
    the dialog's full width (§4.12).
84. Distance/Star Type each resolve via one batched request per chunk
    covering every needed row, even when coordinates are already cached
    but star types aren't (§4.9).
85. A dismissible "Plot needed"/"Target needed" advisory banner appears
    only once an enrichment pass completes with at least one such row,
    resetting on every fresh Save (§4.9).
86. The Engineer's refuel/announcements are never scheduled for the
    route's last row; the final route-completion announcement is spoken
    only for that stop reason (§4.7, §4.8).
87. Galaxy Plotter's Calculate stays disabled with an explanatory status
    message until ship-build resolution completes (§4.12).
88. Galaxy Plotter's Cargo pre-fills from tracked cargo; Reserve tank/
    Algorithm/checkboxes all start at Spansh's own defaults, all
    editable (§4.12).
89. Galaxy Plotter sends only derived fuel/mass/tank-capacity numbers,
    no separate ship-build upload (§4.12, §9).
90. Neutron Plotter results show a `Jumps` column; Galaxy Plotter results
    show `Refuel`/`Inject`/`Neutron` columns; never both at once, never
    for any other route source (§4.2, §4.12).
91. Route type and per-row Jumps/Refuel/Inject/Neutron data persist and
    restore correctly across every consecutive restart; any plain Save
    reverts to Plain (§7).
92. Editing a Neutron/Galaxy route shows a Proceed/Cancel conversion
    warning; a plain route shows none; Edit's own enablement is
    unaffected (§4.2).
93. Saving/restoring a Neutron/Galaxy route forces Ship mode and
    disables the Fleet Carrier chip with the documented tooltip, with no
    confirmation; reverting to Plain re-enables the chip without
    auto-switching modes (§3.4).
94. The companion QR/link button shows only while Auto Pilot is running
    with a session actually started, and hides again for as long as any
    macro is executing (closing its popup if open); a start failure is
    logged, not surfaced (§13).
95. All four companion event kinds (Plotted/Arrived/Refueled/Panic)
    appear in the live feed shortly after the in-app event, without
    delaying Auto Pilot (§13).
96. A companion publish failure never surfaces to the CMDR and never
    stops Auto Pilot — logged only (§13).
97. Stopping Auto Pilot marks the session `completed` or `panicked`
    correctly; re-engaging always starts a brand-new session (§13).

---

## 12. Logging

A background, non-blocking facility recording significant events for
diagnosis — outbound HTTP requests, every journal event either watcher
actually *acts on* (a row transition, an opportunistic cache seed,
`CarrierStats`), journal-watch start/stop, role/track assignment, "Auto
Copy To Clipboard" firing, Auto Pilot triggers/panic stops, macro
recording/playback, update checks, persistence failures. Each line
carries only the fields that action actually used, never a raw-JSON
dump. Two sinks per entry: a durable date-stamped file (always written)
and the Logs window (§3.8, only while open).

- **Never on the hot path**: logging only enqueues onto an in-memory
  queue — never disk I/O or blocking on the calling thread. Matters most
  for the event-driven Sequencing/ engine (CLAUDE.md) and macro playback
  timing. A single background writer drains the queue.
- **Format**: `yyyy-MM-dd HH:mm:ss.fff [LEVEL ] Category: message`, e.g.
  `2026-08-15 09:05:03.250 [INFO ] Http: -> GET
  https://www.edsm.net/api-v1/systems?...`. Levels Debug/Info/Warn/
  Error; Warn/Error may carry an exception's type/message. Categories:
  `Http`, `Journal`, `AutoPilot`, `Roles`, `Track`, `Route`, `Macro`,
  `Update`, `Settings`, `Config`, `Edsm`, `Spansh`, `Companion`, `Scan`,
  `App`.
- **HTTP requests**: every direct outbound call (EDSM, Spansh, companion
  Firestore — the app's only three direct-HttpClient integrations) logs
  method+URL out and status+elapsed time back (or a warning on outright
  failure). Velopack's own internal update-check calls aren't
  individually visible (no logging seam) — covered instead by an
  Info/Warn line around each check's start/outcome (§3.7).
- **File** (`%LocalAppData%\EDFCAutoPilot\Logs\routejumper-yyyy-MM-dd.log`,
  `.Dev` for an unpackaged run): one file per day, opened for
  concurrent-read append (e.g. `Get-Content -Wait`) — the writer flushes
  after draining the queue, so a concurrent tail sees new lines with
  minimal lag without a per-line flush cost. A file crossing the
  per-file size cap mid-day rolls to a new numbered segment
  (`routejumper-yyyy-MM-dd.2.log`, ...).
- **Housekeeping**: keeps the folder bounded, via `routejumper.conf`
  (written with defaults on first creation, re-read periodically so a
  hand-edit takes effect without a restart):

  | Key | Default | Meaning |
  |---|---|---|
  | `LogRetentionDays` | `7` | Files last written this many days ago or older are deleted |
  | `LogMaxFileSizeMB` | `10` | Per-file cap before rolling to a new numbered segment |
  | `LogMaxTotalSizeMB` | `100` | Total cap across every log file - oldest files are deleted first once exceeded |

  Runs at startup, on every rollover, and periodically in the
  background — never against the file currently being written to.
- **Logs window** (§3.8): a live in-memory mirror while open — never the
  durable record, never required for anything to actually be logged.
- Logging failures (e.g. a permissions issue) degrade to "not written to
  disk" rather than crashing the app or interrupting the caller — the
  same philosophy `AppSettingsStore`/`AppConfigStore` use (§7).

---

## 13. Companion Site

A small, mobile-friendly companion website (Angular, statically hosted
via GitHub Pages alongside the docs — `docs/` at `/docs`, the companion
app at `/app`, both deployed together by `.github/workflows/pages.yml`)
showing a live, read-only feed of an in-progress Auto Pilot run (§4.7),
so the CMDR can check in from a phone.

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
- **Session lifecycle**: a brand-new session (fresh UUID, fresh QR code)
  every Auto Pilot engage, never reused. A session's header doc tracks
  `status`: `active` while running, `completed` on a normal stop (manual
  or route-complete), `panicked` on a panic-mode stop (§4.7) — shown on
  the companion site so a viewer can tell a session ended.
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
