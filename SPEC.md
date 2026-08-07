# RouteJumper — Application Specification

**Version:** 1.7
**Status:** Route tab implemented (2s cadence, combined completion step);
Material Design restyle complete, including per-status action icons and a
window/taskbar icon; former Control tab renamed to **Roles** (Elite Dangerous
instance detection, including current location and fleet carrier tracking,
implemented); a new, currently-empty **Controls** tab added after it; tab
headings moved to the left edge of the window to save vertical space;
startup window placement on the rightmost monitor implemented
**Platform:** Windows desktop, WPF, .NET 8, MVVM

---

## 1. Purpose

RouteJumper is a desktop utility for entering a list of "systems" (one per
line), then running an automated, timed sequence that visually walks through
each system in turn — plotting a route, jumping, and cooling down — before
moving to the next.

---

## 2. Scope

| In scope (v1) | Out of scope (v1) |
|---|---|
| Main window with three tabs: **Route**, **Roles**, **Controls** | Persistence of route lists between sessions |
| Text entry, save-to-table workflow on the Route tab | Configurable timing (fixed at 2s for v1.3+) |
| Timed Start/Stop sequence over table rows | Non-timer triggers actually wired in (framework only) |
| Event-driven sequencing engine, open to future trigger types | Sending input (keystrokes etc.) to a detected instance's window |
| Roles tab: detect running Elite Dangerous instances and show FID, commander name, cargo, current location, fleet carrier, journal file, window handle/position, and monitor per instance (see §11) | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Startup window placement on the rightmost monitor (see §3.5) | Content of the Controls tab (currently an empty placeholder — see §3.4) |
| Tab headings placed on the left edge of the window (see §3.1) | Row-addressable sequencing triggers (see §13.1) |

---

## 3. Window Structure

### 3.1 Main Window
- Single top-level window containing a `TabControl` with exactly three tabs.
- Tab order: **Route**, **Roles**, **Controls**.
- Tab headings are placed on the **left edge** of the window
  (`TabStripPlacement="Left"`) rather than across the top, to maximise the
  vertical space available to each tab's content.
- Window is resizable; content should reflow (no fixed-pixel layouts).
- Window title: **"ED:FC AutoPilot"**.
- Window/taskbar icon: see §9.5.

### 3.2 Tab 1 — "Route"
Has two mutually exclusive states, **Edit** and **Running**, described below.
Only one is visible at a time.

### 3.3 Tab 2 — "Roles"
> Formerly named "Control"; renamed to "Roles" to reflect its actual purpose
> — surfacing running instances so a role (e.g. which carrier/ship to jump)
> can be assigned to each one. Its implementation and requirements (§11) are
> unchanged by the rename.

- A **Refresh** button, right-aligned below the content area (same
  placement convention as the Route tab's buttons).
- A vertically-stacked, scrollable list of cards, one per running
  `EliteDangerous64.exe` process found, each occupying 75% of the tab's
  width. See §11 for what a card shows and how the data is derived.
- If no instances are found, the card list is replaced with a centered,
  italic "No running Elite Dangerous instances found." message.
- The list is refreshed automatically once when the tab's ViewModel is
  constructed (i.e. on app startup, without requiring the user to click
  Refresh first) and again on every Refresh click.
- Refresh runs its scan on a background thread; the button disables itself
  for the duration of a scan and re-enables once it completes. The rest of
  the app (including the Route tab) must remain usable while a scan is in
  progress.
- Must not error or block the rest of the app if Elite Dangerous isn't
  running, or if the journal folder doesn't exist.

### 3.4 Tab 3 — "Controls"
- New tab added after **Roles**. Currently an empty placeholder (no
  content, no behaviour) — scope and design not yet defined.

### 3.5 Startup window placement
- On launch, the main window is positioned against the **right edge** of
  the physically rightmost connected monitor (10px margin from the edge),
  vertically centered on that monitor.
- Rationale: on a multi-monitor setup, keeps the app out of the way of
  other work (e.g. an IDE) on the left/middle monitors, rather than
  appearing on whichever monitor Windows considers primary/default.
- The rightmost monitor is determined dynamically each launch (by
  enumerating all monitors and picking the one with the greatest `Left`
  screen coordinate) rather than a hardcoded device name, so this still
  works correctly if a monitor is unplugged/replugged and Windows
  reassigns device identifiers. On a single-monitor system this just
  places the window against that monitor's right edge.
- Positioning happens at `SourceInitialized` (the HWND exists but the
  window has not yet been shown/painted), so there is no visible jump on
  launch. Monitor bounds (physical pixels) are converted to WPF
  device-independent units via the window's own composition-target
  transform, so this is correct regardless of per-monitor DPI scaling.

---

## 4. Route Tab — Edit State (default / initial state)

### 4.1 Layout
- A single text box fills the available space of the tab (grows/shrinks
  with the window).
- Below the text box, right-aligned: two buttons, **Save** and **Cancel**,
  in that order.

### 4.2 Text box behaviour
- Multi-line, free text entry.
- Accepts Enter (new lines) and Tab characters as literal input (not focus
  navigation).
- No line count or character limit imposed by the UI.
- Each line of text, once saved, represents exactly one row of the
  subsequent table (see §6.1 for blank-line handling).

### 4.3 Save button
- **Enabled** only when the text box contains at least one non-whitespace
  character.
- **On click:**
  1. Split the text box contents into lines.
  2. Build one table row per line (see §6.1).
  3. Transition the Route tab from **Edit** state to **Running** state
     (§5).
- Does not clear or otherwise alter the text box contents (so the user can
  switch back if a "back to edit" feature is added later).

### 4.4 Cancel button
- **Always enabled.**
- **On click:** clears all text from the text box. Does not affect any
  existing table/rows if one already exists from a previous save.

---

## 5. Route Tab — Running State (after Save)

### 5.1 Layout
- A table (grid) fills the available space of the tab.
- Below the table, right-aligned: two buttons, **Start** and **Stop**, in
  that order.

### 5.2 Table columns

| # | Header | Content | Notes |
|---|--------|---------|-------|
| 1 | *(blank)* | Icon | See §7.3 for icon states |
| 2 | `#` | Row number | Sequential integer, starting at 1, in save order |
| 3 | `System` | Line text | Verbatim text of the corresponding input line |
| 4 | `Status` | Status text | See §7 for values written during the sequence |

- Table is read-only from the user's perspective (no in-grid editing).
- Row count equals the number of lines saved from the text box.

### 5.3 Start button
- **Enabled** when: the table has at least one row **and** the sequence is
  not currently running.
- **On click:** begins the automated sequence described in §7, starting
  from row 1.
- Disabled for the duration of a running sequence.

### 5.4 Stop button
- **Enabled** only while the sequence is running.
- **On click:** immediately halts further progression of the sequence.
  Whatever icon/status values are currently displayed remain as-is (no
  rollback).
- After Stop, Start becomes available again and — if pressed — begins a
  **new** full run from row 1 (v1 does not support resuming mid-sequence).

---

## 6. Data Rules

### 6.1 Line-to-row conversion (on Save)
- Text is split on line breaks.
- A single trailing blank line (caused by a final newline character) is
  discarded and does **not** become a row.
- All other lines — including interior blank lines — become rows, in
  order, numbered from 1.
- `System` column = the line's text exactly as entered (no trimming of
  internal whitespace).

### 6.2 Row identity
- Row `#` values are fixed once assigned at Save time and do not change
  during a run.

---

## 7. Automated Sequence (Start → Stop)

### 7.1 Trigger cadence
- Each action below is separated from the next by a **2 second** interval
  in v1.3 (reverted from the 0.5s used in v1.2 — 0.5s made the per-step
  transitions hard to actually see, and the real-world use case is jumping
  **fleet carriers**, not ships: fleet carrier jumps are slow in practice,
  so a snappy cadence undersells what the app is modeling. Interim history:
  0.2s during testing, then 0.5s in v1.2, originally 2s before that).
- The sequence must be built so that **advancing to the next action is a
  response to an event**, not a hardcoded delay call, so that the pacing
  mechanism (today: a timer) can later be replaced or supplemented by other
  event sources (e.g. a manual "step" trigger, an external signal) without
  changing the action logic itself. See §10 for the architectural
  requirement this implies.

### 7.2 Action plan
On **Start**:

1. Immediately: show the "in-progress" icon (§7.3) in row 1's icon column.
2. Then, once per 2-second interval, for the **current row**, in order:
   1. `Status` = `Plotting`
   2. `Status` = `Plotted`
   3. `Status` = `Jumping`
   4. **Combined step** (executed together, as a single trigger-driven
      action, not three separate steps):
      - Icon changes from "in-progress" to "complete" (§7.3)
      - If a next row exists, show the "in-progress" icon in that row's
        icon column
      - `Status` = `Cooldown`
   5. `Status` = *(cleared / empty string)*
3. Repeat step 2 for each subsequent row, in row order, until the last row
   has completed step 2.5.
4. The sequence then stops automatically (equivalent to Stop being
   pressed); Start becomes available again.

### 7.3 Icon states (blank-headed column)

The icon shown depends on **both** the row's icon state (None / In-progress /
Complete) and, while In-progress, the row's current `Status` text — the two
are no longer independent. See §9.2 for the implementation (a
multi-value converter keyed on `Icon` + `Status`).

| Icon state | `Status` | Glyph (`PackIconKind`) | When shown |
|---|---|---|---|
| None | — | *(hidden)* | Default, and after a row's cycle content is otherwise done (icon is **not** cleared — see note) |
| In-progress | *(blank, just before first tick)* | `Play` (triangle) | The instant a row becomes "current" (step 7.2.1), before its first `Status` update |
| In-progress | `Plotting` | `Compass` | During step 7.2.2.1 |
| In-progress | `Plotted` | `Hourglass` | During step 7.2.2.2 |
| In-progress | `Jumping` | `RocketLaunch` | During step 7.2.2.3 |
| In-progress | `Cooldown` | `Play` (triangle) | During step 7.2.2.4, until the icon flips to Complete within that same combined step |
| Complete | *(cleared)* | `Check` (tick) | From step 7.2.4 onward, permanently (for that run) |

- All icon glyphs are rendered via the toolkit's `PackIcon` control, colored
  from the active Material palette (`MaterialDesign.Brush.Secondary` — see
  §9.2), not per-icon colors.
- `Play` is also the fallback for any in-progress status not explicitly
  listed above, so a new status value added later degrades gracefully
  instead of showing nothing.

> Note: the spec does not require clearing the icon at any point — once a
> row reaches "Complete" it keeps the tick for the remainder of the run.

### 7.4 Timing summary (example, 3 rows)

```
t=0s   Row1 icon -> Play (in-progress, no status yet)
t=2s   Row1 status -> Plotting   (icon -> Compass)
t=4s   Row1 status -> Plotted    (icon -> Hourglass)
t=6s   Row1 status -> Jumping    (icon -> RocketLaunch)
t=8s   Row1 icon -> Check (complete), Row2 icon -> Play (in-progress), Row1 status -> Cooldown  (combined)
t=10s  Row1 status -> (cleared)
t=12s  Row2 status -> Plotting   (icon -> Compass)
...
```

---

## 8. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (net8.0-windows), MVVM pattern throughout —
  no business logic in code-behind.
- **Responsiveness:** UI must remain interactive throughout a run (Stop
  must be clickable at any point).
- **Extensibility:** Adding a new trigger type or a new action step must
  not require changes to the ViewModel or View layers (see §10).
- **No external dependencies** beyond the .NET/WPF base class libraries.

---

## 9. Styling — Material Design

**Decision:** restyle the application using
[MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
(NuGet packages `MaterialDesignThemes` + `MaterialDesignColors`), to move
away from the default Windows control look.

### 9.1 Setup requirements
- `App.xaml` must merge a `materialDesign:BundledTheme` (with chosen
  `PrimaryColor`/`SecondaryColor`) plus the matching
  `MaterialDesignX.Defaults.xaml` resource dictionary (`X` = `2` or `3` —
  pick one and use it consistently; do not mix).
  > **Update (implemented with MaterialDesignThemes 5.3.2):** the installed
  > package only ships `MaterialDesign2.Defaults.xaml` as a complete theme
  > dictionary. The `MaterialDesign3.*.xaml` files present in this version
  > are per-control additions for newer nav-pattern components
  > (NavigationBar/Rail/Drawer), not a full alternate theme — there is no
  > `MaterialDesign3.Defaults.xaml` to choose. `X = 2` was used; this note
  > supersedes the "pick one" framing above unless a future package version
  > reintroduces a real MD3 defaults dictionary.
- `MainWindow` must use the `MaterialDesignWindow` style (or equivalent) so
  the title bar/chrome is themed, not just the controls inside it.

### 9.2 Icon column
- Replace the current plain-text glyph rendering (`IconToGlyphConverter`
  outputting `▶`/`✔` with a hardcoded `Foreground="Green"`) with the
  toolkit's `PackIcon` control, using an icon-kind converter instead of a
  string-glyph converter.
- The color used for all icon states must be drawn from the active
  Material palette (e.g. a success/secondary brush), not a hardcoded
  `Green`, so it stays coherent if the palette changes later.
  > **Update (implemented with MaterialDesignThemes 5.3.2):** the intuitive
  > `SecondaryHueMidBrush`/`PrimaryHueMidBrush` resource names (used in
  > older toolkit versions and most third-party examples) are not
  > registered by `BundledTheme` in this package version and silently
  > resolve to nothing (falling back to inherited black text). The correct
  > live keys in 5.3.2 are under a `MaterialDesign.Brush.*` namespace —
  > `MaterialDesign.Brush.Secondary` was used here. Confirmed by dumping
  > `Application.Resources` (including merged dictionaries) at startup;
  > verify against the actual package version before reusing this key
  > elsewhere, since it may change again in future releases.
  > **Update:** the icon glyph now also varies by `Status` while
  > In-progress (see §7.3), not just by icon state, so a single-value
  > `IValueConverter` keyed on `Icon` alone is no longer sufficient.
  > `IconToPackIconKindConverter` was replaced by
  > `IconStatusToPackIconKindConverter`, an `IMultiValueConverter` bound via
  > `MultiBinding` to both `Icon` and `Status`. Visibility (hiding the icon
  > for state `None`) remains a separate single-value converter
  > (`RowIconToVisibilityConverter`) bound on `Icon` alone, since visibility
  > doesn't depend on `Status`.
  > Icon kinds used: `Play` (in-progress default), `Compass` (Plotting),
  > `Hourglass` (Plotted), `RocketLaunch` (Jumping), `Check` (Complete).
  > All confirmed to exist in MaterialDesignThemes 5.3.2 before use — verify
  > against the installed package version before adding more, since
  > `PackIconKind` names are not guaranteed stable across releases. Note
  > from exploring candidates: the `Satellite` kind renders as a
  > broken-image placeholder rather than a glyph in this package version
  > (`SatelliteVariant` renders correctly) — a reminder that a name existing
  > in the enum doesn't guarantee it renders correctly; visually confirm any
  > new kind before relying on it.

### 9.3 DataGrid
- The toolkit ships its own DataGrid styling; the existing
  `DataGridTemplateColumn` (icon), `#`, `System`, and `Status` columns must
  be visually verified after the restyle — Material's header/row/selection
  chrome is more opinionated than the default WPF DataGrid style.

### 9.4 Non-goals for this pass
- No dark/light theme toggle in v1.1 (pick one `BaseTheme` and ship it).
- No change to any ViewModel, command, or sequencing logic — this is a
  view-layer-only change.

### 9.5 Application / window icon
- The window/taskbar/exe icon is the `RocketLaunch` `PackIconKind` glyph
  (same family and secondary-palette color as the row icons, for visual
  consistency), exported to `Resources/AppIcon.ico` containing 16, 32, 48,
  and 256px frames.
- `AppIcon.ico` was generated by actually rendering the toolkit's real
  `PackIcon` (`Kind="RocketLaunch"`) at each target size via
  `RenderTargetBitmap` (a one-off, temporary `OnStartup` routine, removed
  after the file was produced) rather than hand-drawing or guessing SVG
  path data — this keeps the taskbar icon pixel-consistent with the glyph
  used inside the app.
- Wired via two places, both required:
  - `<ApplicationIcon>Resources\AppIcon.ico</ApplicationIcon>` in
    `RouteJumper.csproj` (the exe's embedded Win32 icon resource — used by
    Explorer, pinned shortcuts, Alt+Tab).
  - `Icon="Resources/AppIcon.ico"` on `MainWindow`, with the file also
    included as `<Resource Include="Resources\AppIcon.ico" />` in the
    csproj so the relative path resolves (the titlebar/taskbar icon at
    runtime does **not** automatically fall back to `ApplicationIcon` if
    `Window.Icon` is unset in a way that resolves — both must point at the
    same file).

---

## 10. Architectural Requirement — Event-Driven Sequencing

This is a hard requirement, not an implementation detail: the mechanism
that decides *when* the next action in §7.2 runs must be decoupled from
*what* that action does, via an event.

- A **trigger** is anything capable of raising a "proceed" event (e.g. a
  timer, a button, a message from another module).
- A **sequencer** owns the ordered list of actions (built once, at Start)
  and executes exactly one action per "proceed" event, regardless of
  source.
- Multiple triggers must be attachable to the same sequencer simultaneously.
- v1 ships with one trigger in active use (a timer, currently 2s)
  and the framework for at least one alternative trigger type, to prove
  the decoupling holds — but wiring up additional real-world triggers
  (hardware signals, other UI events, etc.) is future scope. See §13 for
  a planned trigger type that will need this decoupling to hold.

---

## 11. Roles Tab — Elite Dangerous Instance Detection

### 11.1 Process discovery
- On every refresh (initial load or button click), all running processes
  named `EliteDangerous64` are enumerated (`Process.GetProcessesByName`).
- Zero instances found is not an error condition — it's the documented
  empty state (§3.3).

### 11.2 Journal file matching
- There is no OS-level link between a game process and the journal file
  it writes to, so each process has to be matched to its journal file
  independently. Every candidate journal file (`Journal.*.log` in
  `%USERPROFILE%\Saved Games\Frontier Developments\Elite Dangerous`) is
  timestamped via its own `Fileheader` event.
- Each process is matched to the journal whose `Fileheader` timestamp is
  closest to that process's `StartTime`, within a 5-minute tolerance.
  Matching is greedy and one-to-one (a journal already assigned to one
  process in this scan cannot also be assigned to another), so this stays
  correct with multiple simultaneous instances (multi-boxing) — verified
  live with two concurrent instances, including after restarting one of
  them mid-session (new PID, new journal file, correctly re-matched with
  no stale data from the old process).
- A process with no journal match within tolerance shows "Not found" for
  the journal filename and "Unknown" for every journal-derived field
  below.
- **Do not** use the live `Status.json`/`Cargo.json` files Frontier writes
  alongside the journals — those are single files per Elite Dangerous
  *installation*, not per running instance, so when two or more instances
  are running there is no way to tell which instance's state a given read
  of those files reflects. Everything instance-specific must come from
  that instance's own uniquely-named journal file.

### 11.3 Per-instance data shown

| Field | Source | Notes |
|---|---|---|
| Commander name | Latest `Commander` event's `Name` | |
| FID | Latest `Commander` event's `FID` | |
| Cargo | Latest `Loadout` event's `CargoCapacity` (max) + latest `Cargo` event with `"Vessel":"Ship"`'s `Count` (current) | Shown as `current / capacity` tonnes. The `Cargo` event fires on essentially every real cargo change (market trades, mining, limpets) and not only on docking — confirmed live by adding cargo mid-session and seeing it reflected on the next Refresh — so the journal alone is sufficient without needing `Cargo.json`. Only total tonnage is tracked, not an itemized commodity breakdown (see §2). |
| Current location | Latest of `Location`, `Docked`, `Undocked`, `FSDJump`, `CarrierJump` (while docked) — `StarSystem` (+ `StationName` if docked) | Shown as `System — Station`, or just `System` if not docked. `Undocked`/`FSDJump` clear the station (no `StarSystem` on `Undocked`, so the tracked system is left alone; `FSDJump` always means undocked). |
| Fleet carrier | Name from `CarrierStats`' `Name`/`CarrierID`; location resolved against that `CarrierID` from `CarrierJump`/`CarrierLocation` (+ `Body`, from `CarrierJump` only) | See both caveats below — the name is commonly unavailable even for an owner, and location resolution must be ID-matched, not just "latest event of the right type". |
| Journal file | The matched journal's filename | Filename only, not the full path (keeps the card compact) |
| Window handle | `Process.MainWindowHandle` | Displayed as hex (`0xNNNN`); the raw `IntPtr` is also kept on the view model (not just the display string) for future use — e.g. sending keystrokes to a specific instance's window |
| Window position | `GetWindowRect` on the window handle | Screen coordinates + size |
| Monitor | `MonitorFromWindow` + `GetMonitorInfo` | Device name, primary flag, resolution, position |

- All of the above (Commander/FID/cargo/location/carrier) are found in a
  **single sequential pass** over the journal file, taking the **latest**
  occurrence of each relevant event — not one pass per field. Most of
  these fields genuinely change during a session (cargo, location, carrier
  position), so "first occurrence found" would go stale; Commander/FID
  don't change in practice but are read the same way for one uniform
  implementation rather than a special case. Event lines are dispatched by
  extracting the `"event":"…"` value directly (cheaper than a full JSON
  parse) before deciding whether to parse the rest of the line at all.
- If a process has no main window yet (e.g. still on the loading/splash
  screen) or exits mid-scan, the window/monitor fields degrade to
  "Unknown" rather than failing the whole scan for that instance.
- **Fleet carrier name caveat:** Frontier only logs `CarrierStats` (which
  carries the carrier's player-chosen `Name`) when the commander opens the
  in-game Carrier Management panel — it is *not* written automatically at
  session start the way `Loadout`/`Cargo` are. So a commander can genuinely
  own a carrier and still show "Unnamed carrier" (or "None detected this
  session" if no carrier-related event has fired at all) if they haven't
  opened that panel yet this session — confirmed live, both real test
  commanders showed "Unnamed carrier" despite owning one, because
  `CarrierLocation`/`CarrierJump` events were present (giving a location)
  but `CarrierStats` was not. This is a real journal limitation, not a
  bug, and is not worth working around by reading older journal files
  (adds cross-session complexity/staleness risk for a cosmetic gap — see
  §13).
  > **Update (bug found and fixed live):** the first implementation took
  > whichever `CarrierLocation`/`CarrierJump` event was simply *latest in
  > the file*, regardless of which carrier it described — and it turns out
  > a commander's journal can contain these events for carriers they don't
  > own. Specifically, `CarrierLocation` carries a `CarrierType` field
  > (`"FleetCarrier"` for one's own vs. `"SquadronCarrier"` for a shared
  > squadron carrier), and both test commanders — being in the same
  > in-game squadron — had `SquadronCarrier` pings for the *same* shared
  > carrier interleaved with their own `FleetCarrier` pings. Because the
  > squadron ping happened to be timestamped later, both cards wrongly
  > showed the squadron carrier's location as if it were each commander's
  > own. Fixed by: (1) only ever recording `CarrierLocation` entries whose
  > `CarrierType` is `"FleetCarrier"`; (2) tracking all seen
  > `CarrierJump`/`CarrierLocation` entries in a map keyed by `CarrierID`
  > (from `MarketID` on `CarrierJump`) rather than one scalar "latest"
  > value; (3) after the full pass, resolving the displayed location by
  > looking up the confirmed-owned `CarrierID` (from `CarrierStats`) in
  > that map — or, if `CarrierStats` never fired this session so ownership
  > can't be confirmed by ID, falling back to the map's one entry *only if
  > there is exactly one distinct carrier ID on record* (no ambiguity to
  > resolve); otherwise the location is left unknown rather than guessed.
  > Verified against the real journals: each commander's card now shows
  > their own carrier's actual system, not the shared squadron carrier's.

### 11.4 Refresh behavior
- The scan (process enumeration + journal reads + Win32 window/monitor
  lookups) runs on a background thread so it never blocks the UI thread;
  the rest of the app (including the Route tab) stays interactive during
  a scan.
- The Refresh button disables itself for the duration of a scan and
  re-enables once it completes.
- Triggered once automatically when the Roles tab's ViewModel is
  constructed (i.e. on app startup), and again on every Refresh click.

---

## 12. Acceptance Criteria

1. Launching the app shows a window with **Route**, **Roles**, and
   **Controls** tabs, with headings on the left edge of the window; Route
   is selected by default.
2. Route tab initially shows an empty, full-size text box with disabled
   Save and enabled Cancel.
3. Typing text enables Save; clicking Cancel clears the text box.
4. Entering N non-empty lines and clicking Save produces a table with
   exactly N rows, numbered 1..N, `System` matching the input lines
   verbatim, `Status` empty, icon column empty.
5. Start is disabled until Save has produced at least one row, and while a
   sequence is running.
6. Clicking Start immediately shows the in-progress triangle on row 1.
7. Every 2 seconds thereafter, exactly one action from §7.2 occurs (the
   combined tick/next-triangle/Cooldown step counts as one), in the
   specified order, until all rows have completed.
8. Row 2's triangle appears at the point specified in §7.2 (within the
   combined step of row 1's cycle), not before.
9. Clicking Stop at any time halts further changes; Start re-enables and,
   if clicked again, restarts the full sequence from row 1.
10. After the last row completes step 7.2.5, the sequence stops itself
    without requiring Stop to be clicked, and Start re-enables.
11. Launching the app (or clicking Refresh) with zero `EliteDangerous64.exe`
    processes running shows the "No running Elite Dangerous instances
    found." message and no cards.
12. Launching the app (or clicking Refresh) with N `EliteDangerous64.exe`
    processes running shows exactly N cards, each showing that instance's
    own FID, commander name, cargo, current location, fleet carrier info,
    journal filename, window handle, window position, and monitor info —
    correctly attributed even with multiple instances running
    simultaneously.
13. Restarting one Elite Dangerous instance (new process, new journal
    file) and clicking Refresh updates that instance's card to the new
    journal/window handle without showing stale data from the previous
    process, and without disturbing other instances' cards.
14. Moving one instance's window to a different monitor and clicking
    Refresh updates that instance's window position and monitor fields
    accordingly.
15. The Roles tab's initial data load happens automatically on app
    startup, without requiring the user to click Refresh first.
16. On launch, the main window is positioned against the right edge of
    the physically rightmost monitor, vertically centered (§3.5).
17. A commander who owns a fleet carrier but has not opened Carrier
    Management this session shows "Unnamed carrier — {system}" (not
    "None detected" — the location events alone are enough to know a
    carrier exists) if any `CarrierJump`/`CarrierLocation` event is
    present; a commander with no carrier-related events at all shows
    "None detected this session".
18. After the Material Design restyle, all of the above still hold —
    styling must not change any behavior, only appearance.

---

## 13. Open Questions / Future Considerations

- Should there be a way to return from the table view to the text box
  (e.g. an "Edit" button) without restarting the app?
- Should the 2 second interval be user-configurable?
- Should Stop support pausing/resuming mid-row, rather than only a full
  restart?
- Should the Roles tab support sending input (keystrokes etc.) to a
  selected instance's window? The window handle is already captured
  (§11.3) specifically in anticipation of this.
- What should the new Controls tab (§3.4) actually contain?
- Should the 5-minute journal-match tolerance (§11.2) be configurable?
- Should cargo show an itemized commodity breakdown, not just total
  tonnage?
- Should the fleet carrier name fall back to scanning older journal files
  when `CarrierStats` hasn't fired in the current session (§11.3)? Carrier
  ownership/name rarely changes, so a historical lookup would likely still
  be accurate — but it adds cross-file complexity and a "how far back do
  we look" judgment call that felt premature to build without a concrete
  need.

### 13.1 Planned: row-addressable triggers (not yet scheduled)

A future trigger type is expected to identify **which row** it applies to
(e.g. an external event that says "row 4 just finished jumping"), rather
than always meaning "advance the current row in the queue." The current
`RouteSequencer` design assumes a strictly ordered, pre-built queue of
actions built once at Start — it has no notion of jumping to an arbitrary
row out of order.

When this is implemented, expect it to require:
- A new trigger/event shape that carries a row identifier (or the
  `SequenceStep` itself becoming addressable per row rather than a flat
  queue).
- Rethinking `RouteSequencer.BuildSteps`/`RunNextStepImmediately`, which
  currently assume "next queued step" == "next required action" — that
  assumption breaks if a trigger can name an arbitrary row.
- Deciding what happens if a row-specific event arrives for a row that
  isn't "current" (e.g. out-of-order completion, or two rows in flight at
  once) — the v1 model has no answer for this today.

This is **not** in scope until the triggering events themselves are
defined — flagged here so the current sequencer isn't assumed to survive
that change unmodified.