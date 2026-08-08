# RouteJumper — Application Specification

**Version:** 1.5
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
| Route tab: paste a route, save it to a table, track progress against a real journal | Sending input (keystrokes, clicks) to a detected game window |
| Roles tab: detect running Elite Dangerous instances; assign Captain/Engineer roles | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Event-driven row-progress engine, driven by a Captain's journal | Content of the Controls tab (currently an empty placeholder) |
| Manual "Set next system" override for correcting automatic detection | Configurable journal-match tolerance or cooldown timing |
| Persistence of route text, window bounds, and Captain/Engineer role assignment | Persisting per-row progress (icon/status), or the last-selected tab |
| "Auto Copy To Clipboard" for the next system, plus a clipboard-source indicator | |
| Material Design styling | |

---

## 3. Window Structure

### 3.1 Main Window
- Single top-level window with a `TabControl` holding exactly three tabs, in
  order: **Route**, **Roles**, **Controls**. Route is selected by default.
- Tab headings are placed on the **left edge** of the window
  (`TabStripPlacement="Left"`).
- Window is resizable; content reflows (no fixed-pixel layouts).
- Window title: **"ED:FC AutoPilot"**.
- Window/taskbar icon: `Resources/AppIcon.ico`, a `RocketLaunch` glyph rendered
  at 16/32/48/256px, matching the row icons' style and color.

### 3.2 Startup window placement
- On launch, the window is positioned against the **right edge** of the
  physically rightmost connected monitor (10px margin), vertically centered on
  that monitor — unless a previous session's bounds were persisted and are
  still reachable (see §6), in which case those are restored instead.
- The rightmost monitor is determined dynamically each launch (the monitor
  with the greatest `Left` screen coordinate), not a hardcoded device name.
- Positioning happens at `SourceInitialized` (HWND exists, window not yet
  painted), so there is no visible jump on launch. Monitor bounds are
  converted to WPF device-independent units via the window's own
  composition-target transform, so this is correct under per-monitor DPI
  scaling.

### 3.3 Controls tab
Empty placeholder — no content, no behavior. Reserved for future use.

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
  - **Cancel** is always enabled; clears all text from the box.

### 4.2 Table state (after Save)
- A read-only table (`DataGrid`) fills the available space, with columns:

  | # | Header | Content |
  |---|--------|---------|
  | 1 | *(blank)* | Row icon — see §4.4 |
  | 2 | `#` | Row number, 1-based, in save order |
  | 3 | `System` | The row's system text, plus a clipboard icon when this row's text is currently on the clipboard (§4.6) |
  | 4 | `Status` | Current status text — see §4.4 |

- Clicking anywhere in a row copies that row's system name to the clipboard
  and plays a confirmation sound (see §4.6 for the clipboard-source icon
  this also sets).
- Right-clicking a row opens a context menu with **"Set next system"**:
  every row before the clicked one is marked Complete, the clicked row
  becomes the current (in-progress) row, and every row after it is reset to
  not-yet-started — regardless of their prior state. This is a direct,
  immediate correction for whenever automatic journal-based detection
  (§5.6) gets it wrong, or the carrier is genuinely off-route.
- Above the table, right-aligned: an **"Auto Copy To Clipboard"** toggle
  switch (§4.6). Not shown while in Edit state.
- Below the table, right-aligned: **Edit** and **Auto Pilot** buttons.
  - **Edit** returns to Edit state with the text box's contents unchanged
    (Save never alters them) and focus restored to it. Re-clicking Save
    afterward always produces a completely fresh table — even if the text
    is identical to what was there before — discarding any prior
    icon/status state. If a Captain is currently assigned (§5.5), the fresh
    table is immediately re-derived from that Captain's journal; otherwise
    row 1 defaults to "next" (in-progress).
  - **Auto Pilot** toggles a placeholder running state: its label reads
    "Auto Pilot" when idle and "Stop" while engaged, and **Edit** is
    disabled while engaged. Toggling it has no other effect — it is not yet
    wired to any automation. Re-saving the route (via Edit → Save) resets
    it to idle.

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
| In-progress | `Plotted` | Hourglass | `CarrierJumpRequest` received |
| In-progress | `Jumping` | RocketLaunch | 3 minutes before `DepartureTime` |
| In-progress | `Cooldown` | Play (triangle) | Waiting on the row before it |
| Complete | *(blank)* | Check | Reached, permanently for this table |

- Icons render via the toolkit's `PackIcon`, colored from the active
  Material palette (`MaterialDesign.Brush.Secondary`), not hardcoded colors.
- A row's icon is never removed once shown (`Visibility.Hidden`, not
  `Collapsed`, when hiding it — keeping its layout space reserved so row
  height doesn't visibly shift as icons appear/disappear).
- A completed row's own `Status` is always blank — see §5.6 for why
  `Cooldown` is shown on the row *after* the one that just completed, not
  on that row itself.

### 4.5 Persistence
See §6.

### 4.6 "Auto Copy To Clipboard"
- A labelled toggle switch, right-aligned above the table, hidden while in
  Edit state.
- Session-only: the on/off state survives Edit/Save cycles within the
  running app, but is **not** persisted — it always starts off on a fresh
  launch.
- While on: the instant a `CarrierLocation` event for the assigned
  Captain's carrier is observed via **live** journal monitoring (never
  during the initial historical replay a fresh Captain assignment does,
  and never for a passive session-start snapshot — same gating as §5.6's
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
  `%LocalAppData%\RouteJumper`), specifically its `JournalDirectory`
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
  eligibility (§5.5) but *includes* it in the displayed total, with tritium
  broken out as its own line. Tritium is tracked incrementally across every
  event that can move it into or out of the ship's hold (`Cargo`,
  `CargoTransfer`, `CarrierDepositFuel`, `MarketBuy`, `MarketSell`),
  resyncing from a `Cargo` event's own `Inventory` breakdown whenever one
  is present (it isn't always).
- Frontier only logs a `Cargo` event when the hold's contents actually
  change, so a ship that has carried nothing at all this session never
  gets one - no event is itself evidence of an empty hold, not missing
  data, so current cargo (and tritium) default to 0 rather than
  "Unknown" in that case. This also means Engineer eligibility (§5.5)
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
  instance's journal into it (§5.6). Clearing Captain — explicitly, or
  because its instance stops running — leaves the route exactly as
  displayed. A new instance appearing in a scan never disturbs
  Captain/Engineer assignments already held by other running instances.
- Assigning Captain also triggers a background rescan of all instances.

### 5.5 Persistence of role assignment
See §6.

### 5.6 Journal-driven route updates

Once Captain is assigned, RouteJumper watches that commander's journal file
for `CarrierJumpRequest`/`CarrierLocation` events belonging to their own
fleet carrier (filtered by `CarrierID` and `CarrierType == "FleetCarrier"`,
so a shared squadron carrier's events are never mistaken for the
commander's own).

- On assignment, the whole journal file is read first, oldest to newest,
  replaying every matching historical event in order — this is what lets
  a single assignment bring the route fully up to date even after several
  jumps already happened (e.g. the app was restarted mid-journey). After
  that, new lines are live-tailed via a `FileSystemWatcher`.
- A `CarrierLocation` event is only trusted as evidence of a real jump —
  and so only used for row matching/catch-up — once a `CarrierJumpRequest`
  has been seen for that carrier in the journal. The very first
  `CarrierLocation` in a session is Frontier's passive "wherever the
  carrier happened to be when the game loaded" snapshot, not the result of
  a jump. Exception: if the carrier has made *no* jump requests anywhere
  in the journal at all (e.g. right after restarting the game mid-journey,
  before requesting a new jump), the passive snapshot is trusted instead,
  since it's the only evidence of progress available.
- Matching a route row to a journal event is always by System name
  (case-insensitive), not row position, since the journal only knows
  system names. Matching a `CarrierJumpRequest`/arrival to a row silently
  completes every earlier not-yet-complete row along the way, so one event
  can catch up several rows at once.

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

### 5.7 Persistence of role assignment (FID-based)
See §6.

---

## 6. Persistence

A single generic `Settings(Key TEXT PRIMARY KEY, Value TEXT)` table in a
local SQLite database at `%LocalAppData%\RouteJumper\routejumper.db`. Every
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
| Captain/Engineer role, by commander FID | Assigned/explicitly unassigned | Every Roles tab refresh, while currently unassigned in memory |

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
- "Auto Copy To Clipboard" (§4.6) is deliberately **not** persisted —
  always off on a fresh launch, by design.
- Not persisted: per-row progress (icon/status), and the last-selected
  tab.

---

## 7. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (`net8.0-windows`), MVVM throughout — no
  business logic in code-behind. The window-placement, focus-management,
  and clipboard-monitoring code in `MainWindow.xaml.cs`/`RouteView.xaml.cs`
  is the sole, deliberate exception, since it needs direct access to a
  real HWND/message pump that a ViewModel has no access to.
- **Responsiveness:** the UI stays interactive throughout a Roles tab
  refresh (a background-thread scan).
- **No external dependencies** beyond the .NET/WPF base class libraries,
  `MaterialDesignThemes`/`MaterialDesignColors`, and `Microsoft.Data.Sqlite`.

---

## 8. Styling — Material Design

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

## 9. Acceptance Criteria

1. Launching the app shows **Route**, **Roles**, **Controls** tabs with
   headings on the left edge; Route is selected by default.
2. Route tab starts in Edit state: an empty, full-size text box with
   disabled Save, enabled Cancel, and keyboard focus already on the box.
3. Typing text enables Save; clicking Cancel clears the text box.
4. Saving N non-blank lines produces a table with exactly N rows, numbered
   1..N, `System` matching the input verbatim (trimmed), `Status` empty,
   icon column empty except row 1 (in-progress).
5. Clicking Edit returns to the text box unchanged, with focus restored;
   re-clicking Save always produces a fresh table, re-derived from the
   currently-assigned Captain's journal if one is assigned, else defaulting
   row 1 to in-progress.
6. Clicking Auto Pilot flips its own label between "Auto Pilot" and
   "Stop" and disables/enables Edit accordingly; it has no other effect.
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
12. Assigning Captain resets the route, then replays that Captain's own
    fleet carrier's journal history into it — including catching up rows
    that have no event of their own — using the freshest evidence of a
    real, deliberate jump rather than a passive session-start snapshot.
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
