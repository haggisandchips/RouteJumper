# RouteJumper — Application Specification

**Version:** 2.0
**Platform:** Windows desktop, WPF, .NET 8, MVVM

---

## 1. Purpose

RouteJumper is a desktop utility for Elite Dangerous fleet carrier owners. A user
pastes a list of star systems (one per line) to form a route. Once a Captain's
game instance is assigned on the Roles tab, RouteJumper watches that commander's
journal file and automatically tracks the carrier's progress along the route,
updating each row's status as the carrier plots, jumps, arrives, and cools down.

---

## 2. Scope

| In scope | Out of scope |
|---|---|
| Route tab: paste a route, save it to a table, track progress against a real journal | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Roles tab: detect running Elite Dangerous instances; assign Captain/Engineer roles | Configurable journal-match tolerance or cooldown timing |
| Event-driven row-progress engine, driven by a Captain's journal | Persisting per-row progress (icon/status), or the last-selected tab |
| Manual "Set next system" override for correcting automatic detection | |
| Selecting a macro for each of Captain/Engineer (§5.5), gating Auto Pilot on that selection, and Auto Pilot playing the Captain's macro to plot each jump and (if Engineer is assigned) the Engineer's to refuel each Cooldown (§4.7) | Auto Pilot retrying a jump after `CarrierJumpCancelled` (§4.7) |
| Persistence of route text, window bounds, and Captain/Engineer role assignment | |
| "Auto Copy To Clipboard" for the next system, plus a clipboard-source indicator | |
| Controls tab: key bindings, running-instance scan, and recording/playback of macros (§6) | |
| Spoken lead-time announcements before Auto Pilot plots/refuels (§4.8), a File menu (§3.4) with Exit and a Preferences dialog (voice/volume/test), and an always-visible mute button | |
| Material Design styling | |

---

## 3. Window Structure

### 3.1 Main Window
- Single top-level window with a `TabControl` holding exactly three tabs, in
  order: **Route**, **Roles**, **Controls**. Route is selected by default.
- Tab headings are placed on the **left edge** of the window
  (`TabStripPlacement="Left"`).
- Window is resizable; content reflows (no fixed-pixel layouts).
- Window title: **"ED:FC Auto Pilot"**.
- Window/taskbar icon: `Resources/AppIcon.ico`, a `RocketLaunch` glyph rendered
  at 16/32/48/256px, matching the row icons' style and color.

### 3.2 Startup window placement
- On launch, the window is positioned against the **right edge** of the
  physically rightmost connected monitor (10px margin), vertically centered on
  that monitor — unless a previous session's bounds were persisted and are
  still reachable (see §7), in which case those are restored instead.
- The rightmost monitor is determined dynamically each launch (the monitor
  with the greatest `Left` screen coordinate), not a hardcoded device name.
- Positioning happens at `SourceInitialized` (HWND exists, window not yet
  painted), so there is no visible jump on launch. Monitor bounds are
  converted to WPF device-independent units via the window's own
  composition-target transform, so this is correct under per-monitor DPI
  scaling.

### 3.3 Controls tab
See §6.

### 3.4 Menu bar and mute button
- A `Menu` sits above the tab area, visible regardless of which tab is
  active, with a single top-level **File** entry:
  - **Preferences…** opens the Preferences dialog (§3.5) as a modal window.
  - **Exit** closes the main window (which persists its bounds as normal,
    §7, and ends the app).
- Beside the menu, right-aligned and equally always-visible, a single
  icon button mutes/unmutes the spoken announcements described in §4.8 -
  its icon swaps between a plain speaker and a crossed-out one to reflect
  the current state. Muting has no effect on the Preferences dialog's own
  Test control (§3.5), which always plays - pressing it is itself an
  explicit request to hear it.

### 3.5 Preferences dialog
- A modal dialog (File > Preferences…) for the spoken announcements' own
  voice and volume - the mute toggle itself lives only on the main window
  (§3.4), not duplicated here.
- **Voice**: a dropdown of every voice installed on the machine (via the
  OS speech engine), shown under a cleaned-up display name — the
  "Microsoft " prefix Windows' stock voices all share is stripped, and a
  "... Desktop" entry is dropped entirely wherever it's just a duplicate
  of the same voice under its plain name (Windows installs both) — plus
  a small clear button beside it that resets the selection back to
  blank - blank means the engine's own default voice, and is the only
  way back to that default once a specific voice has been chosen, since
  the dropdown itself has no blank entry of its own to click back to.
- **Test** (icon button): speaks a fixed sample phrase through the
  currently-selected voice/volume - always audible, even while muted.
- **Volume**: a slider, 0-100.
- Every change to Voice/Volume is saved immediately (§7), the same
  "edits save as they're made" convention the Controls tab's own Options
  section already uses - **Close** is purely a "done for now" navigation
  action, not a distinct save step.

---

## 4. Route Tab

The Route tab has two mutually exclusive states: **Edit** and **Table**. Only
one is visible at a time.

### 4.1 Edit state (default)
- A single multi-line text box fills the available space of the tab.
- Accepts Enter and Tab as literal input (not focus navigation). No line
  count or character limit.
- When empty, shows placeholder text: "Paste route here, one system per
  line."
- Receives keyboard focus automatically whenever this state becomes visible
  — on app launch, and every time **Edit** is clicked from the Table state.
- Below the text box, right-aligned: **Save** and **Cancel** buttons.
  - **Save** is enabled only when the text box contains at least one
    non-whitespace character. Splits the text into rows (§4.3) and switches
    to Table state. Does not clear the text box.
  - **Cancel** is always enabled. If a route has been saved before,
    restores the text box to that last-saved text and returns to Table
    state — undoing whatever's been typed since Edit was last entered,
    without ever rebuilding `Rows` (only Save does that), so the table
    — including any in-progress icon/status/journal-tracked progress —
    is left exactly as it was. If nothing has ever been saved yet (a
    fresh, never-saved launch), there's no table to return to, so this
    just clears the box instead and stays in Edit state.

### 4.2 Table state (after Save)
- A read-only table (`DataGrid`) fills the available space, with columns:

  | # | Header | Content |
  |---|--------|---------|
  | 1 | *(blank)* | Row icon — see §4.4 |
  | 2 | `#` | Row number, 1-based, in save order |
  | 3 | `System` | The row's system text, plus a clipboard icon when this row's text is currently on the clipboard (§4.6) |
  | 4 | `Status` | Current status text — see §4.4 |

- Column widths are user-resizable (drag a header's edge). The icon, `#`,
  and `System` columns are persisted per column (§7) — restored on the
  next launch in place of the default widths above, until first resized.
  `Status`, the trailing column, always fills whatever width the other
  three leave unused rather than being persisted as a fixed size — its
  progress bar (§4.4) needs real remaining width to stretch into on every
  launch, at whatever size the window happens to be, not a pixel width
  frozen from a previous session.
- Clicking anywhere in a row copies that row's system name to the clipboard
  and plays a confirmation sound (see §4.6 for the clipboard-source icon
  this also sets).
- Right-clicking a row opens a context menu with **"Set next system"**:
  every row before the clicked one is marked Complete, the clicked row
  becomes the current (in-progress) row, and every row after it is reset to
  not-yet-started — regardless of their prior state. This is a direct,
  immediate correction for whenever automatic journal-based detection
  (§5.7) gets it wrong, or the carrier is genuinely off-route.
- Above the table, right-aligned: an **"Auto Copy To Clipboard"** toggle
  switch (§4.6). Not shown while in Edit state.
- Below the table, right-aligned: **Edit** and **Auto Pilot** buttons.
  - **Edit** returns to Edit state with the text box's contents unchanged
    (Save never alters them) and focus restored to it. Re-clicking Save
    afterward always produces a completely fresh table — even if the text
    is identical to what was there before — discarding any prior
    icon/status state. If a Captain is currently assigned (§5.6), the fresh
    table is immediately re-derived from that Captain's journal; otherwise
    row 1 defaults to "next" (in-progress).
  - **Auto Pilot**'s label reads "Auto Pilot" when idle and "Stop" while
    engaged, and **Edit** is disabled while engaged. While engaged, it
    drives the route to completion by playing the Captain's selected
    macro (§5.5) — see §4.7 for exactly when and how. Re-saving the route
    (via Edit → Save) resets it to idle. Enabled only when Captain is
    currently assigned with a macro selected for them, and — only if
    Engineer is *also* currently assigned — a macro selected for Engineer
    too (§5.5); disabled otherwise, including whenever assignment or
    macro selection changes on the Roles tab while this tab is showing,
    or requirements drop out from under a run already in progress (which
    also stops that run outright, not just re-locks the button).

### 4.3 Line-to-row conversion (on Save)
- Text is split on line breaks; `\r\n` is normalized to `\n` first.
- Each line is trimmed of leading/trailing whitespace.
- Blank lines (empty, or whitespace-only) are discarded entirely — leading,
  interior, and trailing — and do not become rows.
- Every remaining line becomes a row, in order, numbered from 1 (row numbers
  count kept lines, not original line positions).
- `System` = the line's trimmed text (internal whitespace preserved).
- Row `#` values are fixed at Save time and never change afterward.

### 4.4 Row icon and status

The icon shown depends on both the row's icon state and, while in-progress,
its current `Status` text:

| Icon state | `Status` | Glyph | Meaning |
|---|---|---|---|
| None | — | *(hidden)* | Not yet reached |
| In-progress | *(blank)* | Play (triangle) | Current row, no status yet |
| In-progress | `Plotting` | ProgressClock | Auto Pilot is playing the Captain's macro (§4.7) |
| In-progress | `Plotted` | Hourglass | `CarrierJumpRequest` received |
| In-progress | `Jumping` | RocketLaunch | 3 minutes before `DepartureTime` |
| In-progress | `Cooldown` | Play (triangle) | Waiting on the row before it |
| Complete | *(blank)* | Check | Reached, permanently for this table |

- Icons render via the toolkit's `PackIcon`, colored from the active
  Material palette (`MaterialDesign.Brush.Secondary`), not hardcoded colors.
- A row's icon is never removed once shown (`Visibility.Hidden`, not
  `Collapsed`, when hiding it — keeping its layout space reserved so row
  height doesn't visibly shift as icons appear/disappear).
- A completed row's own `Status` is always blank — see §5.7 for why
  `Cooldown` is shown on the row *after* the one that just completed, not
  on that row itself.
- A thin progress bar sits below the `Status` cell's text (its own space,
  not overlapping it) for `Plotted`/`Jumping`/`Cooldown` (invisible for a
  blank or Complete status, which have no known end — `Visibility.
  Hidden`, not `Collapsed`, same reserved-space convention as the row
  icon above, so a row without the bar renders at exactly the same
  height as one with it) — purely a cosmetic countdown, not a
  route-progress indicator: full the instant that status starts, and
  visually draining down to empty as it approaches the real-world
  instant §5.7 already computes (or, for `Jumping`, estimates — see
  below) for that status's own end (`Jumping` starting, the eventual
  arrival, or `Cooldown` clearing, respectively). Ticks on a lightweight
  UI timer, entirely separate from the event-driven Sequencing/ logic
  that actually drives those transitions (CLAUDE.md) — it never affects,
  and is never affected by, anything but its own redraw. A row whose
  relevant journal timestamp couldn't be parsed shows that status with
  no progress bar rather than a guess.
  `Plotting` (§4.7) also shows the bar, but as an indeterminate cue (a
  continuously animating sweep, not a value counting down to a known
  end) rather than a countdown - there is no way to know in advance how
  long playing the Captain's macro, or the game's own request/
  acknowledgement, will take, so no time is shown for it either
  (`StatusDisplay` is just the bare word "Plotting").
  `Jumping`'s own end can only be estimated at `CarrierJumpRequest` time
  as `DepartureTime` plus the same 1-minute `ArrivalToCooldownDelay` §5.7
  uses elsewhere — not `DepartureTime` itself — since the row doesn't
  actually leave `Jumping` until the composite Arrived step fires, 1
  minute after the carrier's own arrival, which is not yet known this
  early; the carrier's jump is effectively instantaneous at
  `DepartureTime` in practice, making this a close approximation of the
  real, later-confirmed arrival time.
- Alongside the progress bar, the same countdown is shown as text: the
  `Status` cell reads `Plotted (0:11:32)` — the status word, a space, then
  the remaining time in parentheses as `H:MM:SS` (hours unpadded — a
  single digit once under ten, not `00` — minutes and seconds always two
  digits), ticking down live on the same timer that drives the progress
  bar. Omitted (bare status word only) under the same conditions the
  progress bar itself is hidden.

### 4.5 Persistence
See §7.

### 4.6 "Auto Copy To Clipboard"
- A labelled toggle switch, right-aligned above the table, hidden while in
  Edit state.
- Session-only: the on/off state survives Edit/Save cycles within the
  running app, but is **not** persisted — it always starts off on a fresh
  launch.
- While on: the instant a `CarrierLocation` event for the assigned
  Captain's carrier is observed via **live** journal monitoring (never
  during the initial historical replay a fresh Captain assignment does,
  and never for a passive session-start snapshot — same gating as §5.7's
  route catch-up), the **next** row's `System` text (the row after the one
  the carrier just arrived at) is copied to the clipboard automatically.
  This fires immediately on the raw journal event, ahead of the table's
  own delayed status update, so the next system is ready to paste before
  the UI visually catches up. No-op if the arrived-at row is the route's
  last row, or isn't found in the route.
- Turning the toggle **on** also immediately copies whichever row is
  currently the in-progress row (the same row §4.4's "Play" icon marks as
  current), rather than waiting for the next live arrival. No-op if there
  is no in-progress row (an empty or fully-complete route). Turning the
  toggle back off copies nothing.
- Whichever row's text was most recently copied — by any of the above, or
  by clicking a row — shows a small clipboard icon directly after its
  `System` text (§4.2). At most one row shows it at a time. It disappears
  the instant the system clipboard's contents actually change, for any
  reason, including an application other than RouteJumper overwriting it —
  detected via a real Windows clipboard-change notification, not polling.

### 4.7 Auto Pilot execution

While engaged, Auto Pilot plots each row's jump in turn by playing the
Captain's selected macro (§5.5) against the Captain's assigned instance,
and — if Engineer is currently assigned — refuels by playing the
Engineer's selected macro against the Engineer's assigned instance, until
the whole route reaches Complete or Auto Pilot is stopped:

- For whichever row is currently in-progress, if its `Status` is blank
  (not yet plotted) and it isn't showing `Cooldown`, the Captain's macro
  plays immediately - the row's own `Status` becomes `Plotting` (§4.4)
  the instant the macro actually starts, and stays that way until the
  real `CarrierJumpRequest` arrives and moves it to `Plotted` (§5.7).
  `Plotting` is the one row status not driven by anything in the
  journal - Auto Pilot sets it itself, since there is no journal event
  to react to yet at that point; if the macro never results in a real
  jump request (closed the wrong panel, game unresponsive, ...) the row
  is left showing `Plotting` indefinitely rather than guessing.
- If it *is* showing `Cooldown`, the Captain's macro instead plays once
  Cooldown clears (§5.7) plus the configured Auto Pilot delay (Controls
  tab Options, §6.1) — not immediately on clearing, since the game's own
  UI needs a moment to settle first.
- If the row is already `Plotting`, `Plotted`, or `Jumping` — a plot for
  it is already underway or has already been requested, whether that
  happened before Auto Pilot was even engaged (the app was (re)started,
  or the carrier's own journal simply already had a plot in flight) or
  Auto Pilot itself triggered it moments ago and journal tracking (§5.7)
  just hasn't advanced the row past it yet — the Captain's macro does
  **not** play again for it. Playing it a second time would plot a
  redundant jump; Auto Pilot just waits for journal tracking to move the
  row on naturally, the same as it does for any other row already in
  flight.
- The same rules apply the moment Auto Pilot is engaged, not just to
  later rows: if Cooldown happens to already be active on the
  in-progress row at that moment, the first macro play waits for it
  (plus the delay) exactly the same way; if it's already Plotted or
  Jumping, nothing plays for it at all; if it's blank, it plays right
  away.
- Once that row completes and journal tracking (§5.7) advances the route
  to the next row, the same rule repeats for it, and so on until no row
  is left in-progress.
- Independently of the Captain's plot above: the instant a row becomes
  `Jumping`, Auto Pilot reads its `PhaseEndUtc` (§4.4) - the carrier's
  estimated arrival instant (`DepartureTime` plus 1 minute, the same
  value the progress bar counts down to, and the real-world instant the
  row *after* this one will start `Cooldown` at). Once the configured
  Auto Pilot delay has passed beyond that instant - near the end of
  *this* row's own `Jumping` phase, not before it - and only if Engineer
  is currently assigned, Auto Pilot plays the Engineer's selected macro
  against the Engineer's assigned instance (see §4.8 for the spoken
  announcements this schedules). This fires once per row (not repeatedly
  while it remains `Jumping`). With no Engineer assigned, nothing plays
  here — Auto Pilot's own requirements (§4.2) already mean an assigned
  Engineer always has a macro selected too.
- Both the Captain's plot and the Engineer's refuel play through the same
  single mechanism as a manual Play (§6.5) — visible via `IsPlaying`,
  stoppable via **Stop**, and reported through the same closeable warning
  banner (§6.5) if the target window loses focus mid-script — not a
  separate, invisible channel. That mechanism only ever runs one playback
  at a time, so if the Engineer's refuel is still playing when the
  Captain's own plot fires (or vice versa), starting the new one cancels
  whichever was still running, the same as it would for two manual Plays.
- Auto Pilot stops itself automatically once every row reaches Complete
  (flipping its label back to "Auto Pilot"), and also if Captain (or, if
  assigned, Engineer) stops meeting Auto Pilot's own requirements (§4.2)
  partway through a run - a role or macro selection changing out from
  under an active run halts it immediately rather than continuing
  regardless.
- A `CarrierJumpCancelled` (§5.7) reverting a row's `Status` back to
  blank does not, on its own, cause a further macro play for that same
  row - Auto Pilot only ever plays the Captain's macro once per row it
  newly becomes aware of; recovering from a cancelled jump on the row it
  already played for requires manual intervention. This has no bearing
  on the Engineer's refuel trigger, which is keyed off the row becoming
  `Jumping`, not off a jump request.

### 4.8 Spoken announcements
- Ahead of each of the two triggers §4.7 describes - the Captain's plot
  and the Engineer's refuel - RouteJumper speaks a lead-time announcement
  through the OS's speech engine (voice/volume from the Preferences
  dialog, §3.5): "Plotting in 30 seconds" 30 seconds before the
  Captain's macro is due to play, then "Plotting in 5 seconds" 5 seconds
  before; "Refueling in 30 seconds"/"Refueling in 5 seconds" the same
  way for the Engineer's.
- The Captain's announcement is scheduled the moment a row's `Cooldown`
  starts, against a fixed delay from `Cooldown`'s own precomputed clear
  time. The Engineer's is scheduled the moment the row becomes `Jumping`,
  against `Jumping`'s own `PhaseEndUtc` (§4.4) - the carrier's estimated
  arrival instant (`DepartureTime` plus 1 minute, the same value the
  progress bar counts down to, and the instant the row after it will
  start `Cooldown` at) plus the configured Auto Pilot delay. Since
  `Jumping` itself starts a fixed 3 minutes before `DepartureTime`, this
  gives several real minutes of lead time for the two warnings below,
  rather than only the short, UI-settle-only Auto Pilot delay - nowhere
  near enough for either. Neither announcement is ever spoken for a jump
  that fires immediately with no `Cooldown` having been waited on, since
  that leaves no lead time to announce anything against.
- The 30-second announcement is silently skipped if that much lead time
  isn't actually available by the time it's scheduled - since announcing
  it moments early would just be misleading wording. The 5-second one is
  more forgiving: with less than 5 seconds actually left (but the trigger
  itself still pending), it's spoken immediately instead of skipped, so
  a short-but-real gap still gets *some* last-moment heads-up rather than
  silently none at all.
- The Engineer's announcements are only actually spoken if an Engineer is
  still assigned with a macro selected by the time each is due - checked
  fresh at that moment, not just when first scheduled.
- Muting (§3.4) silently suppresses every announcement described here;
  it has no effect on the Preferences dialog's own Test control (§3.5).

---

## 5. Roles Tab

### 5.1 Process discovery
- Every refresh (initial load, or the Refresh button) enumerates all
  running `EliteDangerous64.exe` processes. Zero found is the documented
  empty state — a centered, italic "No running Elite Dangerous instances
  found." message replaces the card list.
- Refresh runs on a background thread; the button disables itself for the
  duration and re-enables once done. The rest of the app stays interactive
  throughout. Refresh runs automatically once on startup, and again on
  every click.
- While Captain is assigned, a live (not historically-replayed) `CarrierStats`
  event for that commander's carrier — logged whenever they open Carrier
  Management in-game — also triggers a background refresh automatically,
  so the card's carrier name/fuel level update without waiting for a
  manual click.

### 5.2 Journal matching
- There is no OS-level link between a process and the journal file it
  writes to. Every candidate journal file (`Journal.*.log`, in the
  configured journal folder — see below) is timestamped via its own
  `Fileheader` event.
- The journal folder itself comes from `routejumper.conf` (a plain-text,
  `Key=Value`-per-line file beside `routejumper.db` in
  `%LocalAppData%\EDFCAutoPilot`), specifically its `JournalDirectory`
  entry. If the file doesn't exist yet, it's created on first read with
  that entry set to Frontier's own standard location
  (`%USERPROFILE%\Saved Games\Frontier Developments\Elite Dangerous`) —
  hand-editable afterward (e.g. to point at a non-default Saved Games
  location) with no restart required, since it's re-read fresh on every
  Roles tab refresh.
- Each process is matched to the journal whose `Fileheader` timestamp is
  closest to that process's start time, within a 5-minute tolerance.
  Matching is greedy and one-to-one — a journal already assigned to one
  process in a scan cannot also be assigned to another.
- A process with no journal match within tolerance shows "Not found" for
  the journal filename and "Unknown" for every journal-derived field.
- Live `Status.json`/`Cargo.json` are never used — those are per
  *installation*, not per running instance, so with multiple instances
  running there is no way to tell which instance's state a read of those
  files reflects. Everything instance-specific comes from that instance's
  own journal file.

### 5.3 Per-instance card

Each running instance's card shows:

| Field | Source |
|---|---|
| Commander name, FID | Latest `Commander` event |
| Cargo | Latest `Loadout` (capacity) + latest ship `Cargo` (current, defaulting to 0 - not "Unknown" - if no `Cargo` event has been seen at all this session), shown as `current / capacity`t, tritium included in the total with a separate `Nt tritium` sub-line underneath (omitted when zero) |
| Current location | Latest of `Location`/`Docked`/`Undocked`/`FSDJump`/`CarrierJump` (while docked) |
| Fleet carrier | Name from `CarrierStats` (only logged if the Carrier Management panel was opened this session — "Unnamed carrier" if not); location from `CarrierJump`/`CarrierLocation` matched by `CarrierID`, `FleetCarrier` type only (never a squadron carrier) |
| Carrier fuel | The carrier's own tritium fuel tank level (0–1000t), shown as a `Nt fuel` sub-line under Fleet carrier, omitted until known — from whichever of `CarrierStats`' `FuelLevel` or `CarrierDepositFuel`'s `Total` was most recently seen for the commander's own (resolved) `CarrierID` |
| Journal file | Matched journal's filename - clicking it copies the filename to the clipboard and plays a confirmation sound (no visual indicator afterward, unlike the Route tab's row click-to-copy - §4.6) |
| Window handle | `Process.MainWindowHandle`, shown as hex |
| Window position | `GetWindowRect` on the window handle |
| Monitor | `MonitorFromWindow` + `GetMonitorInfo` |

- All fields are found in a single sequential pass over the journal,
  taking the latest occurrence of each relevant event.
- Cargo's current tonnage excludes tritium for the purpose of Engineer
  eligibility (§5.6) but *includes* it in the displayed total, with tritium
  broken out as its own line. Tritium is tracked incrementally across every
  event that can move it into or out of the ship's hold (`Cargo`,
  `CargoTransfer`, `CarrierDepositFuel`, `MarketBuy`, `MarketSell`),
  resyncing from a `Cargo` event's own `Inventory` breakdown whenever one
  is present (it isn't always).
- Frontier only logs a `Cargo` event when the hold's contents actually
  change, so a ship that has carried nothing at all this session never
  gets one - no event is itself evidence of an empty hold, not missing
  data, so current cargo (and tritium) default to 0 rather than
  "Unknown" in that case. This also means Engineer eligibility (§5.6)
  correctly follows from `Loadout`'s capacity alone once it's known, even
  before any `Cargo` event has fired.
- Carrier fuel is an absolute reading (not a tracked delta, unlike ship
  tritium), so "latest wins" per `CarrierID` is enough — but it's still
  resolved against the commander's own confirmed `CarrierID` before
  display, the same way `CarrierSystem`/`CarrierBody` are, since a
  `CarrierDepositFuel` deposit into a squadron carrier the commander
  doesn't own must never be shown as if it were theirs.
- If a process has no main window yet, or exits mid-scan, window/monitor
  fields degrade to "Unknown" rather than failing the whole scan.
- Cards stretch to fill the tab's full available width, in a
  vertically-stacked, scrollable list.

### 5.4 Role assignment
- Two roles, **Captain** and **Engineer**, each assignable to at most one
  running instance (not necessarily the same one). Each card has toggle
  buttons for both.
- **Engineer** cannot be assigned to an instance whose available cargo
  capacity is zero or unknown.
- Assigning **Captain** to an instance (whether or not a different
  instance already held it) resets the whole route, then replays that
  instance's journal into it (§5.7). Clearing Captain — explicitly, or
  because its instance stops running — leaves the route exactly as
  displayed. A new instance appearing in a scan never disturbs
  Captain/Engineer assignments already held by other running instances.
- Assigning Captain also triggers a background rescan of all instances.

### 5.5 Role macros

A section above the instance cards lets the user pick which recorded
macro (Controls tab, §6.5) each role should use once Auto Pilot is
engaged: **Captain plots via** and **Engineer refuels via**, each a
dropdown of every currently recorded macro by name, with a small clear
button beside it to unset the choice. This selection belongs to the role
itself, not to whichever instance currently holds it — it stays put if
the role moves to a different instance, and is independent of whether
the role is even currently assigned at all. A macro that's deleted
(Controls tab) while selected here is cleared back to unset rather than
left pointing at nothing that no longer exists.

Auto Pilot (§4.2) is enabled only once Captain is assigned with a macro
selected here for them, and — only if Engineer is *also* currently
assigned — a macro selected for Engineer too; an unassigned Engineer
imposes no macro requirement of its own.

### 5.6 Persistence of role assignment
See §7.

### 5.7 Journal-driven route updates

Once Captain is assigned, RouteJumper watches that commander's journal file
for `CarrierJumpRequest`/`CarrierLocation` events belonging to their own
fleet carrier (filtered by `CarrierID` and `CarrierType == "FleetCarrier"`,
so a shared squadron carrier's events are never mistaken for the
commander's own).

- On assignment, the whole journal file is read first to work out a
  single, authoritative answer to "what's this carrier's current status
  right now" — **not** by replaying every historical line as its own
  event. This is decided by the journal's own **line order** — the
  strictly authoritative sequence these events actually happened in, not
  each event's own `timestamp` field — walking oldest to newest and
  keeping track of whichever of the carrier's `CarrierJumpRequest`/
  `CarrierLocation`/`CarrierJumpCancelled` events was seen *last*: if
  that's a `CarrierJumpRequest` not yet followed by a matching arrival or
  a cancellation, that request's target is the current system — `Status`
  becomes `Plotted` for it (and `Jumping`, on the normal schedule below,
  if due). Otherwise, the carrier is wherever its most recent
  `CarrierLocation` placed it — that row (and everything before it) is
  marked Complete, and the row after it becomes in-progress. Only this
  one derived result is applied to the route, as a single event — after
  that, new lines are live-tailed via a `FileSystemWatcher` and applied
  one at a time, same as always, since there's only ever one new event to
  react to at a time once live, never a backlog.
- This matters because a carrier's real path through a session isn't
  necessarily a clean walk forward through the pasted route in order — a
  system can legitimately be visited more than once (e.g. revisiting a
  tritium depot between "real" hops). Applying every historical line as
  its own event, oldest to newest, would complete that system's row the
  *first* time some later row's event arrived (see the catch-up rule
  below), permanently excluding it from ever matching again on its
  subsequent, genuine visits — showing stale, wrong progress. Computing
  the single final state first avoids this entirely.
- Matching a route row to a journal event is always by System name
  (case-insensitive), not row position, since the journal only knows
  system names — and always the row's *first* occurrence of that name,
  since the route is always freshly Reset immediately before this
  computed result is applied. Matching a `CarrierJumpRequest`/arrival to
  a row silently completes every earlier not-yet-complete row along the
  way, which is how a single result can catch up several rows at once. A
  system that's a genuine, intended waypoint appearing more than once in
  the route (a deliberately circular route) is the one case "first
  occurrence" can point at the wrong instance of it - the manual "Set
  next system" override (§4.2) exists for exactly that.

**Transitions and timing** (all measured from a journal event's own
timestamp, never from when the app happens to read the line; each fires
immediately if its computed time has already passed when replaying
history, otherwise via a non-blocking timer):

1. `CarrierJumpRequest` → the row's `Status` becomes `Plotted`
   immediately.
2. 3 minutes before that event's own `DepartureTime` → `Status` becomes
   `Jumping`.
3. 1 minute after the matching `CarrierLocation`'s own timestamp → the
   arrived-at row's icon becomes Complete with a blank status; if a next
   row exists, *that* row's icon becomes in-progress. **Only if this
   `CarrierLocation` was observed live** (genuinely tailed in real time,
   never during the initial historical replay described above) does the
   next row's `Status` also become `Cooldown` — the cooldown belongs to
   the row waiting on it, not the row that just finished, but it's only
   ever shown for a jump actually being watched happen, never as a side
   effect of catching a route up to old history (there's no reliable way
   to place a stale row's cooldown window against the current wall clock
   after the fact). Nothing is put into Cooldown if there's no next row.
4. A further 4 minutes after that (5 minutes after `CarrierLocation` in
   total) → the next row's `Cooldown` status clears, if it was showing one.

A `CarrierJumpCancelled` event for the tracked carrier (filtered by
`CarrierID` only — this event carries no `CarrierType`) reverts whatever
the cancelled request had done: whichever row is currently in-progress
with a `Status` of `Plotted` or `Jumping` (there is at most one at a time)
has its `Status` cleared back to blank, leaving its icon in-progress and
ready for a fresh `CarrierJumpRequest`. The event carries no system name
of its own, so the affected row is found by state, not by name. Any
`Jumping` transition the cancelled request had already scheduled (step 2
above) is cancelled at the same time, so a late-firing stale timer can't
re-set `Jumping` on a row whose jump was cancelled.

- Assigning Captain resets every row (`Icon → None`, `Status →` blank)
  before replaying, so a previous Captain's leftover progress never
  lingers.
- This whole mechanism is event-driven by design (see CLAUDE.md's
  non-negotiable rule): every row mutation is a direct response to an
  event, never a hardcoded delay inside the row-update logic itself. The
  real-world scheduling described above lives in the journal watcher, not
  in the row-update logic, which only ever reacts to "now".

### 5.8 Persistence of role assignment (FID-based)
See §7.

---

## 6. Controls Tab

A tab to manage named sets of key bindings for the app's own automation:
record a sequence of keypresses/clicks against a running instance, capture
it as an editable, human-readable script, and replay it later against that
same instance. A macro selected for a role (Roles tab, §5.5) is what Auto
Pilot (§4.2) actually plays once engaged; nothing else here is wired to
Captain/Engineer automatically.

The tab has four vertically-stacked sections: Options, Key Bindings, and
Running Instances stay a fixed, always-visible height at the top; Recorded
Macros fills the rest of the tab's available space beneath them (no
sub-tabs, no whole-page scrolling — each section scrolls internally if its
own content overflows).

### 6.1 Options

Two settings, laid out side by side:
- **Auto Pilot delay (ms)**, defaulting to `5000`. Auto Pilot (§4.2) waits
  this long, on top of however long a jump's Cooldown itself already
  took, both after Cooldown clears before the Captain plots the next jump
  and after Cooldown starts before the Engineer (if assigned) refuels
  (§4.7) - a real jump needs a moment for the game's own UI to settle
  before a macro starts clicking through panels, whichever end of
  Cooldown it's settling after. Has no effect on Cooldown's own timing
  (§5.7), and no effect on a manually-triggered Play (§6.5).
- **Auto wait (ms)**, defaulting to `300`. Applied automatically after
  every leaf instruction a script executes — whether played (§6.5),
  stepped one at a time via the macro editor's Step button (§6.5), or
  triggered by Auto Pilot (§4.7) — so a script can rely on this for
  consistent pacing between actions instead of needing an explicit `WAIT`
  after every single one. Skipped only when the very next instruction is
  itself a `WAIT` (its own duration already provides the pause, so
  stacking this one in front of it would just be redundant) or, when
  stepping specifically, when there's no next instruction at all (the
  script is about to wrap back to the start).

A second row holds two more fields, purely a testing aid for trying a
script out from this tab without a live route or a running instance in
the right cargo/fuel state:
- **Test {NEXT_SYSTEM}**, defaulting to `Sol`. What `PASTE {NEXT_SYSTEM}`
  resolves to for a manual Play or Step started from this tab.
- **Test {TRITIUM_LOOPS}**, defaulting to `1`. What `REPEAT
  {TRITIUM_LOOPS}` resolves to for a manual Play or Step started from
  this tab, skipping the CMDR rescan §6.4 otherwise describes entirely.

Neither field is ever consulted for an Auto Pilot-triggered run (§4.7),
which always resolves both placeholders live. **Play** and **Step**
(§6.3, §6.5) are disabled while either field is blank, the same as any
of their other requirements.
Session-only, like "Auto Copy To Clipboard" (§4.6) — never persisted.

### 6.2 Key Bindings

A section associating each of nine named actions with a real key
(optionally with modifiers, e.g. `Ctrl+Shift+J`) — the vocabulary a
recorded script's tokens are drawn from (§6.4).

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

- Each action is shown as a button labelled with its current binding (e.g.
  "Ctrl+Shift+J"). Clicking it puts that row into capture mode ("Press a
  key…"); the next key pressed anywhere in the tab becomes its new
  binding. A bare modifier key alone (Ctrl/Shift/Alt/Win) does not
  complete capture — only a full chord ending in a non-modifier key does.
  `Escape` cancels capture, leaving the previous binding unchanged.
- Bindings are persisted individually and restored on launch (§7); each
  defaults to the table above until explicitly rebound.

### 6.3 Running Instances

A section listing currently-running Elite Dangerous instances — scanned
independently of the Roles tab (its own process/journal scan), shown as
cards styled consistently with the Roles tab's own instance cards, but
limited to commander name, window position, and monitor, since capacity/
cargo/route data isn't relevant to recording or playback. A **Refresh**
button rescans on demand (also run once automatically on tab
construction); the empty state matches the Roles tab's ("No running Elite
Dangerous instances found.").

This selected instance is the shared target for both recording and
playback (§6.4, §6.5) — there is only one selection, not a separate one
per action. With exactly one running instance, it is selected
automatically, since there is no meaningful choice to make; if a second
instance appears later that automatic selection is left alone. With more
than one instance, or once more appear, the user clicks a card to select
it (highlighted); clicking a different card moves the selection, it does
not add to it.

This section's own button row holds **Refresh** at its left, opposite
**New Script** (§6.5) and the media-style **Record**, **Stop**, and
**Play** buttons grouped at its right — New Script rendered icon-only,
for visual consistency with Record/Stop/Play beside it. A single Stop
button covers both recording and playback, since only one of the two is
ever active at a time:
- **New Script** (§6.5) creates a new, empty macro and opens it directly
  in the editor, for writing or pasting in a script by hand instead of
  recording one. Enabled under the same nothing-else-currently-
  recording/playing/stepping condition as Record below, but — unlike
  Record — needs no selected instance and works even while the editor
  is already open for a different macro.
- **Record** is enabled only while an instance is selected, nothing is
  already recording or playing, and the macro editor (§6.5) isn't open.
- **Play** is enabled only while both an instance and a macro (§6.5) are
  selected, and nothing is currently recording. It is not restricted to
  the instance a macro was originally recorded against — any selected
  running instance can play any recorded macro. Play stays enabled while
  a macro is already playing: pressing it again cancels the running
  playback and starts the newly-selected one in its place.
- **Stop** is enabled while recording or while a macro is playing, and
  cancels whichever is active — immediately, wherever in the script
  playback currently is, not just between steps.

### 6.4 Recording

Pressing **Record** brings the selected instance's window to the
foreground (the same as Play does before playback - see §6.3), then
starts capturing keyboard and mouse input system-wide, filtered to only
the moments the target instance's window is foreground (input while some
other window is focused is not recorded - including, after that initial
foregrounding, input directed back at RouteJumper's own window). Pressing
**Stop** ends capture and turns it into a new named macro (§6.5). Only one
recording can be active at a time.

Each captured key or left-click is classified by how long it was held: a
press released within 400ms is a **tap/click**; longer is a **hold/hold
click**, recorded with its duration. A click's position is relative to
the target window's client area (top-left = `0,0`), taken from where the
button went *down* (not up), since drags aren't recorded; other mouse
buttons and scrolling are not recorded either. A gap of 150ms or more
between two recorded inputs is recorded as an explicit wait of that
length, so played-back timing matches the original pacing.

Capture is translated into a small, line-oriented, human-editable text
script:

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

- A line naming a bound action (e.g. `UP`) taps that action's current key
  binding; `KEY <chord>` taps an arbitrary key/chord not tied to any
  action. `HOLD` takes the same token form plus a millisecond duration.
- `CLICK <x>,<y>` left-clicks that position; `HOLD CLICK <x>,<y> <ms>`
  clicks and holds for `<ms>` before releasing. Either coordinate may be
  the literal placeholder `{CENTRE}` instead of a number, resolved at play
  time to that axis' current midpoint of the target window's client area
  — this is how a macro can reliably click "the middle of the window"
  even if the window has been resized since the macro was recorded or
  written, rather than a fixed position typed directly into the script.
- `REPEAT <n> … END` and `MACRO <name> … END` blocks nest; `MACRO` blocks
  are only valid at the top level (not inside a `REPEAT`) and are not
  themselves played inline — `CALL <name>` invokes one wherever it
  appears, including from inside a `REPEAT`.
- `PASTE <text>` sets the clipboard to `<text>` and sends `Ctrl+V`. The
  text may contain the literal placeholder `{NEXT_SYSTEM}`, resolved at
  play time to the Route tab's current in-progress row's system name (or
  left as literal empty text if there is none) for an Auto Pilot-triggered
  run, or to the Options section's **Test {NEXT_SYSTEM}** field (§6.1) for
  a manual Play or Step started from this tab — this is how a macro can
  paste a value that isn't known until it actually runs, rather than only
  fixed text typed directly into the script. `PASTE` is never produced by
  recording itself (there is no such thing as a "recorded paste" — manual
  `Ctrl+V` during recording is captured as an ordinary key chord); it is
  only ever added or edited into a script by hand.
- `{TRITIUM_LOOPS}` is a further placeholder, usable anywhere a script
  expects a number — most usefully as a REPEAT count, e.g.
  `REPEAT {TRITIUM_LOOPS}`. Unlike `{NEXT_SYSTEM}`/`{CENTRE}` (resolved
  live, instruction by instruction, as playback reaches them), it's
  resolved once, textually, immediately before the whole script is parsed
  — a REPEAT's count has to be known before the script can even be split
  into steps.

  For an **Auto Pilot-triggered run** of a script that actually contains
  this placeholder, RouteJumper first refreshes CMDR info for every
  running instance — the same rescan the Roles and Controls tabs' own
  Refresh buttons do — then resolves the placeholder against whichever
  instance the value should reflect: this tab's own selected instance
  (§6.3) if one happens to be selected here, otherwise the
  currently-assigned Engineer (§5.4) — the normal case, since nothing is
  usually selected in this tab during an Auto Pilot run. The value is the
  number of full ship-loads of tritium that instance still needs to buy or
  mine to fill its carrier's fuel depot to 1000t *and* leave its own cargo
  hold topped off, net of whatever tritium it's already carrying (cargo
  capacity, carrier fuel level, and tracked onboard tritium — see §5.3).
  If that instance's cargo capacity isn't known (or is zero), *or its
  carrier fuel level isn't known yet* (e.g. `CarrierStats`/
  `CarrierDepositFuel` haven't been seen this session, or ownership
  couldn't be confirmed), `{TRITIUM_LOOPS}` resolves to `0` rather than
  risk dividing by an unknown capacity or, worse, silently treating an
  unknown fuel level as an empty depot — which would overstate the loops
  needed rather than under-run. A script with no `{TRITIUM_LOOPS}` in it
  at all skips this rescan entirely and plays immediately — the rescan
  is a real, sometimes multi-second delay (every running instance's
  process/journal), and paying it unconditionally on every single Auto
  Pilot trigger risks the target window's actual in-game state drifting
  from what the script assumes by the time it finally starts sending
  input.

  For a **manual Play or Step** started from this tab, the CMDR rescan
  above never happens at all — the placeholder resolves directly to the
  Options section's **Test {TRITIUM_LOOPS}** field (§6.1) instead, so a
  `REPEAT {TRITIUM_LOOPS}` script can be tried out without any running
  instance's cargo/fuel state being relevant.
- Blank lines and lines starting with `#` are ignored. The parser is
  deliberately forgiving of malformed lines (skipped, not rejected
  outright), since this text is meant to be hand-edited.
- Multiple steps may be written on one line by separating them with `;`
  (whitespace around the `;` is ignored), e.g. `UP; WAIT 200; DOWN` — purely
  a readability convenience for grouping related steps; it has no effect on
  playback, which still executes the same steps in the same order. Never
  produced by recording itself, only by hand-editing.

### 6.5 Recorded Macros

A section listing every macro recorded (or hand-written via **New
Script** — see §6.3) so far (newest last). By default it shows a
compact list, one row per macro:
- The macro's **name**, with the rest of the row's own background also
  clickable (anywhere except the pencil/Delete icons below) — clicking
  anywhere in that clickable area selects that macro as the Play target
  (§6.3), highlighting the row. It does not open the macro for editing.
- A small **pencil** icon that opens the full-size editor (below) for
  that macro.
- A **Delete** icon, always available; deleting the macro that is
  currently selected and/or being edited clears that selection/edit
  state.

While no macro has been recorded (or written via **New Script**) yet,
the list is replaced by an italic hint pointing at a starting point:
"No macros recorded yet. See the README's sample scripts for
ready-to-use examples, or click New Script above to write one by hand."
— "sample scripts" is a real link, opening the README's
[Sample scripts](README.md#sample-scripts) section in the default
browser. Disappears for good the moment the first macro exists, the
same as any other empty-state message elsewhere in the app.

The source commander a macro was originally recorded against is shown as
a small caption under its name — display only; it does not restrict which
instance can play the macro (§6.3). Omitted entirely (no blank line left
behind) for a macro created via **New Script** rather than recorded,
since it has no source commander to show.

The **New Script** button (§6.3's own button row, not this section)
creates a new, empty macro (blank script text, no source commander) and
immediately opens it in the editor below — for writing a script by
hand, or pasting one in from elsewhere, without ever recording anything.
The new macro is persisted and behaves identically to a recorded one
from this point on — Play, Step, rename, delete, and role-macro
selection (§5.5) all work the same regardless of how a macro's script
text originally got there.

Clicking the pencil replaces the list with a full-size **editor** for
that one macro, since a script is naturally a tall, narrow list of short
commands and benefits from as much vertical room as the window has to
give it. Opening the editor also selects that macro as the Play target
(§6.3) — the same as clicking its name in the list — so Play can be used
without leaving the editor, e.g. to try out an edit immediately:
- An editable **name** field.
- A large, editable, multi-line **script text** box beside a narrow,
  fixed-width reference panel listing the script grammar (§6.4) —
  positioned as a side column, not stacked above or below the text box,
  so the reference never competes with the script for vertical space.
  The reference is grouped under three headings, in order: **Actions**
  (`ACTION_NAME`, `KEY`, `HOLD`, `CLICK`, `HOLD CLICK`, `PASTE`, and
  `# comment`), **Controls** (`WAIT`, `REPEAT`, `MACRO`, `CALL`), and
  **Variables** (the three placeholders in use — `{CENTRE}`,
  `{NEXT_SYSTEM}`, `{TRITIUM_LOOPS}`).
- A **Save** button below the script text box returns to the compact
  list. Edits (to either the name or the script text) are saved as
  they're made, independent of Save — pressing it is purely a "done for
  now" navigation action, not a distinct save step.
- The Key Bindings section (§6.2) stays visible above the editor, since a
  script is written in terms of that vocabulary. **Record** (§6.3) is
  disabled for as long as the editor is open.
- A **Step** button beside Save, labelled with what it's about to run
  next — e.g. "Next: RIGHT", "Next: CLICK 240,160" — naming the next
  leaf instruction in the same terms its script line uses (a REPEAT is
  counted by its unrolled iterations, a CALL by the macro it inlines).
  Pressing it executes just that instruction against the selected
  instance, then stops — for trying a script one command at a time
  without running the whole thing. `WAIT` steps are skipped
  entirely rather than counted as a step of their own — there's nothing
  to observe from single-stepping a pure delay, so it would only cost an
  extra click for no visible effect. Since the target instance's
  window won't generally still be foreground after the user's own click
  back on RouteJumper to press Step again, each step re-foregrounds it
  first, the same as Play does once at the very start of a whole script.
  Reaching the end wraps back to the beginning, so the same script can be
  stepped through repeatedly; editing the script text restarts stepping
  from the beginning. Enabled under the same conditions as Play (an
  instance selected, nothing already recording/playing/stepping), plus a
  macro actually open in the editor with at least one step to run.
  **Stop** (§6.3) also cancels a step in progress, the same as it does
  for a full playback.

Pressing **Record** again while macros already exist always starts a
brand-new recording as an additional macro; it never overwrites or
appends to an existing one.

Playing the selected macro against the selected instance (§6.3) brings
that instance's window to the foreground, then executes the script's
steps in order using simulated system-wide input (not messages posted to
the window directly, since a DirectX game reads actual input device state
rather than the window message queue) — the same key-chord/hold/click/
wait/paste vocabulary §6.4 describes, resolving `PASTE {NEXT_SYSTEM}`
against the Route tab's live state at the moment it executes. After each
step, the configured Auto wait (§6.1) is applied automatically before the
next one runs, unless the next step is itself a `WAIT` — this is what
lets a script rely on consistent built-in pacing between actions instead
of an explicit `WAIT` after every single one, and applies the same way
whether this playback was started manually or by Auto Pilot (§4.7).
Starting a new playback while a previous one is still in progress cancels
the previous one first, the same as pressing **Stop** (§6.3) does
explicitly — playback can be cancelled at any point in the script, not
just between steps.

If the target window ever loses focus while a script is playing (the
simulated input has nowhere else meaningful to go once something else has
focus), playback aborts immediately and a closeable warning banner is
shown — a solid, high-contrast fill (not just an outline), so it reads
clearly as a warning rather than something easy to miss. It's shown above
the tab area itself, so it stays visible regardless of which tab the user
is currently on, not just while looking at the Controls tab — dismissed
via its own close button, and otherwise left showing until the user
dismisses it or starts another playback. An ordinary user-initiated Stop
is not treated as this kind of failure and shows no message.

### 6.6 Persistence
See §7.

---

## 7. Persistence

A single generic `Settings(Key TEXT PRIMARY KEY, Value TEXT)` table in a
local SQLite database at `%LocalAppData%\EDFCAutoPilot\routejumper.db`. Every
operation opens and closes its own short-lived connection. A persistence
failure (e.g. a permissions issue) degrades to "nothing persisted" rather
than the app failing to start.

A separate, deliberately hand-editable `routejumper.conf` sits beside the
database (see §5.2) for configuration a user might reasonably want to
change directly in a text editor — currently just the journal folder —
rather than internal app state like the table below.

| What | Persisted when | Restored when |
|---|---|---|
| Route text | Every Save (first time or after Edit) | App startup, rebuilt via the normal Save path |
| Window position, size, maximized state | Window closing | App startup — in place of the default rightmost-monitor placement, unless the persisted position is no longer reachable on the current monitor setup |
| Route table column widths (icon, `#`, `System` only — not `Status`, §4.2) | Window closing | App startup, per column — falling back to that column's default width until first resized |
| Captain/Engineer role, by commander FID | Assigned/explicitly unassigned | Every Roles tab refresh, while currently unassigned in memory |
| Captain/Engineer role macro, by macro Id (§5.5) | Selected/cleared | Every Roles tab refresh, while currently unselected in memory |
| Controls tab options (Auto Pilot delay, Auto wait) | Every change | App startup, falling back to their 5000ms/300ms defaults until first changed |
| Controls tab key bindings | Every successful capture (§6.2) | App startup, per action — falling back to that action's default binding until first rebound |
| Controls tab recorded macros | Every new recording, edit, or delete (§6.5) | App startup, as a single collection |
| Announcement voice, volume, muted (§3.4, §3.5) | Every change | App startup, falling back to the engine's own default voice, 100 volume, and unmuted until first changed |

- Role assignment is restored by FID (not `ProcessId`, which doesn't
  survive a process restart) — this covers both "app just launched" and
  "the role holder's game process restarted mid-session" with the same
  logic. A restored Captain match goes through the same reset-and-replay
  as a fresh manual assignment.
- Explicitly unassigning a role clears its persisted FID, so it is not
  automatically reassigned on the next refresh. A role holder's process no
  longer running clears the in-memory assignment but leaves the persisted
  FID alone, so it can be picked back up automatically if that commander's
  instance reappears.
- A role's selected macro is persisted by the macro's own stable Id, not
  its (freely editable) name, so renaming a macro never loses a role's
  selection of it; deleting the selected macro clears the role's
  selection back to unset instead of leaving it pointing at nothing.
- "Auto Copy To Clipboard" (§4.6) is deliberately **not** persisted —
  always off on a fresh launch, by design.
- Not persisted: per-row progress (icon/status), and the last-selected
  tab.

---

## 8. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (`net8.0-windows`), MVVM throughout — no
  business logic in code-behind. The window-placement, focus-management,
  clipboard-monitoring, and Preferences-dialog-opening code in
  `MainWindow.xaml.cs`/`RouteView.xaml.cs` is the sole, deliberate
  exception, since it needs direct access to a real HWND/message pump (or,
  for the Preferences dialog, is itself an inherently view-layer concern)
  that a ViewModel has no access to.
- **Responsiveness:** the UI stays interactive throughout a Roles tab
  refresh (a background-thread scan).
- **No external dependencies** beyond the .NET/WPF base class libraries,
  `MaterialDesignThemes`/`MaterialDesignColors`, `Microsoft.Data.Sqlite`,
  and `System.Speech` (the OS speech engine used for §4.8's spoken
  announcements).

---

## 9. Styling — Material Design

- `App.xaml` merges a `materialDesign:BundledTheme` (`PrimaryColor="Blue"`,
  `SecondaryColor="Cyan"`, `BaseTheme="Light"`) plus
  `MaterialDesign2.Defaults.xaml`. No dark/light theme toggle.
- `MainWindow` uses the `MaterialDesignWindow` style so the title bar is
  themed, not just the controls inside it.
- Row/status icons and the "Auto Copy To Clipboard" switch use the
  toolkit's `PackIcon` and `ToggleButton` (styled
  `MaterialDesignSwitchToggleButton`) controls, colored from the active
  Material palette (`MaterialDesign.Brush.Secondary`), never hardcoded
  colors.

---

## 10. Acceptance Criteria

1. Launching the app shows **Route**, **Roles**, **Controls** tabs with
   headings on the left edge; Route is selected by default.
2. Route tab starts in Edit state: an empty, full-size text box with
   disabled Save, enabled Cancel, and keyboard focus already on the box.
3. Typing text enables Save; on a fresh, never-saved launch, clicking
   Cancel clears the text box (there's nothing saved yet to return to).
4. Saving N non-blank lines produces a table with exactly N rows, numbered
   1..N, `System` matching the input verbatim (trimmed), `Status` empty,
   icon column empty except row 1 (in-progress).
5. Clicking Edit returns to the text box unchanged, with focus restored;
   re-clicking Save always produces a fresh table, re-derived from the
   currently-assigned Captain's journal if one is assigned, else defaulting
   row 1 to in-progress. Clicking Cancel instead (with a route already
   saved) discards any changes made since Edit was clicked, returns to
   Table state, and leaves the table exactly as it was - including
   whatever icon/status progress it already had.
6. Clicking Auto Pilot flips its own label between "Auto Pilot" and
   "Stop" and disables/enables Edit accordingly; while engaged, it drives
   the route via the Captain's macro (§4.7) rather than sitting idle.
7. Clicking anywhere in a row copies that row's system name to the
   clipboard, plays a confirmation sound, and shows the clipboard icon
   after that row's `System` text.
8. Right-clicking a row and choosing "Set next system" marks every row
   before it Complete, makes it the current row, and resets every row
   after it — regardless of prior state.
9. Launching the app (or clicking Refresh) with zero
   `EliteDangerous64.exe` processes shows the empty-state message; with N
   processes, shows exactly N cards with correctly-attributed data, even
   with multiple instances running simultaneously.
10. Restarting one instance updates only that instance's card on the next
    refresh, without disturbing others.
11. Assigning Captain to a card assigns it to that instance only,
    unassigning it from any other card that held it; the same
    independently for Engineer. Both roles can be assigned to the same
    instance.
12. Assigning Captain resets the route, then applies that Captain's own
    fleet carrier's *current* status to it as a single result computed
    from the whole journal — including catching up rows that have no
    event of their own — using whichever of a still-pending jump request
    or the carrier's most recent arrival is actually the latest word on
    its progress, not a naive line-by-line replay that could complete a
    revisited system's row too early and never match it again.
13. A row that receives a jump request transitions to `Jumping` 3 minutes
    before that request's own `DepartureTime`; the composite Arrived step
    happens 1 minute after the matching arrival event's own timestamp, on
    the row *after* the one that arrived, not the arrived-at row itself,
    and only if a next row exists. `Cooldown` itself only ever appears for
    a genuinely live-observed arrival — never as a side effect of
    assigning/reassigning Captain and replaying journal history, even for
    a jump that completed only moments before assignment — and, when
    shown, clears a further 4 minutes later.
14. The Engineer button is disabled on any card whose available cargo
    capacity is zero or unknown.
15. Turning "Auto Copy To Clipboard" on immediately copies the current
    in-progress row's system to the clipboard (no-op if there is none);
    while on, a live-observed carrier arrival copies the *next* row's
    system, ahead of the row table's own delayed status update.
16. Saving a route, moving/resizing the window, and assigning Captain all
    persist; relaunching the app restores the route, window bounds, and
    Captain assignment (including a fresh journal replay) with no manual
    interaction required. "Auto Copy To Clipboard" always resets to off.
17. Explicitly unassigning a role clears its persisted FID so it is not
    automatically reassigned on a later launch.
18. A `CarrierJumpCancelled` event clears the `Status` (`Plotted` or
    `Jumping`) off the currently in-progress row, leaving its icon
    in-progress; any already-scheduled `Jumping` transition for that
    cancelled request never fires afterward.
19. While Captain is assigned, a live `CarrierStats` event triggers a
    background Roles tab refresh automatically; the same event during the
    initial historical journal replay does not.
20. A card whose commander has opened Carrier Management (or deposited
    fuel) this session shows the carrier's fuel tank level as a `Nt fuel`
    sub-line under Fleet carrier, resolved against the commander's own
    carrier — never a squadron carrier's — and omitted entirely until
    known.
21. On first launch (or whenever `routejumper.conf` is missing), the file
    is created beside `routejumper.db` with the default journal folder.
    Hand-editing `JournalDirectory` in that file and clicking Refresh
    rescans the newly-configured folder immediately, with no app restart
    required.
22. A ship that has never had a `Cargo` event logged this session shows
    `0`, not "Unknown", for current cargo (and tritium); once cargo
    capacity is also known, this is enough on its own to make Engineer
    assignable.
23. Clicking a card's journal filename copies it to the clipboard and
    plays a confirmation sound; no clipboard-source icon appears
    afterward (unlike the Route tab's equivalent, §4.6).
24. The Controls tab shows all nine key-binding actions with their default
    bindings on first launch; clicking a binding button enters capture
    mode, a subsequent non-modifier key (chord) rebinds it and persists
    the new binding, and `Escape` cancels capture without changing it.
25. The Controls tab's Running Instances section scans and lists running
    instances independently of the Roles tab, as Roles-styled cards. With
    exactly one instance running it is selected automatically; with more
    than one, clicking a card selects it. Record is enabled only while an
    instance is selected and nothing is already recording or playing.
26. Pressing Record brings the selected instance's window to the
    foreground, then captures taps vs. holds and clicks vs. held clicks
    (400ms threshold for both), left-click position (window-relative,
    taken from where the button went down), and gaps ≥150ms as explicit
    waits, and produces a human-readable script in the documented grammar
    (§6.4) on Stop, added as a new macro without disturbing any existing
    ones.
27. `CLICK`/`HOLD CLICK` coordinates may use the literal placeholder
    `{CENTRE}` in place of a number for either axis, resolved at play
    time against the target window's then-current client-area size.
28. The Recorded Macros list shows each macro as a name with the rest of
    its row also clickable (selects it as the Play target, highlighting
    it), except for a pencil icon (opens the full-size editor) and a
    delete icon, which are excluded from that click area. Play is enabled
    only once both an instance and a macro are selected, and can play any
    selected macro against any selected instance, regardless of which
    instance it was originally recorded against.
29. Clicking a macro's pencil icon selects that macro (as Play's target)
    and opens an editor with an editable name, a large editable script
    text box, and a narrow side panel documenting the script grammar
    (§6.4); edits to either field are persisted as they're made, and
    Save (below the script box) returns to the list without losing them.
    The Key Bindings section remains visible above the editor throughout,
    and Record is disabled for as long as the editor is open.
30. Playing a macro brings the selected instance's window to the
    foreground and executes the script's steps in order, resolving any
    `PASTE {NEXT_SYSTEM}` against the Route tab's current in-progress row
    at the moment it plays; starting a new playback cancels any playback
    already in progress, the same as pressing Stop does explicitly. Stop
    is enabled while recording or while a macro is playing (never both at
    once) and cancels whichever is active immediately, wherever in the
    script playback currently is.
31. Key bindings and recorded macros both survive an app restart with no
    manual reconfiguration required.
32. If the target instance's window loses focus while a macro is
    playing, playback aborts immediately and a closeable, solid-red
    warning banner (white text) explaining why is shown above the tab
    area, visible regardless of which tab is active; dismissing it (or
    starting another playback) clears it. Pressing Stop or starting a
    replacement playback does not show this message.
33. The Roles tab shows a Captain and an Engineer macro picker, listing
    every recorded macro by name, independent of which instance (if any)
    currently holds each role. The Route tab's Auto Pilot button is
    disabled unless Captain is assigned with a macro selected, and -
    only if Engineer is also assigned - a macro is selected for Engineer
    too; toggling role assignment or macro selection on the Roles tab
    re-evaluates this immediately, whichever tab is currently showing.
34. Deleting a macro (Controls tab) that's currently selected for
    Captain or Engineer clears that role's selection rather than leaving
    it referencing a macro that no longer exists. Renaming a selected
    macro does not lose the selection.
35. The Controls tab shows an Options section above Key Bindings with an
    "Auto Pilot delay (ms)" setting, defaulting to 5000, and an
    "Auto wait (ms)" setting, defaulting to 300, laid out side by
    side; both persist across restarts.
36. Engaging Auto Pilot plays the Captain's selected macro against the
    Captain's assigned instance for whichever row is currently
    in-progress, but only if it still needs plotting: immediately if its
    Status is blank and it isn't showing Cooldown, or after Cooldown
    clears plus the configured Auto Pilot delay if it is - the same rule
    whether this is the first row (evaluated the moment Auto Pilot is
    engaged) or a later one (evaluated as journal tracking advances the
    route). If the row is already Plotted or Jumping when evaluated -
    including the moment Auto Pilot is first engaged, e.g. after an app
    restart mid-journey with a jump already in flight - the macro is not
    played again for it. This repeats until every row reaches Complete,
    at which point Auto Pilot stops itself automatically, or until Auto
    Pilot is stopped manually or its requirements stop being met
    mid-run.
37. The macro editor's Step button runs exactly one leaf instruction
    against the selected instance, re-foregrounding it first, and its
    label names that upcoming instruction (e.g. "Next: RIGHT"); `WAIT`
    steps are skipped rather than counted; reaching the last step wraps
    back to the first. Step is disabled while recording, playing, or
    already stepping, and Stop cancels a step in progress the same as
    it cancels a full playback. After running an instruction, Step
    pauses for the configured Auto wait before it's ready to run the
    next one, except when that instruction was the script's last one or
    the next one is a `WAIT`.
38. Before an Auto Pilot-triggered run of a script whose text contains
    `{TRITIUM_LOOPS}`, RouteJumper rescans CMDR info and resolves it to
    the number of full ship-loads of tritium this tab's own selected
    instance (or, with none selected, the currently-assigned Engineer)
    still needs to fill its carrier's fuel depot to 1000t and top off its
    own cargo hold, net of tritium already aboard — `0` if that
    instance's cargo capacity or carrier fuel level isn't known (an
    unknown fuel level must never be treated as an empty depot). A script
    without the placeholder skips the rescan and plays immediately. A
    manual Play or Step from this tab never rescans at all — see
    criterion 43.
39. Independently of the Captain's plot, if Engineer is currently
    assigned, the moment a row's Status becomes Cooldown, Auto Pilot
    waits the same configured Auto Pilot delay and then plays the
    Engineer's selected macro against the Engineer's assigned instance -
    once per row's Cooldown period, alongside (not instead of) the
    Captain's own plot-at-Cooldown-clear trigger, and not at all with no
    Engineer assigned.
40. A row showing Plotted, Jumping, or Cooldown shows a progress bar
    below its Status text that starts full and visually drains to empty
    by the real-world instant that status is due to end, and the Status
    text itself shows the same countdown as "Status (H:MM:SS)" (hours
    unpadded, minutes/seconds always two digits), ticking down live; a
    row with a blank or Complete status shows neither, and renders at
    the same height as a row that does.
41. Resizing the Route table's icon, `#`, or `System` column and
    relaunching the app restores that column's width exactly as left, per
    column, with no manual reconfiguration required. `Status` always
    fills the remaining width instead, on every launch regardless of
    window size, rather than being restored at a fixed size.
42. During a manual Play or an Auto Pilot-triggered run, the configured
    Auto wait is applied automatically after every instruction the script
    executes, unless the next one is itself a `WAIT` - the same setting
    and rule the macro editor's Step button applies (criterion 37),
    uniformly regardless of how the script was started.
43. The Options section shows "Test {NEXT_SYSTEM}" (defaulting to `Sol`)
    and "Test {TRITIUM_LOOPS}" (defaulting to `1`) fields; a manual Play
    or Step started from this tab resolves those placeholders directly
    from these fields (no CMDR rescan, no Route tab dependency), while an
    Auto Pilot-triggered run always resolves them live regardless of what
    these fields hold. Play and Step are both disabled while either field
    is blank. Neither field persists across a restart.
44. Clicking **New Script** creates a new macro with empty script text
    and no source commander, adds it to the Recorded Macros list, and
    opens it directly in the editor — with no selected instance and no
    prior recording required. It's enabled/disabled by the same
    nothing-else-recording/playing/stepping condition as Record, but
    unlike Record does not require a selected instance and remains
    enabled while the editor is already open for another macro. The
    macro it creates persists across a restart and can be played,
    stepped, renamed, or selected for a role's macro exactly like a
    recorded one.
45. A **File** menu is visible above the tabs regardless of which tab is
    active; **Preferences…** opens the modal Preferences dialog (§3.5),
    and **Exit** closes the app (persisting window bounds first, as
    normal). An always-visible mute button sits beside the menu; clicking
    it toggles its icon between a plain and a crossed-out speaker.
46. While Auto Pilot is running, once a row's `Cooldown` starts, "Plotting
    in 30 seconds" is spoken 30 seconds before the Captain's macro is due
    to plot the next jump, then "Plotting in 5 seconds" 5 seconds before.
    The same wording with "Refueling" is
    spoken ahead of the Engineer's refuel macro if Engineer is assigned,
    scheduled the moment the row becomes `Jumping` (the configured Auto
    Pilot delay after `Jumping`'s own estimated-arrival `PhaseEndUtc`).
    Muting suppresses both; neither is spoken for a jump that fires
    immediately with no Cooldown having been waited on. The 30-second
    one is silently skipped if that much lead time isn't actually
    available by the time it's scheduled; the 5-second one is spoken
    immediately instead of skipped whenever less than 5 seconds remain
    but the trigger itself hasn't happened yet.
47. The Preferences dialog lists every voice installed on the machine in
    its Voice dropdown, without a "Microsoft " prefix and without a
    duplicate "... Desktop" entry for the same voice; selecting one takes
    effect and persists immediately, and its clear button resets the
    selection back to the
    engine's own default. Its Test button speaks a sample phrase through
    the current voice/volume regardless of whether the main window's mute
    button is currently muted. Its Volume slider (0-100) persists
    immediately.
48. Voice, volume, and muted all survive an app restart with no manual
    reconfiguration required, each falling back to its own default (the
    engine's default voice, 100, unmuted) until first changed.
49. The instant Auto Pilot begins playing the Captain's macro for a blank
    row, that row's icon becomes ProgressClock and its Status becomes
    "Plotting" with no countdown - an indeterminate progress bar instead
    of a counting-down one, and no "(H:MM:SS)" in the Status text. It
    stays that way (Auto Pilot does not play the macro again for it)
    until the real CarrierJumpRequest arrives and moves it to "Plotted",
    even if that takes an indeterminate amount of time.
