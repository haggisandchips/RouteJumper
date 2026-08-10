# RouteJumper — ED:FC Auto Pilot

RouteJumper is a Windows desktop utility for **Elite Dangerous** fleet
carrier owners. Paste a route (one star system per line), assign a
Captain's running game instance to it, and RouteJumper watches that
commander's journal file in real time to track the carrier's progress —
plotting, jumping, arriving, cooling down — row by row, automatically. An
optional **Auto Pilot** mode goes a step further: it drives the whole
route to completion itself, playing back a recorded macro to plot each
jump (and, with an Engineer assigned, another to refuel the carrier)
without you touching the keyboard.

This is a fan-made tool and is not affiliated with or endorsed by
Frontier Developments plc. Elite Dangerous is a trademark of Frontier
Developments plc.

---

## Quick Start

1. **Route tab** — paste your route (one system per line) and click
   **Save**.
2. **Controls tab** — create the macros Auto Pilot will play: a
   jump-plotting script for the Captain, and (optionally) a refuelling
   script for the Engineer. Use the [sample scripts](#sample-scripts)
   below as a starting point if you like. Before using any script, make
   sure **Key Bindings** here match Elite Dangerous's own keyboard
   controls for UI navigation — the defaults (arrow keys, Space, Del,
   End, Backspace, `4`) match the game's own keyboard-and-mouse
   defaults, so this only needs attention if you've remapped them.
3. **Roles tab** — assign **Captain** to the fleet carrier owner's
   running Elite Dangerous instance, and, only if you want automatic
   refuelling, assign **Engineer** to any commander's instance that's
   aboard the carrier. Then pick each role's macro under **Captain
   plots via** / **Engineer refuels via** (an Engineer macro is only
   required once Engineer is assigned).
4. **Route tab** — click **Auto Pilot**.

RouteJumper announces upcoming actions out loud ("Plotting beginning in
30 seconds", "Refueling beginning in 5 seconds") before it plots or
refuels — once you hear one, keep your hands off the mouse/keyboard,
since real input is about to be sent to the game.

---

## Contents

- [Quick Start](#quick-start)
- [What it does](#what-it-does)
- [Requirements](#requirements)
- [Getting the app](#getting-the-app)
- [Building from source](#building-from-source)
- [Running the tests](#running-the-tests)
- [The three tabs](#the-three-tabs)
  - [Route tab](#route-tab)
  - [Roles tab](#roles-tab)
  - [Controls tab](#controls-tab)
- [Macro scripting language](#macro-scripting-language)
- [Sample scripts](#sample-scripts)
- [Data & configuration locations](#data--configuration-locations)
- [Project layout](#project-layout)
- [Packaging & distributing releases via GitHub](#packaging--distributing-releases-via-github)
- [Troubleshooting](#troubleshooting)
- [License](#license)
- [Changelog](CHANGELOG.md)

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
  Elite Dangerous instance — a small scripting language (see below) lets
  you hand-edit what gets recorded, or write scripts from scratch.
- **Detects every running Elite Dangerous instance** on the machine and
  matches each one to its journal file, showing commander, cargo,
  location, and fleet carrier status live.
- **Copies the next system to the clipboard automatically** as the
  carrier arrives, so it's ready to paste into the game.
- **Announces upcoming Auto Pilot actions out loud** ("Plotting beginning
  in 30 seconds", "Refueling beginning in 5 seconds") via the OS speech
  engine, so you don't have to keep watching the app to know when it's
  about to send input to the game. Configurable (voice, volume) via File
  > Preferences, and mutable via the button beside the File menu.

## Requirements

- **Windows 10/11**, x64.
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**
  (or the .NET 8 SDK, if building from source) — RouteJumper is a WPF
  app (`net8.0-windows`) and does not run on macOS/Linux.
- **Elite Dangerous**, with journal logging enabled (on by default) and
  a fleet carrier.

## Getting the app

Grab the latest installer (`RouteJumperSetup.exe`) from the project's
**[GitHub Releases](https://github.com/haggisandchips/RouteJumper/releases)**
page and run it. From then on RouteJumper checks for and silently installs
newer releases itself on launch (via [Velopack](https://velopack.io/)) — no
need to revisit the Releases page for future updates. See
[Packaging & distributing releases via GitHub](#packaging--distributing-releases-via-github)
below for how releases are produced.

## Building from source

1. Install the **.NET 8 SDK** and, if using Visual Studio, the **.NET
   desktop development** workload (Visual Studio 2022 or later).
2. Clone the repository and open `RouteJumper.sln`.
3. Press **F5** to run, or from a terminal:

   ```
   dotnet build RouteJumper.sln
   dotnet run --project RouteJumper/RouteJumper.csproj
   ```

## Running the tests

The solution includes a full unit test suite (`RouteJumper.Tests`,
xUnit) covering the route-progress engine, macro script parsing, journal
parsing, settings persistence, and the ViewModel layer:

```
dotnet test RouteJumper.Tests/RouteJumper.Tests.csproj
```

---

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
  required (see [Sample scripts](#sample-scripts) below for three ready
  to use this way). Record instead captures real keyboard/mouse input
  against a selected instance as a new named macro; Play replays any
  recorded macro against any selected instance. Click a macro's pencil
  icon to open the full-size editor (name + script text + a
  syntax-reference panel), where you can also **Step** through a script
  one instruction
  at a time.

---

## Macro scripting language

A macro is a small, line-oriented, hand-editable text script. Recording
produces this text automatically, but you're free to write or edit it
by hand — the parser is deliberately forgiving (a malformed line is
just skipped, not rejected).

| Syntax | Meaning |
|---|---|
| `UP`, `DOWN`, `LEFT`, `RIGHT`, `SELECT`, `PREV_PANEL`, `NEXT_PANEL`, `EXIT`, `RIGHT_PANEL` | Tap the key currently bound to that action (Controls tab) |
| `KEY <chord>` | Tap an arbitrary key not tied to a named action, e.g. `KEY Control+A` |
| `HOLD <token> <ms>` | Press-and-hold an action or `KEY ...` token for `<ms>` milliseconds |
| `CLICK <x>,<y>` | Left-click at a position relative to the game window's client area |
| `HOLD CLICK <x>,<y> <ms>` | Click-and-hold at a position for `<ms>` milliseconds |
| `{CENTRE}` | Usable in place of an `x` or `y` coordinate — resolves to that axis' midpoint at play time |
| `WAIT <ms>` | Pause before the next step |
| `PASTE <text>` | Sets the clipboard to `<text>` and sends Ctrl+V |
| `{NEXT_SYSTEM}` | Placeholder inside a `PASTE`, resolved at play time to the Route tab's current next system |
| `REPEAT <n>` … `END` | Repeats its body `n` times (nestable) |
| `{TRITIUM_LOOPS}` | Placeholder usable anywhere a number is expected (most often `REPEAT {TRITIUM_LOOPS}`) — resolved to how many full ship-loads of tritium are still needed to top off the carrier's fuel depot and the ship's own hold |
| `MACRO <name>` … `END` | Defines a named, reusable sub-routine (top level only) |
| `CALL <name>` | Invokes a macro defined with `MACRO` |
| `# comment` | Ignored, like a blank line |
| `A; B; C` | Multiple steps on one line, separated by `;`, purely for readability |

Every leaf instruction is automatically followed by the configured
**Auto wait** delay (skipped only if the next instruction is itself a
`WAIT`), so a script doesn't need an explicit `WAIT` after every single
step.

---

## Sample scripts

Three ready-to-use scripts are included below to get you started —
covering the Captain's jump-plotting routine and both ways an Engineer
can refuel the carrier (buying Tritium from a station market, or
transferring it from the carrier's own hold). To use one of these:

1. On the **Controls** tab, click the **New Script** icon (the
   file-plus icon next to Record/Stop/Play, in the Running Instances
   section) — this creates a new, empty macro and opens it straight in
   the editor.
2. Give it a clear **name** (e.g. "Plot Next System"), then paste one of
   the scripts below into the script box.
3. On the **Roles** tab, pick it under **Captain plots via** or
   **Engineer refuels via** as appropriate.

All three assume the in-game **Right Panel** key binding is set to open
the ship's right-hand panel from the **Home** tab (per their own
`NOTE` comments) — make sure `RIGHT_PANEL` (Controls tab) matches your
in-game binding before using them.

### 1. Plot Next System (Captain)

Plots a jump to the route's next system via the Galaxy Map. Only
appropriate for the **Captain** role.

```
#######################################################################
#                                                                     #
# Plots the next system.                                              #
# This script is only appropriate for the Captain role.               #
#                                                                     #
# NOTE: This script requires Right Panel to be set on the Home tab.   #
#                                                                     #
#######################################################################


# Open Right Panel
RIGHT_PANEL; WAIT 3000

# Ensure top left
REPEAT 3; UP; LEFT; END

# Open Carrier Management
DOWN; RIGHT; SELECT; WAIT 5000

# Open Galaxy Map
DOWN; SELECT; WAIT 500; SELECT; WAIT 5000

# Select System
UP; SELECT; PASTE {NEXT_SYSTEM}; WAIT 500; DOWN; SELECT; WAIT 5000

# Plot Jump
HOLD CLICK {CENTRE},{CENTRE} 1000; WAIT 5000

# Exit
REPEAT 12; DOWN; END; SELECT
```

### 2. Refuel Via Market (Engineer)

Refuels the carrier by buying Tritium from a docked station's Commodity
Market, then donating it (and anything already in the ship's hold) to
the carrier's Tritium Depot — looped `{TRITIUM_LOOPS}` times. Assumes
Tritium is the only commodity for sale at that market.

```
#######################################################################
#                                                                     #
# Refuels the carrier by purchasing Tritium from the Market.          #
#                                                                     #
# It assumes that Tritium is the only commodity for sale. If that is  #
# not the case then adjust the script to select Tritium correctly.    #
#                                                                     #
#######################################################################


MACRO DonateTritium
    # Enter Titium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Click Donate Tritium
    SELECT

    # Confirm deposit (does nothing if no tritium on ship)
    UP; SELECT

    # Exit Tritium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Return Top Left
    UP; UP
END

MACRO BuyTritium
    # Enter Commodity Market
    RIGHT; RIGHT; SELECT; WAIT 1000

    # Buy Tritium
    RIGHT; SELECT; WAIT 500; UP; UP; HOLD RIGHT 5000; WAIT 500; DOWN; SELECT; WAIT 1000

    # Exit Commodity Market
    LEFT; REPEAT 4; DOWN; END; SELECT; WAIT 1000
END

#
# Start Routine
#

# Enter Carrier Services
SELECT; WAIT 5000

REPEAT {TRITIUM_LOOPS}
    # Donate anything the ship is carrying
    CALL DonateTritium

    # Buy more ... this is the last step so jump is plotted with this tritium effectively
    # missing and therefore not counted towards the tritium required by the jump.
    CALL BuyTritium
END

# Exit
EXIT
```

### 3. Refuel Via Transfer (Engineer, carrier owner only)

Refuels the carrier by transferring Tritium straight out of the
carrier's own cargo hold, rather than buying it — only appropriate when
the Engineer's commander is the carrier's owner. Reliable only when
Tritium is the *only* commodity in both the ship's and the carrier's
holds (otherwise its position in the transfer screen isn't fixed).

```
#######################################################################
#                                                                     #
# Refuels the carrier by transfering Tritium from the carrier's hold. #
# This script is only appropriate when Engineer is the carrier owner. #
#                                                                     #
# It is only reliable when Tritium is the only commodity in both the  #
# ship's hold and the carrier's hold. If not the location of Tritium  #
# in the transfer screen is indeterminate and changes depending on    #
# the ship's hold contents.                                           #
#                                                                     #
# NOTE: This script requires Right Panel to be set on the Home tab.   #
#                                                                     #
#######################################################################

MACRO DonateTritium
    # Enter Titium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Click Donate Tritium
    SELECT

    # Confirm deposit (does nothing if no tritium on ship)
    UP; SELECT

    # Exit Tritium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Return Top Left
    UP; UP
END

MACRO TransferTritium
    # Open Right Panel
    RIGHT_PANEL; WAIT 3000

    # Access Transfer (allowing for Inventory already containing something)
    RIGHT; UP; UP; RIGHT; SELECT

    # Select Tritium (REQUIRES TRITIUM TO BE THE ONLY ITEM IN THE HOLD)
    # NOTE: If Tritium is NOT the only item it is indeterminate whether it will be at the top or elsewhere
    UP

    # Transfer Max Tritium
    HOLD LEFT 5000; SELECT; SELECT

    # Close Right Panel
    EXIT; EXIT
END

#
# Start Routine
#

# Enter Carrier Services
SELECT; WAIT 5000

# Open Right Panel and navigate to Inventory, then close panel
RIGHT_PANEL; WAIT 3000; REPEAT 4; NEXT_PANEL; END; EXIT

REPEAT {TRITIUM_LOOPS}
    # Donate anything the ship is carrying
    CALL DonateTritium

    # Transfer ... this is the last step so jump is plotted with this tritium effectively
    # missing and therefore not counted towards the tritium required by the jump.
    CALL TransferTritium
END

# Open Right Panel and navigate to Home, then close panel
RIGHT_PANEL; WAIT 2000; REPEAT 4; PREV_PANEL; END; EXIT

# Exit
EXIT
```

---

## Data & configuration locations

RouteJumper stores its own state in `%LocalAppData%\RouteJumper\`:

| File | Contents |
|---|---|
| `routejumper.db` | SQLite key/value store — route text, window bounds/column widths, Captain/Engineer role + macro assignment, key bindings, recorded macros, Options |
| `routejumper.conf` | Plain-text, hand-editable `Key=Value` config — currently just `JournalDirectory`, defaulting to Frontier's standard `Saved Games\Frontier Developments\Elite Dangerous` folder. Edit this (while the app is closed or running — it's re-read on every Roles tab refresh) to point at a non-default journal location. |

Not persisted, by design: per-row route progress/status (re-derived from
the journal on every launch), the last-selected tab, and "Auto Copy To
Clipboard"/the Controls tab's test-value fields (always reset to their
defaults on a fresh launch).

## Project layout

```
RouteJumper.sln
RouteJumper/                    The application
  App.xaml(.cs)
  MainWindow.xaml(.cs)          Window shell, File menu, startup placement, clipboard-change hook
  Common/                       ICommand implementations, ObservableObject base, clipboard helper
  Models/                       Small data types (ControlAction, RowIcon, RecordedMacro, ...)
  ViewModels/                   One ViewModel per tab, plus per-row/per-item ViewModels
  Sequencing/                   The route-progress engine
  Services/                     Journal parsing/watching, macro parser/player, settings/config
                                 stores, process/window scanning, key-binding formatting,
                                 speech synthesis
  Converters/                   WPF IValueConverters
  Views/                        XAML for each tab, plus the Preferences dialog
  Resources/                    App icon
RouteJumper.Tests/               xUnit unit test suite, mirroring the layout above
```

---

## Packaging & distributing releases via GitHub

RouteJumper self-updates via [Velopack](https://velopack.io/) (see
`RouteJumper/Services/UpdateService.cs`): every launch checks GitHub
Releases for a newer version and, if found, downloads and applies it on
the *next* exit rather than interrupting the current session. This means
a release isn't just a built exe — it has to be packaged with Velopack's
own `vpk` tool so the installed copy and the update feed are both in the
format `UpdateManager` expects.

### Cutting a release (automated)

[`.github/workflows/release.yml`](.github/workflows/release.yml) does
this end to end: pushing a tag matching `v*` builds RouteJumper, packs
it with `vpk`, and publishes it as a GitHub Release, which
`UpdateService` then picks up automatically on every installed copy's
next launch.

```
git tag v1.0.0
git push origin v1.0.0
```

That's it — no local `vpk` install, no personal access token, nothing
else to configure. The workflow uses GitHub Actions' own automatic,
repo-scoped `GITHUB_TOKEN` (granted `contents: write` at the top of the
workflow file) to both fetch the previous release (so Velopack can
generate a small delta package against it) and publish the new one.

Move the relevant entries from [`CHANGELOG.md`](CHANGELOG.md)'s
`[Unreleased]` section into a new dated, versioned section (e.g.
`[1.0.0] - 2026-08-10`) as part of the same commit that gets tagged.

### Cutting a release (manual)

The steps below are what the workflow above automates — useful for
testing a package locally, or if you ever need to cut a release without
CI.

#### 1. Install the `vpk` CLI (one-time)

```
dotnet tool install -g vpk
```

#### 2. Decide on a version and tag

Use [Semantic Versioning](https://semver.org/) tags, e.g. `v1.0.0` (pass
the bare `1.0.0`, no `v` prefix, as the pack version in step 4):

```
git tag v1.0.0
git push origin v1.0.0
```

#### 3. Publish a self-contained build

WPF apps don't support trimming reliably, so publish **self-contained**
(bundles the .NET runtime — no separate runtime install needed):

```
dotnet publish RouteJumper/RouteJumper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o publish/win-x64
```

Unlike a plain zipped release, don't add `-p:PublishSingleFile=true`
here — Velopack needs the individual output files (not one bundled exe)
so it can diff them against the previous release and generate a small
delta package.

#### 4. Pack it with Velopack

```
vpk pack `
  -u RouteJumper `
  -v 1.0.0 `
  -p publish/win-x64 `
  -e RouteJumper.exe `
  -i RouteJumper/Resources/AppIcon.ico
```

Produces `RouteJumperSetup.exe` (the installer), a full `.nupkg`, and a
`RELEASES` manifest under `Releases/` — plus a delta package against the
previous version, if `Releases/` still has that previous version's
output sitting in it from the last time this was run (keep that folder
around between releases so delta generation has something to diff
against; a missing previous version just means a full-size update
instead of a small delta one, not a failure). The workflow gets this by
running `vpk download github` into `Releases/` first; doing this by hand
means running that yourself, or just accepting a full-size package.

#### 5. Upload the release to GitHub

```
vpk upload github `
  --repoUrl https://github.com/haggisandchips/RouteJumper `
  --token $env:GITHUB_TOKEN `
  --tag v1.0.0 `
  --publish
```

Unlike the automated workflow (which uses GitHub Actions' own built-in
token), a manual upload needs a real [personal access
token](https://github.com/settings/personal-access-tokens/new) with
**Contents: Read and write** on this repo (`$env:GITHUB_TOKEN = gh auth
token` reuses the `gh` CLI's own, if already logged in). Omit
`--publish` to upload as a draft release for a final check before it
goes live. First-time installers still need to download
`RouteJumperSetup.exe` from the Releases page directly — `UpdateService`
only ever updates an *already-installed* copy, and no-ops entirely for
an unpackaged `dotnet run` build.

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

---

## License

[MIT](LICENSE) — free to use, modify, and redistribute.
