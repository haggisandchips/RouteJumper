---
title: User Guide
---

{% include nav.html %}

# RouteJumper User Guide

This page covers what RouteJumper does, how each tab works, where its
data lives, and how to troubleshoot common issues. For installing the
app and the fastest path to a working Auto Pilot setup, see the
[README](https://github.com/haggisandchips/RouteJumper#readme). For the
macro scripting language and ready-to-use sample scripts, see
[Macro Scripting](macro-scripting.md). For building from source,
running tests, and cutting releases, see [Development](development.md).

## Contents

- [What it does](#what-it-does)
- [The three tabs](#the-three-tabs)
  - [Route tab](#route-tab)
  - [Roles tab](#roles-tab)
  - [Controls tab](#controls-tab)
- [Data & configuration locations](#data--configuration-locations)
- [Troubleshooting](#troubleshooting)

---

## What it does

- **Tracks a pasted route** against a Captain's real Elite Dangerous
  journal — no manual bookkeeping. Handles plots, cancellations, and
  cooldowns.
- **Drives the route automatically (Auto Pilot)** by playing a recorded
  macro against the Captain's game window to plot each jump, and (if an
  Engineer is assigned) another macro to refuel the carrier every
  cooldown.
- **Records and replays keyboard/mouse macros** against any running
  Elite Dangerous instance — a small scripting language (see
  [Macro Scripting](macro-scripting.md)) lets you hand-edit what gets
  recorded, or write scripts from scratch.
- **Detects every running Elite Dangerous instance** on the machine and
  matches each one to its journal file, showing commander, cargo,
  location, and fleet carrier status live.
- **Copies the next system to the clipboard automatically** as the
  carrier arrives, so it's ready to paste into the game.
- **Announces upcoming Auto Pilot actions out loud** ("Plotting in 30
  seconds", "Refueling in 5 seconds") via the OS speech engine, so you
  don't have to keep watching the app to know when it's about to send
  input to the game. Configurable (voice, volume) via File > Preferences,
  and mutable via the button beside the File menu.

## The three tabs

### Route tab

The main working screen. It starts in an **Edit** state — a plain
multi-line text box — where you paste a route, one system per line.
Clicking **Save** turns it into a read-only table with a status icon,
row number, system name, and status for each row, and switches to a
**Table** state with **Edit** and **Auto Pilot** buttons.

- Clicking a row copies its system name to the clipboard (with a
  confirmation sound and a small clipboard icon on the row).
- Right-clicking a row offers **"Set next system"** — a manual override
  that marks every row before it Complete, makes it the current row, and
  resets everything after it. This is your escape hatch whenever
  automatic detection gets it wrong or the carrier goes off-route.
- **"Auto Copy To Clipboard"** (a toggle above the table) copies the
  *next* system to the clipboard the instant the carrier is observed
  arriving — handy for quickly pasting into the game's galaxy map.
- **Auto Pilot** (enabled once a Captain is assigned with a macro
  selected for them — see the Roles tab) drives the whole route to
  completion: it plays the Captain's macro to plot each jump, waits out
  each cooldown, and (with an Engineer assigned) plays the Engineer's
  macro to refuel in between. It stops itself once every row is
  Complete, or immediately if you click it again, or if the underlying
  Captain/Engineer/macro requirements stop being met mid-run.

A row's status cycles automatically as the journal reports real
progress: *(blank)* → `Plotted` → `Jumping` → *(arrived, row completes,
next row becomes current)* → `Cooldown` → *(blank again)*. While Auto
Pilot is engaged, a row also passes through `Plotting` between blank and
`Plotted` — the moment its macro starts playing, until the game
confirms the jump was actually requested. A small countdown
("`Plotted (0:11:32)`") and progress bar show how long is left in the
current phase; `Plotting` shows an indeterminate spinner instead, since
there's no way to know in advance how long it'll take.

### Roles tab

Lists every running `EliteDangerous64.exe` process as a card (commander
name, cargo, current location, fleet carrier name/location/fuel, window
position/monitor), refreshed on launch and on demand via **Refresh**.

- Assign **Captain** to the instance whose journal RouteJumper should
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

### Controls tab

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

## Data & configuration locations

RouteJumper stores its own state in `%LocalAppData%\EDFCAutoPilot\`:

| File | Contents |
|---|---|
| `routejumper.db` | SQLite key/value store — route text, window bounds/column widths, Captain/Engineer role + macro assignment, key bindings, recorded macros, Options |
| `routejumper.conf` | Plain-text, hand-editable `Key=Value` config — currently just `JournalDirectory`, defaulting to Frontier's standard `Saved Games\Frontier Developments\Elite Dangerous` folder. Edit this (while the app is closed or running — it's re-read on every Roles tab refresh) to point at a non-default journal location. |

Not persisted, by design: per-row route progress/status (re-derived from
the journal on every launch), the last-selected tab, and "Auto Copy To
Clipboard"/the Controls tab's test-value fields (always reset to their
defaults on a fresh launch).

---

## Troubleshooting

- **"No running Elite Dangerous instances found."** — RouteJumper only
  detects `EliteDangerous64.exe` processes; make sure the game is
  actually running (not just the launcher), then click **Refresh**.
- **Journal file shows "Not found" for a running instance** — the
  journal folder is wrong (see `routejumper.conf` above), or the
  process/journal timestamps are more than 5 minutes apart (a very slow
  launch). Confirm `JournalDirectory` points at your actual
  `Saved Games\Frontier Developments\Elite Dangerous` folder.
- **A macro doesn't seem to do anything in-game** — RouteJumper sends
  real synthesized input (`SendInput`), which only reaches whichever
  window currently has focus; don't touch the mouse/keyboard yourself
  while a macro plays, and make sure the target game window isn't
  minimized.
- **Auto Pilot button stays disabled** — it requires a Captain assigned
  with a macro selected for them (Roles tab), and, only if an Engineer
  is *also* assigned, a macro selected for them too.
- **No spoken announcements** — check the mute button beside the File
  menu isn't engaged, and that Volume is above 0 in File > Preferences.
