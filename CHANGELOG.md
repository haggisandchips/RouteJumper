# Changelog

All notable changes to ED:FC Auto Pilot are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Ship mode**: a toggle beside the menu bar switching between Fleet
  Carrier mode (the default) and a leaner, purely passive Ship mode for
  a solo commander flying their own hand-plotted route (e.g. from a
  neutron-highway planner) — no macro automation at all, just the same
  row-by-row progress tracking, against your own ship's journal instead
  of a Captain's. Adds a new **Track** tab (a single-instance
  counterpart to Roles) shown only in Ship mode; Roles/Controls and the
  Route tab's Auto Pilot button are hidden while it's active.
- Route tab **`Distance`** and **`Star Type`** columns, populated in the
  background against [EDSM](https://www.edsm.net) — leg distance between
  each row (and from your current position to row 1), and each row's own
  system's main star type. A system EDSM has no coordinates for shows
  "Plot needed" instead of a blank cell (or "Target needed" if only the
  star type is missing), so the actual gap is obvious rather than a
  knock-on effect of the row before it.
- Auto Pilot **panic mode**: stops itself immediately and shows a
  closeable banner if a plot/refuel macro is interrupted for any reason,
  if the Captain's macro finishes without a real jump request following,
  or if the Engineer's macro finishes without the carrier's fuel level
  actually having gone up on a fresh check — rather than risking the
  carrier jumping further and further away on the strength of a macro
  that's silently stopped working. A `{TRITIUM_LOOPS}` resolution is now
  also capped so the Engineer's refuel script can never take long enough
  to still be playing when the Captain's own next plot comes due.
- Background app logging: a rolling, date-stamped log file
  (`%LocalAppData%\EDFCAutoPilot\Logs\`) plus a live, non-modal **Help >
  Logs** viewer (with a "Wrap text" option), covering HTTP requests to
  EDSM, journal-driven row transitions, role/track assignment, Auto
  Pilot triggers and panic-mode stops, macro recording/playback, update
  checks, and persistence failures. Housekeeping (retention days, and
  per-file/total size caps) is configurable in `routejumper.conf`.
- A **Help** menu, holding Logs, Check for Updates, and About (the
  latter two moved out of File).
- A Preferences checkbox to turn off the silent startup update check
  entirely — Help > Check for Updates always still works on demand
  regardless.
- Route tab **Import Current Route**: reads whatever route is currently
  plotted in-game (`NavRoute.json`) and replaces the saved route with it
  in one click — available in both tracking modes, with no Captain/
  tracked instance needing to be assigned first. **Trim for FC** (Fleet
  Carrier mode only) collapses a saved route down to a series of hops no
  longer than 500ly each, dropping whichever intermediate systems aren't
  actually needed to stay within a fleet carrier's real jump range — a
  planning aid for a route pasted or imported with many closely-spaced
  waypoints (e.g. a neutron-highway plotter's own output). Both confirm
  via a Yes/No dialog first (each replaces/rewrites the saved route
  outright) and then apply immediately, left-to-right in the order
  you'd actually use them: Import Current Route, Trim for FC, Auto Pilot.
- **Spansh integration**: a new **Integrations > Spansh…** dialog to
  calculate a route between two systems (autocomplete Source/Destination
  fields) via Spansh's own route-plotting API and import it straight
  into the Route tab, replacing the currently-saved route the same way
  Import Current Route does. Polls the calculation every 5 seconds with
  an indeterminate progress indicator while it's running. A footnote
  links out to Spansh's own hosted Fleet Carrier route planner (which
  accounts for tritium capacity and schedules restock stops along the
  way — this dialog's own Calculate is a single direct hop sequence,
  not tritium-aware) for that heavier planning job instead. Every jump
  Spansh returns seeds the same Distance/Star Type cache EDSM lookups
  use, so the imported route's columns populate instantly rather than
  needing a fresh EDSM round trip. Used with the express permission of
  Spansh's author, Gareth Harper, credited in the dialog itself.

### Changed

- EDSM coordinate and star-type lookups are now resolved together in a
  single request per batch of systems, retiring the separate, more
  heavily-throttled per-system endpoint star type used to need. A
  system EDSM confirms has no record of isn't queried again for a
  configurable cooldown (`EdsmUnresolvedRetryHours` in
  `routejumper.conf`, default 12 hours) — in memory for the rest of the
  session, and on disk so the cooldown survives a restart too.
- EDSM cache writes to disk no longer block the in-memory cache
  update/UI notification that drives the Route tab's live-refresh — a
  burst of seeds (e.g. reading a freshly-plotted `NavRoute.json`)
  updates the table essentially instantly, with the (comparatively slow)
  database write trailing behind on its own background queue.
- Coordinate/star-type/system-address caching moved off prefixed
  `Settings` rows onto its own `ResolvedLookups` table, mirroring
  `UnresolvedLookups`' own existing rationale for a separate schema.
- The EDSM request batch size is now configurable
  (`EdsmCoordinatesBatchSize` in `routejumper.conf`), defaulting to 100
  systems per request instead of the previous hardcoded 10.
- The coordinate/star-type cache no longer persists every system ever
  resolved, indefinitely — a value EDSM itself resolves is now kept only
  for the running session (asking EDSM again next launch is cheap, now
  that it's a single batched request), and a journal/Spansh seed is
  written to disk only when it fills a system EDSM has already confirmed
  it has no record of. System address (id64) is no longer persisted at
  all — nothing reads it yet. Keeps `routejumper.db` bounded instead of
  growing with every system a commander has ever visited.

### Fixed

- `Star Type` no longer shows the bare journal code for the brown dwarf
  classes (L, T, Y) — now formatted as "L (Brown dwarf)", "T (Brown
  dwarf)", and "Y (Brown dwarf)", matching the main-sequence classes'
  own display style.
- Ship mode: a genuinely live-tailed `FSDTarget` naming a system not in
  the pasted route could leave a stale `Targeted` row icon stuck showing
  instead of clearing it.
- Route tab: the `Status`/`#` column headers could render sitting above
  the `System` column instead of aligned with it.
- `Star Type` values no longer repeat the redundant trailing "Star" word
  EDSM's own data already ends in (e.g. "K (Yellow-Orange)" instead of
  "K (Yellow-Orange) Star").
- A journal-targeted system (e.g. a T Tauri star, raw code `TTS`) and
  the same system resolved via EDSM ("T Tauri") could show different
  `Star Type` text depending on which source resolved it first — and
  since the cache was permanent, the mismatch never healed itself once
  cached. `Star Type` is now always cached as the canonical journal
  code and formatted fresh on read, so both sources converge on
  identical text regardless of resolution order.
- `Distance` and `Star Type` could each end up firing their own
  separate EDSM request per system instead of genuinely batching
  together — most visibly on a route whose coordinates were already
  cached (e.g. from an earlier session) but whose star types weren't,
  which fell back to one EDSM request per row. Star Type now always
  resolves via a single batched request covering every system in the
  route.

## [1.3.0] - 2026-08-13

### Added

- File > **About** dialog showing the app icon, name, installed version
  (or "Development build" for an unpackaged run), a short description,
  the fan-made/not-affiliated disclaimer, a link to the GitHub repo,
  and copyright/license.
- File > **Check for Updates**, an on-demand version of the update
  check already run silently on every launch, reporting whether you're
  up to date, a new version was downloaded, updates aren't available
  for this build, or the check failed.

## [1.2.2] - 2026-08-13

### Added

- Published a documentation site (GitHub Pages) covering the three
  tabs, the macro scripting language, sample scripts, data/config
  locations, troubleshooting, and the development/release process -
  moved out of the README, which now focuses on installation and the
  Quick Start.

### Changed

- The Controls tab's "sample scripts" link (shown while no macro has
  been recorded yet) now opens the docs site instead of the README's
  own section.

## [1.2.1] - 2026-08-10

### Fixed

- README referred to the packaged installer as `EDFCAutoPilotSetup.exe`;
  Velopack actually names it `EDFCAutoPilot-win-Setup.exe` (confirmed
  against the real v1.2.0 release assets).
- The Route tab's Status column (and so its progress bar) still didn't
  reach the far right of the table, and resizing the window while on
  that tab could produce a horizontal scrollbar. Two compounding causes:
  the `System` column's `Auto` width never shrinks below its content's
  natural width, so a long real system name could force the whole table
  wider than the window; and the Status column's width was being
  persisted and restored as a fixed pixel value like any other column,
  permanently overriding its intended fill-the-remainder sizing the
  first time it was ever resized (or the window was). `System` is now a
  weighted Star column instead of `Auto` (truncating with an ellipsis,
  full name on hover, if the window gets too narrow), and `Status` is no
  longer persisted at all - it always fills whatever width the other
  three columns leave unused.

## [1.2.0] - 2026-08-10

### Added

- Spoken lead-time announcements before Auto Pilot plots a jump or
  refuels ("Plotting"/"Refueling in 30 seconds"/"...in 5 seconds"), via
  the OS speech engine.
- A File menu (Preferences…, Exit) and an always-visible mute button on
  the main window.
- A Preferences dialog to choose the announcement voice (from every voice
  installed on the machine), test it, and set its volume - all persisted.
- A new "Plotting" row status, shown from the instant Auto Pilot starts
  playing the Captain's macro until the real CarrierJumpRequest arrives -
  an indeterminate progress cue (duration unknown) rather than a
  countdown, since previously the row just sat blank while the macro ran.

### Changed

- The Engineer's refuel still fires at the same real-world instant as
  before (the configured Auto Pilot delay after the next row's Cooldown
  starts), but that instant is now computed as soon as the current row
  becomes Jumping - using its DepartureTime-derived estimated-arrival
  PhaseEndUtc - instead of only once Cooldown is actually observed
  starting live. Gives the "Refueling in 30 seconds"/"...in 5 seconds"
  announcements several real minutes of lead time to work with instead
  of only the short Auto Pilot delay itself.
- The app's own display name (Explorer's file Properties, Task Manager,
  and Velopack's Start Menu shortcut) is now "ED:FC Auto Pilot",
  matching the main window's own title/taskbar text.
- Renamed the exe itself (`RouteJumper.exe` → `EDFCAutoPilot.exe`), the
  `%LocalAppData%` settings folder (`RouteJumper` → `EDFCAutoPilot`),
  and the Velopack app ID used for packaging/updates to match - a clean
  break rather than an in-place migration, so an existing install needs
  to be uninstalled and the new one installed fresh rather than picked
  up as an automatic update.
- Shortened the spoken announcements by dropping "beginning" - e.g.
  "Plotting in 30 seconds" instead of "Plotting beginning in 30
  seconds".

### Fixed

- Record/Play could fail to bring the target Elite Dangerous window to
  the foreground when some other window currently had focus, due to
  Windows' focus-stealing prevention silently ignoring the request.
- The Engineer's "Refueling in 5 seconds" announcement almost never
  actually spoke under the default Auto Pilot delay - its due time
  landed right at the instant Cooldown starts, always losing a race
  against real elapsed time.
- The Route tab's Status column progress bar (determinate and the new
  indeterminate "Plotting" one alike) only ever stretched as wide as its
  own status text, not the actual column width - the DataGridCell's
  default content alignment left nothing for the bar's own Stretch to
  fill into.

## [1.1.0] - 2026-08-10

### Added

- Controls tab: while no macro has been recorded (or written via New
  Script) yet, the Recorded Macros list shows a hint linking to the
  README's sample scripts as a starting point.

## [1.0.0] - 2026-08-10

### Added

- **Route tab**: paste a route (one system per line), save it to a
  table, and track its progress automatically against a Captain's real
  Elite Dangerous journal — plots, jumps, arrivals, and cooldowns are
  all reflected live, with a per-row countdown and progress bar. A
  manual "Set next system" override corrects automatic detection when
  needed. "Auto Copy To Clipboard" copies the next system the instant
  the carrier arrives.
- **Roles tab**: detects every running `EliteDangerous64.exe` instance,
  matches each to its journal file, and shows commander, cargo, current
  location, and fleet carrier status (including fuel level) live.
  Captain and Engineer roles can be assigned to any running instance,
  each with its own selected macro for Auto Pilot to play.
- **Controls tab**: rebindable key bindings, a small human-readable
  macro scripting language (taps, holds, clicks, waits, repeats,
  sub-macros, and clipboard-paste/tritium-loop placeholders), and
  recording, editing, and stepwise/full playback of macros against any
  running instance.
- **Auto Pilot**: drives a saved route to completion by playing the
  Captain's macro to plot each jump and, if an Engineer is assigned,
  the Engineer's macro to refuel every cooldown — stopping itself once
  the route completes or its requirements stop being met.
- Persistence of route text, window bounds, column widths, Captain/
  Engineer role assignment (by commander FID), role macro selection,
  and Controls tab options/key bindings/macros, all via a local SQLite
  database, plus a hand-editable `routejumper.conf` for the journal
  folder location.
- Silent self-updating via [Velopack](https://velopack.io/): on launch,
  a background check downloads any newer GitHub release and applies it
  on the app's next normal exit, never interrupting an active route or
  Auto Pilot run. No-op for an unpackaged/dev build.
- Material Design styling throughout.
- `RouteJumper.Tests`, a unit test suite covering the route-progress
  engine, macro script parsing, journal parsing, settings persistence,
  and the ViewModel layer.
- MIT `LICENSE` and a full `README.md` covering setup, usage, the macro
  scripting language, sample scripts, and the release procedure for
  distributing builds via GitHub.

[1.2.1]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.2.1
[1.2.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.2.0
[1.1.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.1.0
[1.0.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.0.0
