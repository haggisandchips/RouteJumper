---
title: User Guide
---

# ED:FC Auto Pilot User Guide

This page covers what ED:FC Auto Pilot does, how each tab works, where its
data lives, and how to troubleshoot common issues. For installing the
app and the fastest path to a working Auto Pilot setup, see the
[README](https://github.com/haggisandchips/RouteJumper#readme). For the
macro scripting language and ready-to-use sample scripts, see
[Macro Scripting](macro-scripting.md). For building from source,
running tests, and cutting releases, see [Development](development.md).

---

## What it does

ED:FC Auto Pilot has two mutually exclusive tracking modes, switched via a
**Fleet Carrier / Ship** toggle beside the menu bar:

- **Fleet Carrier mode** (the default) — paste a route, assign a
  Captain's running game instance, and ED:FC Auto Pilot tracks the
  carrier's progress against that commander's journal, with an optional
  **Auto Pilot** that drives the whole route itself.
- **Ship mode** — for a solo commander flying their own hand-plotted
  route (e.g. from a neutron-highway planner): paste the route, assign
  your own running instance on the **Track** tab, and ED:FC Auto Pilot
  tracks your ship's progress the same way — purely passively, since
  there's no automation for a route you're flying yourself.

Common to both modes:

- **Tracks a pasted route** against a real Elite Dangerous journal — no
  manual bookkeeping. Handles plots/targets, cancellations, and
  cooldowns.
- **Shows each row's leg distance and destination star type**
  (`Distance`/`Star Type` columns), looked up against
  [EDSM](https://www.edsm.net) in the background as soon as the route is
  saved.
- **Copies the next system to the clipboard automatically** as you
  arrive, so it's ready to paste into the game.
- **Detects every running Elite Dangerous instance** on the machine and
  matches each one to its journal file.
- **Calculates and imports a route via Spansh** (Integrations > Spansh…)
  without leaving the app — see [Spansh integration](#spansh-integration)
  below.
- **Logs significant events** (journal-driven row transitions, EDSM
  requests, Auto Pilot triggers, ...) to a rolling file and an optional
  live viewer (Help > Logs) — see [Logs](#logs) below.

Fleet Carrier mode adds:

- **Drives the route automatically (Auto Pilot)** by playing a recorded
  macro against the Captain's game window to plot each jump, and (if an
  Engineer is assigned) another macro to refuel the carrier every
  cooldown. A deliberately paranoid "panic mode" stops Auto Pilot the
  instant anything about a plot or refuel looks wrong, rather than
  risking the carrier jumping further away on the strength of a macro
  that's silently not working.
- **Records and replays keyboard/mouse macros** against any running
  Elite Dangerous instance — a small scripting language (see
  [Macro Scripting](macro-scripting.md)) lets you hand-edit what gets
  recorded, or write scripts from scratch.
- **Announces upcoming Auto Pilot actions out loud** ("Plotting in 30
  seconds", "Refueling in 5 seconds") via the OS speech engine, so you
  don't have to keep watching the app to know when it's about to send
  input to the game. Configurable (voice, volume, and whether to check
  for updates on startup) via File > Preferences, and mutable via the
  button beside the menu bar.

## The tabs

### Route tab

The main working screen in both tracking modes. It starts in an **Edit**
state — a plain multi-line text box — where you paste a route, one
system per line. Clicking **Save** switches to a **Table** state: a
read-only table with a status icon, row number, system name, `Distance`,
`Star Type`, and status for each row, plus **Import Current Route**,
**Trim for FC** and **Auto Pilot** (the latter two Fleet Carrier mode
only), and **Edit** buttons.

- **Import Current Route** replaces the saved route with whatever's
  currently plotted in-game (read straight from the game's own
  `NavRoute.json`) — no Captain/tracked instance needs to be assigned
  first.
- **Trim for FC** (Fleet Carrier mode only) collapses the saved route
  down to a series of hops no longer than 500ly each, dropping whichever
  systems aren't actually needed to stay within a fleet carrier's real
  jump range — handy after importing or pasting a route with lots of
  closely-spaced waypoints (e.g. from a neutron-highway plotter). **Tip:**
  plotting the in-game route with a small-jump-range ship packs more,
  closer-together waypoints into `NavRoute.json` for Trim to choose
  from, so the resulting carrier hops land closer to the full 500ly
  than plotting the same route with a long-range explorer would.
  Requires a Captain assigned (Roles tab) with their carrier's own current
  location known — if not, a message tells you to assign one or open
  Carrier Management in-game first. That real location becomes the
  trimmed route's own new first entry and is what the trim is anchored
  from, rather than the pasted route's own row 1, since the carrier may
  already be mid-route by the time you click Trim; anchoring from the
  wrong assumed starting point risks plotting an inefficient first hop.
- Clicking a row copies its system name to the clipboard (with a
  confirmation sound and a small clipboard icon on the row).
- Right-clicking a row offers **"Set next system"** — a manual override
  that marks every row before it Complete, makes it the current row, and
  resets everything after it. This is your escape hatch whenever
  automatic detection gets it wrong or you're genuinely off-route.
- **"Auto Copy To Clipboard"** (a toggle above the table) copies the
  *next* system to the clipboard the instant your arrival is observed —
  handy for quickly pasting into the game's galaxy map.
- **`Distance`** is the leg distance (previous row → this row, or your
  current position → row 1), and **`Star Type`** is that row's own
  system's main star, both resolved against EDSM in the background as
  each row is looked up — a system EDSM has no coordinates for, shows
  small "Plot needed" text instead of a blank cell (or "Target needed"
  if only the star type is missing), so it's obvious which row is the
  actual gap rather than a knock-on effect of the one before it. In-game,
  plotting a route to that system — or simply targeting it — fills in the
  missing `Distance`/`Star Type` live, straight from the game's own data,
  with no re-save needed. If a lookup pass finishes with any row still
  showing "Plot needed"/"Target needed", a dismissible banner appears
  above the table pointing you at them — dismissing it only hides the
  banner, not the per-row hints themselves, and a fresh Save re-evaluates
  from scratch.
- **Auto Pilot** (Fleet Carrier mode only; enabled once a Captain is
  assigned with a macro selected for them — see the Roles tab) drives
  the whole route to completion: it plays the Captain's macro to plot
  each jump, waits out each cooldown, and (with an Engineer assigned)
  plays the Engineer's macro to refuel in between — except for the
  route's own last row, which never gets a refuel (there's no next jump
  left to fuel for). It stops itself once every row is Complete —
  speaking a one-off "You have arrived at your destination..."
  announcement as it does — or immediately if you click it again, if the
  underlying Captain/Engineer/macro requirements stop being met
  mid-run, or if its own "panic mode" trips — see below.

A row's status cycles automatically as the journal reports real
progress. In **Fleet Carrier mode**: *(blank)* → `Plotted` → `Jumping` →
*(arrived, row completes, next row becomes current)* → `Cooldown` →
*(blank again)*. While Auto Pilot is engaged, a row also passes through
`Plotting` between blank and `Plotted` — the moment its macro starts
playing, until the game confirms the jump was actually requested. In
**Ship mode** (see the Track tab below): `Targeted` (a jump target is
locked, e.g. in the galaxy map) → `Jumping` (the jump is actually
underway) → *(arrived)* → `Cooldown` → *(blank again)* — there's no
`Plotted`/`Plotting`, since nothing is automated. Either way, a small
countdown (e.g. "`Plotted (0:11:32)`") and progress bar show how long is
left in the current phase, where that's knowable; `Plotting` (and
Ship mode's `Targeted`/`Jumping`) show no countdown, since there's no
way to know in advance how long either will take.

**Auto Pilot's "panic mode"** is deliberately paranoid: it stops Auto
Pilot immediately and shows a banner explaining why, rather than
guessing "probably fine", if a plot/refuel macro is interrupted for any
reason, if the Captain's macro finishes but no jump request actually
followed, or if the Engineer's macro finishes but a fresh check doesn't
find a genuine new fuel deposit for the carrier since the macro started
playing. (This is deliberately *not* just "is the fuel level higher than
before" — Elite's own journal never logs the fuel a jump itself
consumes, so the last known level often still reflects however things
stood *before* the jump this refuel is recovering from; comparing
against that stale number could wrongly panic on a refuel that actually
worked.) This is important because if a macro does not complete
normally it is likely the game is not in the right state for the start
of the next macro. We wouldn't want to continue plotting jumps if the
game had somehow clicked Auto Launch, would we 🙂.

### Roles tab

**Fleet Carrier mode only.** Lists every running `EliteDangerous64.exe`
process as a card (commander name, cargo, current location, fleet
carrier name/location/fuel, window position/monitor), refreshed on
launch and on demand via **Refresh**.

- Assign **Captain** to the instance whose journal ED:FC Auto Pilot should
  track — this resets the route and replays that commander's journal
  history to catch it up to the carrier's real current status in one
  step.
- Assign **Engineer** to the instance Auto Pilot should use to refuel
  the carrier (only offered to an instance with free cargo capacity).
- Pick which recorded macro (from the Controls tab) each role should
  play once Auto Pilot is engaged, under **Captain plots via** /
  **Engineer refuels via**.

Both roles are persisted (by commander FID, which survives a game
restart, not by process ID) and restored automatically next launch.

### Track tab

**Ship mode only** (switch to it via the **Fleet Carrier / Ship** toggle
beside the menu bar) — a leaner counterpart to the Roles tab for a solo
commander flying their own route by hand, e.g. one plotted with a
neutron-highway planner. Lists running instances the same way the Roles
tab does, but with just commander name, current location, and journal
filename (no cargo/carrier fields, since they're not relevant here).

- A single **Track** button per card assigns/unassigns that instance as
  the one ED:FC Auto Pilot follows — assigning resets the route and
  replays that instance's own ship journal to catch it up to your
  current status, the same reset-then-replay principle Captain
  assignment uses in Fleet Carrier mode.
- There's no macro automation at all in Ship mode — you plot and fly
  every jump yourself, and ED:FC Auto Pilot just watches and reflects
  progress. Switching to Ship mode hides Roles/Controls and the Route
  tab's Auto Pilot button entirely (and stops any Auto Pilot run
  already in progress, if you switch mid-route).

### Controls tab

**Fleet Carrier mode only.**

Where key bindings and macros live — the automation vocabulary Auto
Pilot (and manual playback) is built from.

- **Options**: **Auto Pilot delay (ms)** (default `5000`) — the extra
  settle time given to the game's UI around a jump's cooldown before
  Auto Pilot's next macro plays — and **Auto wait (ms)** (default
  `300`) — the pacing delay automatically inserted after every
  instruction a script executes. A second row of test-only fields lets
  you try a script from this tab without a live route or a running
  instance in the right cargo/fuel state.
- **Key Bindings**: the nine named actions (`UP`, `DOWN`, `LEFT`,
  `RIGHT`, `SELECT`, `PREV_PANEL`, `NEXT_PANEL`, `EXIT`, `RIGHT_PANEL`)
  a script refers to by name — click a binding to rebind it to any
  key/chord.
- **Running Instances**: its own independent scan of running game
  instances, used purely as the record/playback target.
- **New Script** (the file-plus icon, grouped with Record/Stop/Play) /
  **Record** / **Stop** / **Play**: New Script creates a new, empty
  macro and opens it straight in the editor, for writing (or pasting
  in) a script by hand instead of recording one — no running instance
  required (see [Sample scripts](macro-scripting.md#sample-scripts) for
  three ready to use this way). Record instead captures real
  keyboard/mouse input against a selected instance as a new named
  macro; Play replays any recorded macro against any selected instance.
  Click a macro's pencil icon to open the full-size editor (name +
  script text + a syntax-reference panel), where you can also **Step**
  through a script one instruction at a time.

---

## Spansh integration

**Integrations > Spansh…** opens a small dialog (available in both
tracking modes) to calculate a route between two systems and import it
straight into the Route tab, without leaving the app or hand-typing a
system list.

- Type into the **Source**/**Destination** fields for a live autocomplete
  search against Spansh, then click **Calculate**. The dialog polls the
  calculation every 5 seconds (an indeterminate progress bar, since
  there's no way to know in advance how long Spansh's own queue/
  calculation will take) and, once it completes, replaces the
  currently-saved route with the calculated jump list — the same
  "replaces the saved route outright" behaviour Import Current Route
  and Trim for FC use, applied here as soon as the calculation finishes
  rather than behind a confirmation dialog, since opening this dialog at
  all is already the deliberate action.
- Every jump Spansh returns seeds the same Distance/Star Type cache EDSM
  lookups use (coordinates and, once resolved by EDSM or targeted
  in-game, star type), so the freshly-imported route's columns populate
  instantly rather than waiting on a fresh EDSM round trip for systems
  Spansh already told us the exact position of.
- This dialog's own Calculate is a single, direct source → destination
  hop sequence — it doesn't know about tritium capacity or plan restock
  stops along the way. For that, a footnote links out to Spansh's own
  hosted **Fleet Carrier route planner**, which does account for it; use
  that instead and paste the result in (or **Import Current Route** if
  you plot it in-game) when tritium/restock planning actually matters.
- Used with the express permission of Spansh's author, Gareth Harper,
  credited directly in the dialog.

---

## Logs

**Help > Logs** opens a live, non-modal viewer for the same background
logging ED:FC Auto Pilot does throughout a session — HTTP requests to
EDSM, journal-driven row transitions, role/track assignment, Auto Pilot
triggers and panic-mode stops, macro recording/playback, update checks,
and persistence failures. It's the first place to look when something
doesn't seem right and you want to know what the app actually saw/did.

- It only ever shows entries logged *while it's open* — closing and
  reopening it starts blank again, even within the same session. The
  durable record is the log file on disk (below), not this window.
- A **Wrap text** checkbox switches long lines between scrolling
  horizontally and wrapping within the window; **Clear** empties the
  currently-visible text without touching the file.
- Everything shown here is also written to a date-stamped file under
  `%LocalAppData%\EDFCAutoPilot\Logs\` regardless of whether this window
  has ever been opened — see the data locations table below for
  housekeeping (retention/size) settings, and tailing it from another
  process (e.g. `Get-Content -Wait`) while the app runs.

---

## Data & configuration locations

ED:FC Auto Pilot stores its own state in `%LocalAppData%\EDFCAutoPilot\`:

| File | Contents |
|---|---|
| `routejumper.db` | SQLite key/value store — route text, window bounds/column widths, Captain/Engineer role + macro assignment, key bindings, recorded macros, Options, EDSM coordinate/star-type cache and unresolved-lookup cooldown |
| `routejumper.conf` | Plain-text, hand-editable `Key=Value` config: `JournalDirectory` (defaults to Frontier's standard `Saved Games\Frontier Developments\Elite Dangerous` folder), log housekeeping (`LogRetentionDays`/`LogMaxFileSizeMB`/`LogMaxTotalSizeMB` — default 7 days / 10MB / 100MB), `EdsmUnresolvedRetryHours` (default 12 — how long before retrying a system EDSM had no record of last time), `EdsmCoordinatesBatchSize` (default 100 — how many systems are bundled into a single EDSM coordinates/star-type request), and `SpanshAutocompleteDebounceMs` (default 250 — how long the Spansh dialog's Source/Destination fields wait after your last keystroke before searching). Edit this (while the app is closed or running — most of it is re-read on every Roles tab refresh or within ~30 minutes) to change any of these without reinstalling. |
| `Logs\routejumper-yyyy-MM-dd.log` | Date-stamped log files (see [Logs](#logs) above) — kept and size-capped automatically per the housekeeping settings above. |

Not persisted, by design: per-row route progress/status (re-derived from
the journal on every launch), the last-selected tab, and "Auto Copy To
Clipboard"/the Controls tab's test-value fields (always reset to their
defaults on a fresh launch).

---

## Troubleshooting

Start with **Help > Logs** (see [Logs](#logs) above) for most issues — it
shows what ED:FC Auto Pilot actually saw and did in real time, which is
usually the fastest way to tell "the journal doesn't have the event I
expected" apart from "the app saw it but didn't react the way I expected".

- **"No running Elite Dangerous instances found."** — ED:FC Auto Pilot only
  detects `EliteDangerous64.exe` processes; make sure the game is
  actually running (not just the launcher), then click **Refresh**.
- **Journal file shows "Not found" for a running instance** — the
  journal folder is wrong (see `routejumper.conf` above), or the
  process/journal timestamps are more than 5 minutes apart (a very slow
  launch). Confirm `JournalDirectory` points at your actual
  `Saved Games\Frontier Developments\Elite Dangerous` folder.
- **`Distance`/`Star Type` stay blank, or show "Plot needed"/"Target
  needed"** — EDSM simply has no record of that system (common for
  freshly-generated/unexplored ones); it isn't retried again for a while
  (`EdsmUnresolvedRetryHours`, default 12 — see the data locations table
  above) once EDSM has confirmed it has nothing, so a permanently-missing
  system isn't hit on every single Save.
- **A macro doesn't seem to do anything in-game** (Fleet Carrier mode) —
  ED:FC Auto Pilot sends real synthesized input (`SendInput`), which only
  reaches whichever window currently has focus; don't touch the
  mouse/keyboard yourself while a macro plays, and make sure the target
  game window isn't minimized.
- **Auto Pilot button stays disabled or disappears** — it's hidden
  entirely in Ship mode (no automation there at all); in Fleet Carrier
  mode it requires a Captain assigned with a macro selected for them
  (Roles tab), and, only if an Engineer is *also* assigned, a macro
  selected for them too.
- **Auto Pilot stopped itself mid-route with a banner message** — that's
  "panic mode" (see the Route tab section above) reacting to something
  unexpected about a plot/refuel, not a bug being silently swallowed; the
  banner explains what it saw, and Help > Logs has the fuller picture.
- **No spoken announcements** — check the mute button beside the menu
  bar isn't engaged, and that Volume is above 0 in File > Preferences.
- **Didn't want to install the latest version automatically** — uncheck
  "Automatically check for updates on startup" in File > Preferences;
  Help > Check for Updates still works on demand regardless.
- **Spansh dialog shows "Failed: ..."** — either Spansh itself
  rejected/couldn't complete the request (the status message names why),
  or it couldn't be reached at all; Help > Logs has the fuller picture.
  Calculate stays disabled until both Source and Destination are
  actually selected from the autocomplete list, not just typed.
