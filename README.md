# ED:FC Auto Pilot

ED:FC Auto Pilot is a Windows desktop utility for **Elite Dangerous** fleet
carrier owners. Paste a route (one star system per line), assign a
Captain's running game instance to it, and ED:FC Auto Pilot watches that
commander's journal file in real time to track the carrier's progress —
plotting, jumping, arriving, cooling down — row by row, automatically. An
optional **Auto Pilot** mode goes a step further: it drives the whole
route to completion itself, playing back a recorded macro to plot each
jump (and, with an Engineer assigned, another to refuel the carrier)
without you touching the keyboard.

A **Ship mode** toggle switches to a leaner, purely passive alternative
for a solo commander flying their own hand-plotted route (e.g. from a
neutron-highway planner) — no automation, just the same row-by-row
progress tracking against your own ship's journal.

This is a fan-made tool and is not affiliated with or endorsed by
Frontier Developments plc. Elite Dangerous is a trademark of Frontier
Developments plc.

📖 **For everything beyond installation — both tracking modes, macro
scripting, sample scripts, data locations, development, and
troubleshooting — see the full
[ED:FC Auto Pilot documentation](https://haggisandchips.github.io/RouteJumper/).**

Questions, feedback, or support? Join the
[Discord server](https://discord.gg/GeF3AVjcUd).

---

## Technical Note

This entire application began as an academic exercise, but has since turned into a
practical application with a genuine place in the Elite:Dangerous community.

Every line of code has been written by AI — specifically Claude Code. I have not
written nor modified any part of the codebase myself. Code has been reviewed for
function, performance<sup>&nbsp;[1]</sup> and safety and thoroughly tested ... but
code style and quality belongs to Claude.

[1] A lot of effort has been put into optimising and validating this - Claude took a
bit of persuasion with some parts but we got there.

---

## Requirements

- **Windows 10/11**, x64.
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**
  (or the .NET 8 SDK, if building from source) — ED:FC Auto Pilot is a WPF
  app (`net8.0-windows`) and does not run on macOS/Linux.
- **Elite Dangerous**, with journal logging enabled (on by default) and
  a fleet carrier.

## Getting the app

Grab the latest installer (`EDFCAutoPilot-win-Setup.exe`) from the project's
**[GitHub Releases](https://github.com/haggisandchips/RouteJumper/releases)**
page and run it. From then on ED:FC Auto Pilot checks for and silently installs
newer releases itself on launch (via [Velopack](https://velopack.io/)) — no
need to revisit the Releases page for future updates.

The installer isn't code-signed yet, so Windows SmartScreen may warn on
first run — the project has applied to the
[SignPath Foundation](https://signpath.org/) for free code signing for
open source projects, and this note will be updated once that's in place.

## Building from source

1. Install the **.NET 8 SDK** and, if using Visual Studio, the **.NET
   desktop development** workload (Visual Studio 2022 or later).
2. Clone the repository and open `RouteJumper.sln`.
3. Press **F5** to run, or from a terminal:

   ```
   dotnet build RouteJumper.sln
   dotnet run --project RouteJumper/RouteJumper.csproj
   ```

See the [development docs](https://haggisandchips.github.io/RouteJumper/development.html)
for running the test suite and the project layout. Pull requests are
welcome.

---

## Quick Start

This is the Fleet Carrier + Auto Pilot flow (the default mode). For Ship
mode's simpler, passive-tracking-only setup, see the
[Track tab section](https://haggisandchips.github.io/RouteJumper/#track-tab)
in the docs.

1. **Route tab** — paste your route (one system per line) and click
   **Save**.
2. **Controls tab** — create the macros Auto Pilot will play: a
   jump-plotting script for the Captain, and (optionally) a refuelling
   script for the Engineer. Use the
   [sample scripts](https://haggisandchips.github.io/RouteJumper/macro-scripting.html#sample-scripts)
   in the docs as a starting point if you like. Before using any script,
   make sure **Key Bindings** here match Elite Dangerous's own keyboard
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

ED:FC Auto Pilot announces upcoming actions out loud ("Plotting in 30
seconds", "Refueling in 5 seconds") before it plots or refuels — once
you hear one, keep your hands off the mouse/keyboard, since real input
is about to be sent to the game.

For a full walkthrough of every tab, see the
[ED:FC Auto Pilot documentation](https://haggisandchips.github.io/RouteJumper/).

---

## License

[MIT](LICENSE) — free to use, modify, and redistribute.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
