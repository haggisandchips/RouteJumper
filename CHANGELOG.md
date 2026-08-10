# Changelog

All notable changes to RouteJumper are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
  and Velopack's Start Menu shortcut) is now "ED:FC Auto Pilot",
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

[1.2.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.2.0
[1.1.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.1.0
[1.0.0]: https://github.com/haggisandchips/RouteJumper/releases/tag/v1.0.0
