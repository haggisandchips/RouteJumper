---
title: User Guide
---

# ED:FC Auto Pilot User Guide

For installing the app, see the
[README](https://github.com/haggisandchips/RouteJumper#readme). For the
macro scripting language and sample scripts, see
[Macro Scripting](macro-scripting.md). For building from source, see
[Development](development.md).

---

## What it does

ED:FC Auto Pilot tracks a pasted route against a real Elite Dangerous
journal. Switch between two mutually exclusive modes with the **Fleet
Carrier / Ship** toggle beside the menu bar:

- **Fleet Carrier mode** (default) — assign a Captain's game instance on
  the **Roles** tab and ED:FC Auto Pilot tracks the carrier's progress,
  with an optional **Auto Pilot** that flies the whole route itself.
- **Ship mode** — assign your own instance on the **Track** tab and
  ED:FC Auto Pilot tracks your ship's progress passively — there's no
  automation, since you plot and fly every jump yourself.

Both modes:

- Show each row's leg distance and destination star type, looked up
  automatically from [EDSM](https://www.edsm.net).
- Copy the next system to the clipboard as you arrive.
- Detect every running Elite Dangerous instance and match it to its
  journal file.
- Calculate and import a route via the **Spansh** menu — see
  [Spansh integration](#spansh-integration).
- Log everything significant to a file and an optional live viewer
  (Help > Logs) — see [Logs](#logs).

Fleet Carrier mode also adds Auto Pilot: recorded macros that plot each
jump and (if an Engineer is assigned) refuel the carrier every cooldown,
with spoken lead-time announcements before each action.

## The tabs

### Route tab

Starts in **Edit** — paste a route, one system per line, and click
**Save** to build the table. The table shows each row's status, `#`,
`System`, `Distance`, `Star Type`, and status text. A route calculated
via **Spansh > Neutron Plotter…** additionally shows a `Jumps` column
right of `Distance` (ordinary hops since the previous waypoint); one via
**Spansh > Galaxy Plotter…** shows `Refuel`/`Inject`/`Neutron` columns
there instead. Below the table:

- **Import Current Route** — replaces the saved route with whatever's
  currently plotted in-game.
- **Trim for FC** (Fleet Carrier mode only) — collapses the route to
  hops of 500ly or less, the carrier's real jump range. Requires a
  Captain assigned with their carrier's current location known.
- **Auto Pilot** (Fleet Carrier mode only) — drives the whole route by
  playing the Captain's macro to plot each jump and the Engineer's to
  refuel each cooldown, stopping itself once the route completes, if you
  click it again, or if its "panic mode" trips (see below). A QR-code
  button appears beside it the moment it starts — see
  [Companion site](#companion-site).
- **Edit** — go back to the text box to change the route. For a Neutron/
  Galaxy Plotter route, this asks you to confirm first, since it converts
  the route back to a plain one and loses the extra columns above.

Click a row to copy its system name to the clipboard. Right-click a row
for **"Set next system"** — a manual override for correcting the current
row when automatic tracking gets it wrong. The **"Auto Copy To
Clipboard"** toggle above the table copies the next system automatically
as you arrive at each one.

`Distance`/`Star Type` fill in automatically after Save. A system EDSM
has no data for shows "Plot needed" (or "Target needed" if only the star
type is missing) — plotting a route to it, or just targeting it, in-game
fixes this live with no re-save needed.

A row's status cycles automatically: in **Fleet Carrier mode**, *(blank)*
→ `Plotted` → `Jumping` → *(arrived)* → `Cooldown` → *(blank)*; while
Auto Pilot is engaged, it also passes through `Plotting` while its macro
is playing. In **Ship mode**, `Targeted` → `Jumping` → *(arrived)* →
`Cooldown` → *(blank)*.

**Panic mode**: Auto Pilot stops immediately and shows a banner if a
plot/refuel macro doesn't complete normally (eg focus was lost), or
completes but the expected result (a jump request, or a fresh fuel
deposit) isn't actually seen.

### Roles tab

**Fleet Carrier mode only.** Lists every running Elite Dangerous
instance as a card (commander, cargo, location, carrier name/location/
fuel), refreshed on launch and via **Refresh**.

- Assign **Captain** — the instance whose journal ED:FC Auto Pilot
  tracks. Resets and re-derives the route from that commander's journal.
- Assign **Engineer** — the instance Auto Pilot uses to refuel the
  carrier (needs free cargo capacity).
- Pick which recorded macro each role uses under **Captain plots via** /
  **Engineer refuels via**.

Both roles persist across restarts and game relaunches.

### Controls tab

**Fleet Carrier mode only.** Where key bindings and macros live.

- **Options** — **Auto Pilot delay (ms)** (default `5000`) and
  **Auto wait (ms)** (default `300`), plus test-only fields for trying a
  script from this tab without a live route.
- **Key Bindings** — the nine named actions (`UP`, `DOWN`, `LEFT`,
  `RIGHT`, `SELECT`, `PREV_PANEL`, `NEXT_PANEL`, `EXIT`, `RIGHT_PANEL`) a
  script refers to by name. Click one to rebind it.
- **Running Instances** — used as the record/playback target.
- **New Script** / **Record** / **Stop** / **Play** — New Script opens a
  blank macro in the editor for writing one by hand (see
  [Sample scripts](macro-scripting.md#sample-scripts)); Record captures
  real input against a selected instance; Play replays a recorded macro
  against a selected instance. Click a macro's pencil icon to open the
  full editor, where you can also **Step** through it one instruction at
  a time.

### Track tab

**Ship mode only.** A leaner version of the Roles tab for a solo
commander flying their own route. Lists running instances (commander,
location, journal filename); a single **Track** button per card
assigns/unassigns it as the one instance ED:FC Auto Pilot follows.
There's no macro automation in this mode — you fly, the app just tracks.

---

## Spansh integration

**Spansh > Fleet Carrier…** / **Spansh > Neutron Plotter…** /
**Spansh > Galaxy Plotter…** open a dialog to calculate a route via
[Spansh](https://spansh.co.uk) and import it straight into the Route
tab, in either tracking mode.

- **Fleet Carrier** tab — a direct source → destination hop sequence.
  Doesn't account for tritium/restock planning; for that, use Spansh's
  own hosted Fleet Carrier Router (linked in the dialog).
- **Neutron Plotter** tab — a neutron-highway route between two systems,
  with editable Range/Efficiency and a Normal/Overcharge supercharge
  choice. Imports only the boost stops plus the destination, plus each
  one's own `Jumps` count (see [Route tab](#route-tab)).
- **Galaxy Plotter** tab — an *exact* route using your ship's own real
  jump range, fuel usage, and supercharge/injection options, rather than
  a flat range figure — the ship build is read from your journal
  automatically, so there's nothing to fill in beyond Source/Destination,
  Cargo, and a handful of route-option checkboxes. Imports only the
  route's own waypoints, plus each one's own Refuel/Inject/Neutron flags.

Type into Source/Destination for a live autocomplete search, then click
**Calculate**. On success, the route replaces your saved route
immediately.

---

## Companion site

While **Auto Pilot** is engaged (Fleet Carrier mode), a QR-code button
appears beside it on the Route tab. Scan it with your phone (or click
**Copy Link** in the popup) to open a small, mobile-friendly page showing
a live feed of that run: each jump plotted, each arrival, each refuel,
and any panic-mode stop — newest first, updating automatically with no
refresh needed.

It's read-only and requires no login — the link itself is the only
"key", so treat it like any other unlisted link (don't post it somewhere
public if you'd rather keep your route private). A fresh link/QR code is
generated every time you engage Auto Pilot; the previous run's link
still works afterward, just showing that it finished (or stopped early).

These pages aren't kept around for posterity — once a run finishes, its
page stays reachable for `CompanionSessionRetentionHours` (default 1
hour, just long enough to be confident you've actually seen the final
result), then gets cleaned up automatically the next time you launch
ED:FC Auto Pilot. That window only starts once the run actually ends, so
a run that's still going stays visible for however long it takes. A
fixed 72-hour maximum age applies unconditionally on top of that, mainly
as a backstop for a run that never got a clean ending (e.g. the app was
closed mid-route).

If the button never appears, or the page stays empty, that's a real
failure worth a look (a network issue, or the shared companion service
being briefly unreachable) — check Help > Logs (category `Companion`)
for what happened. It's entirely best-effort either way: a companion-site
failure never blocks or affects Auto Pilot itself, it just means no QR
code shows up (or the page doesn't update) that time.

Testing the companion site itself against a local build (`ng serve`)
instead of the real deployed one? Point `CompanionSiteBaseUrl` in
`routejumper.conf` at your local address (e.g. `http://localhost:4200`)
— see [Data & configuration locations](#data--configuration-locations).

---

## Logs

**Help > Logs** opens a live viewer for everything ED:FC Auto Pilot logs
during a session — journal-driven row transitions, EDSM requests, Auto
Pilot triggers and panic-mode stops, macro playback, and more.

It's live-only: it shows entries logged while it's open, starting blank
every time — so open it *before* reproducing an issue, not after.
Everything is also written to a date-stamped file under
`%LocalAppData%\EDFCAutoPilot\Logs\`; click **Open Logs Folder** in the
Logs window to jump straight there for anything that already happened.

---

## Data & configuration locations

ED:FC Auto Pilot stores its state in `%LocalAppData%\EDFCAutoPilot\`:

| File | Contents |
|---|---|
| `routejumper.db` | SQLite store — route text, window bounds, role/macro assignment, key bindings, recorded macros, options, EDSM lookup cache |
| `routejumper.conf` | Plain-text, hand-editable config — `JournalDirectory` (defaults to Frontier's standard `Saved Games\Frontier Developments\Elite Dangerous` folder), log housekeeping, EDSM retry/batch settings, Spansh autocomplete debounce, `CompanionSiteBaseUrl` (the [companion site](#companion-site)'s base link, e.g. for pointing it at a local `ng serve` instance while testing), `CompanionSessionRetentionHours` (how long a finished run's companion page stays reachable before it's cleaned up, default 1 - a fixed 72-hour maximum applies regardless) |
| `Logs\routejumper-yyyy-MM-dd.log` | Date-stamped log files, size/age-capped automatically |

Not persisted: per-row route progress (re-derived from the journal on
launch), the last-selected tab, "Auto Copy To Clipboard", and the
Controls tab's test-value fields.

---

## Troubleshooting

Start with **Help > Logs** if you can reproduce the issue — it shows what
the app actually saw and did, live. If the issue already happened, it
opens blank; click **Open Logs Folder** in that window (or browse to
`%LocalAppData%\EDFCAutoPilot\Logs\`) for the same detail on disk.

- **"No running Elite Dangerous instances found."** — make sure the game
  itself is running (not just the launcher), then click **Refresh**.
- **Journal shows "Not found" for a running instance** — check
  `JournalDirectory` in `routejumper.conf` points at your real
  `Saved Games\Frontier Developments\Elite Dangerous` folder.
- **`Distance`/`Star Type` stay blank or show "Plot needed"/"Target
  needed"** — EDSM has no record of that system (common for
  freshly-generated ones); it's retried periodically, not on every Save.
- **A macro doesn't seem to do anything in-game** — it sends real
  synthesized input, which only reaches the focused window; don't touch
  the mouse/keyboard while a macro plays, and make sure the game window
  isn't minimized.
- **Auto Pilot button stays disabled or disappears** — hidden entirely
  in Ship mode; in Fleet Carrier mode it needs a Captain assigned with a
  macro selected (and, if Engineer is also assigned, a macro for them
  too).
- **Auto Pilot stopped with a banner message** — that's panic mode
  reacting to something unexpected; the banner explains what, and Help >
  Logs has the fuller picture.
- **No spoken announcements** — check the mute button beside the menu
  bar, and Volume in File > Preferences.
- **Spansh dialog shows "Failed: ..."** — Spansh rejected the request or
  couldn't be reached; the status message explains why. Calculate stays
  disabled until Source/Destination are picked from the autocomplete,
  not just typed.
- **No QR-code button appears next to Auto Pilot** — the companion
  session failed to start (a network issue, or the shared service being
  briefly unreachable); Help > Logs (category `Companion`) has the
  detail, and Auto Pilot itself is unaffected either way. See
  [Companion site](#companion-site).
