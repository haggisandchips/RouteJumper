# RouteJumper — Application Specification

**Version:** 2.9
**Status:** Spec consistency pass - closed out stale §13 open questions and
§2/§10 scope-table entries that earlier "Update" notes had already
superseded (the Edit button and row-addressable/non-timer-trigger items),
so the base requirement text matches what's actually implemented instead
of only the annotations; no functional code changes.
Edit button added to return from the table to the text box
(§5.5) - every Save now produces a fresh table, re-derived from the
currently-assigned Captain's journal if one is assigned, else defaulting
row 1 to "next"; row icon no longer visibly resizes the row as it
appears/disappears (§9.2); context menu vertical padding halved again per
feedback; Route tab implemented (2s cadence, combined completion step);
clicking anywhere in a row copies its System text to the clipboard and
plays a ping, with no leftover per-cell focus border and the grey row-
selection background kept; Save now trims each line and drops all blank
lines (not just a trailing one), and the empty text box shows placeholder
text and receives focus automatically on launch; Engineer role now also
disabled when cargo capacity is unknown, not just when known to be zero;
fixed a serious live-found bug (§11.5) where Captain assignment could mark
an entire freshly-typed route Complete based only on the session's passive
startup carrier-location snapshot, not actual jump evidence - then found
and fixed a regression from that same fix (multi-session journeys with no
jump requested yet in the new session); manual "Set next system" right-
click override added (§11.6) for whenever automatic detection still gets
it wrong or the carrier is genuinely off-route;
Material Design restyle complete, including per-status action icons and a
window/taskbar icon; former Control tab renamed to **Roles** (Elite Dangerous
instance detection, including current location and fleet carrier tracking,
implemented); a new, currently-empty **Controls** tab added after it; tab
headings moved to the left edge of the window to save vertical space;
startup window placement on the rightmost monitor implemented; row-addressable
events implemented (§13.1), and Captain/Engineer role assignment (§11.5) live-
updates the route from a commander's own journal, verified against a real
running instance, including the derived `Jumping`/Cooldown-clear timing
(§11.5) verified both against real historical journal data and a synthetic
real-time scheduling test; route reset-on-Captain-(re)assignment vs.
leave-as-is-on-clear (§11.5) verified live against two real concurrent
instances; a startup-`CarrierLocation`-misread-as-fresh-arrival bug (§11.5)
found via that same two-instance test, fixed, and reverified
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
| Text entry, save-to-table workflow on the Route tab, with an Edit button to return to it (see §5.5) | Configurable timing (fixed at 2s for v1.3+) |
| Timed Start/Stop sequence over table rows | Sending input (keystrokes etc.) to a detected instance's window |
| Event-driven sequencing engine, open to future trigger types | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Roles tab: detect running Elite Dangerous instances and show FID, commander name, cargo, current location, fleet carrier, journal file, window handle/position, and monitor per instance (see §11) | Content of the Controls tab (currently an empty placeholder — see §3.4) |
| Startup window placement on the rightmost monitor (see §3.5) | Configurable 5-minute cooldown / journal-match tolerance (see §11.2/§11.5) |
| Tab headings placed on the left edge of the window (see §3.1) | Sending input to arbitrary system names not present in the route (§11.6's manual override targets existing rows only) |
| Captain/Engineer role assignment (§11.5), with a real, non-timer trigger (a Captain's journal - §10's Update) wired all the way into the row-addressable sequencing machinery anticipated by §13.1 | |
| Manual "Set next system" override for when automatic journal-based detection needs correcting (§11.6) | |

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
  subsequent table (see §6.1 for blank-line handling and trimming).
- When empty, shows placeholder text: "Paste route here, one system per
  line." (`materialDesign:HintAssist.Hint`, non-floating - it disappears
  once text is entered rather than shrinking into a caption, since a
  persistent floating label reads oddly above a large paste area).
- Receives keyboard focus automatically on app launch, so the user can
  start typing/pasting immediately without clicking into it first.

  > **Implementation note:** the declarative WPF idiom for this
  > (`FocusManager.FocusedElement="{Binding ElementName=...}"` on the
  > containing element) was tried first and did **not** work - confirmed
  > live via UI Automation (`HasKeyboardFocus` stayed on the window, not
  > the box) - because a plain `UserControl` nested inside a `TabControl`
  > isn't its own focus scope, which that property depends on. Fixed
  > imperatively instead, in `RouteView.xaml.cs`'s constructor:
  > `RouteTextBox.Loaded += (_, _) => Keyboard.Focus(RouteTextBox);` - the
  > standard reliable pattern for this, and pure view-layer behavior (no
  > business logic), the same carve-out `MainWindow.xaml.cs`'s startup
  > window placement already relies on. Verified live (`HasKeyboardFocus`
  > `True`, and the Material text field's focus-indicator underline
  > visibly switched from grey to the accent color) immediately on
  > launch, with nothing clicked.

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

  > **Update: that "back to edit" feature is now implemented** - see §5.5.
  > Every Save (first time, or after Edit) also now sets row 1's icon to
  > in-progress by default, and - if a Captain is currently assigned -
  > triggers a fresh re-derivation of the whole table from that Captain's
  > journal. See §5.5 for both, since they're really properties of *every*
  > Save, not just the first one.

### 4.4 Cancel button
- **Always enabled.**
- **On click:** clears all text from the text box. Does not affect any
  existing table/rows if one already exists from a previous save.

---

## 5. Route Tab — Running State (after Save)

### 5.1 Layout
- A table (grid) fills the available space of the tab.
- Below the table, right-aligned: three buttons, **Edit**, **Start**, and
  **Stop**, in that order (see §5.5 for Edit).

### 5.2 Table columns

| # | Header | Content | Notes |
|---|--------|---------|-------|
| 1 | *(blank)* | Icon | See §7.3 for icon states |
| 2 | `#` | Row number | Sequential integer, starting at 1, in save order |
| 3 | `System` | Line text | Verbatim text of the corresponding input line |
| 4 | `Status` | Status text | See §7 for values written during the sequence |

- Table is read-only from the user's perspective (no in-grid editing).
- Row count equals the number of lines saved from the text box.
- Clicking anywhere in a row copies that row's system name to the
  clipboard and plays a confirmation ping. The row's grey selection
  background is kept; the DataGrid's usual per-cell "current cell" border
  is suppressed (see Update).

  > **Update (implemented, `RouteViewModel.CopySystemCommand`):** went
  > through three iterations before landing here, each fixing a problem
  > the previous one surfaced live:
  > 1. A `DataGridTemplateColumn` `Button` (`ControlTemplate` replaced
  >    with a bare `TextBlock`) still picked up the implicit MaterialDesign
  >    `Button` style's other property Setters (`FontWeight`, sizing) -
  >    `Style`/`Template` are independent in WPF, so replacing the
  >    template doesn't stop the style's Setters applying. Confirmed live:
  >    bolded the `System` text and threw off row alignment against the
  >    plain-`TextBlock` `#`/`Status` columns (zoomed screenshot
  >    comparison).
  > 2. Switched to a bare `TextBlock` with a `MouseBinding` (the same
  >    element `DataGridTextColumn` renders internally for the other
  >    columns, so nothing to leak from) - fixed the alignment, confirmed
  >    live pixel-aligned against `#`. But it only made the `System` text
  >    itself clickable, not the rest of the row.
  > 3. Per the user's follow-up ("clicking on any part of the row should
  >    copy"), moved the click handling from a per-cell template to
  >    `DataGrid.RowStyle`. `DataGridRow` has no `Command` property to bind
  >    to, so `Behaviors/ClickCommandBehavior.cs` was added - a small
  >    attached-property behavior (`Command`/`CommandParameter`, wiring
  >    `PreviewMouseLeftButtonUp` to `ICommand.Execute`) in the same
  >    view-layer-glue spirit as `Converters/` - and the `System` column
  >    reverted back to a plain `DataGridTextColumn` (no per-cell template
  >    needed any more, which incidentally also removes any future risk of
  >    alignment drift between it and `#`/`Status`, since it's the
  >    identical element type again).
  > A later "remove the black outline, keep the grey background" request
  > turned out to be a separate mechanism from what the outline looked
  > like it should be: setting `DataGridCell.FocusVisualStyle="{x:Null}"`
  > (the adorner-layer focus rectangle) had **no effect** - confirmed live,
  > the border was still there after that change alone. It's actually
  > coming from `Style.Triggers` inside the toolkit's own
  > `MaterialDesignDataGridCell` style (`IsFocused`/`IsSelected`/
  > `IsKeyboardFocusWithin` setting `BorderThickness`/`BorderBrush` on the
  > cell template) - a `BasedOn` style's own `Setter`s can't outrank a base
  > style's `Trigger`s (Style Triggers are a higher WPF precedence tier
  > than plain Style Setters), so the fix adds matching `Trigger`s of its
  > own for the same three conditions, each just forcing
  > `BorderThickness="0"` - a `BasedOn` style's own Triggers *are* applied
  > after the inherited ones and do take precedence. The grey row
  > background is a separate, row-level trigger (`RowStyle`) untouched by
  > any of this - confirmed live, still shows correctly on the selected
  > row after the fix.
  > Uses `SystemSounds.Asterisk.Play()` for the ping (a built-in Windows
  > sound, no bundled audio asset, keeping with §8's no-external-
  > dependencies rule) and `System.Windows.Clipboard.SetText`.

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

### 5.5 Edit button

- **Enabled** when: the table is showing (i.e. **Running** state) **and**
  the timer-paced sequence is not currently running (same guard as
  Start).
- **On click:** returns to **Edit** state (§4), showing the text box
  again with its contents exactly as they were left (Save never alters
  them - §4.3) - so the user can revise the pasted route without
  retyping it from scratch.
- Re-clicking Save afterward always produces a **completely fresh**
  table, discarding whatever icon/status state the previous table had -
  even if the text is byte-for-byte identical to what was there before,
  and regardless of whether that old state came from a manual Start/Stop
  run or a Captain's journal replay:
  - Row 1's icon defaults to in-progress (the "next" triangle) and every
    other row starts blank, **unless** a Captain is currently assigned
    (§11.5), in which case the table is immediately re-derived from
    scratch from that Captain's journal history instead - exactly as if
    Captain had just been (re)assigned. If no Captain is assigned, the
    row-1-defaults-to-next state is left standing.
  - This applies identically to the very first Save, not just ones after
    Edit - "before Captain is initially assigned, the first row should
    be marked as next" per the request that added this.

  > **Implementation:** `RouteViewModel` has no reference to
  > `RolesViewModel` (or vice versa) - `RouteViewModel.Save()` raises a
  > `RouteSaved` event, and `MainViewModel` (the only place that
  > references both tab ViewModels) wires it to
  > `RolesViewModel.RefreshRouteForCurrentCaptain()`, which is a no-op if
  > no Captain is assigned, or restarts that Captain's journal watch
  > (`StartCaptainWatch`) if one is - the exact same replay mechanism
  > §11.5 already uses for a fresh assignment, just re-triggered.
  > `RefreshRouteForCurrentCaptain` doesn't need to fire an explicit
  > `RowEventKind.Reset` first, unlike `ToggleCaptain`'s assign branch -
  > `Save()` always builds entirely new `RouteRowViewModel` instances, so
  > there's no leftover state on them to clear.
  > **Verified live:** Saved a route with no Captain assigned (row 1
  > defaulted to next); clicked Edit (text box reappeared with the route
  > intact, focus returned to it); re-Saved the identical text (fresh
  > table, row 1 defaulted to next again, not carrying anything over).
  > Then assigned Captain (route correctly replayed: rows 1-3 Complete,
  > row 4 in-progress, against real journal data); clicked Edit and
  > re-Saved with Captain still assigned - the table came back showing
  > the identical real progress (rows 1-3 Complete, row 4 in-progress)
  > rather than the no-Captain default, confirming the re-derivation
  > path.

---

## 6. Data Rules

### 6.1 Line-to-row conversion (on Save)
- Text is split on line breaks.
- Each line is trimmed of leading/trailing whitespace.
- Blank lines (empty, or whitespace-only before trimming) are discarded
  entirely and do **not** become rows - this includes a trailing blank
  line from a final newline, but also any interior or leading blank
  lines, since pasted route lists commonly have stray blank lines mixed
  in.
- Every remaining line becomes a row, in order, numbered from 1 (i.e. row
  numbers count kept lines, not original line positions - a route with a
  blank line 2 in the input has its second kept line as row 2, not row 3).
- `System` column = the line's text after trimming (internal whitespace,
  if any, is preserved as entered).

  > **Update (implemented):** originally only a single trailing blank line
  > was dropped and internal whitespace was preserved verbatim (interior
  > blank lines became empty rows, and leading/trailing whitespace within
  > a line was kept). Changed to trim + drop-all-blank-lines since pasted
  > route lists (the primary way this text box is actually used - see
  > `materialDesign:HintAssist.Hint` below) routinely have both. Verified
  > live: leading/interior/trailing blank lines (including a tab-only
  > line) and leading/trailing spaces around real system names were all
  > handled correctly in one Save.

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
  > **Update (row-resize flicker, fixed):** `RowIconToVisibilityConverter`
  > returned `Visibility.Collapsed` for state `None`, which removes the
  > icon from layout entirely - reported live as "a very slight resizing
  > of rows as the icons are added/removed", since the DataGrid would
  > shrink that row's height while no icon was shown, then grow it back
  > the instant an icon appeared. Changed to `Visibility.Hidden`, which
  > keeps the icon's layout space reserved even while invisible, so row
  > height no longer depends on whether that row currently has an icon.

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
- v1 ships with the timer trigger (2s) driving the demo Start/Stop
  sequence, and a real-world, non-timer trigger — a Captain's own
  journal (§11.5) — driving the row-addressable sequencing machinery
  anticipated by §13.1, now implemented rather than merely a framework
  proving the decoupling holds.

  > **Update:** a second, parallel trigger shape now exists alongside the
  > original `ISequenceTrigger` -
  > `IRowEventTrigger`/`RowEvent`/`RowEventKind` (`Sequencing/`), consumed
  > by `RouteSequencer.AttachRowTrigger`/`SetRows`. This is the "row-
  > addressable trigger" anticipated by §13.1, now implemented for real:
  > a row event names a row by its System text rather than meaning "advance
  > the queued step", and one event can catch several rows up to Complete
  > at once. It is deliberately a *separate* mechanism from the timer-paced
  > `_steps` queue (own `SetRows` target, own handler) rather than a
  > variant of it, since the two have incompatible notions of "next action"
  > - the queue always means position-in-list, a row event always means
  > "whichever not-yet-complete row has this System text". Both can be
  > attached to the same `RouteSequencer` at once without conflict, since
  > they operate on disjoint code paths against the same `RouteRowViewModel`
  > instances. See §11.5 for the real trigger source (a Captain's journal).

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
| Cargo | Latest `Loadout` event's `CargoCapacity` (max) + latest `Cargo` event with `"Vessel":"Ship"`'s `Count` (current) | Shown as `current / capacity` tonnes. The `Cargo` event fires on essentially every real cargo change (market trades, mining, limpets) and not only on docking — confirmed live by adding cargo mid-session and seeing it reflected on the next Refresh — so the journal alone is sufficient without needing `Cargo.json`. Only total tonnage is tracked, not an itemized commodity breakdown (see §2). However, ignore tritium in the cargo count (the reason for this is what we need to know is how much space the ship has available for tritium so anything already present is not relevant.) |
| Current location | Latest of `Location`, `Docked`, `Undocked`, `FSDJump`, `CarrierJump` (while docked) — `StarSystem` (+ `StationName` if docked) | Shown as `System — Station`, or just `System` if not docked. `Undocked`/`FSDJump` clear the station (no `StarSystem` on `Undocked`, so the tracked system is left alone; `FSDJump` always means undocked). |
| Fleet carrier | Name from `CarrierStats`' `Name`/`CarrierID`; location resolved against that `CarrierID` from `CarrierJump`/`CarrierLocation` (+ `Body`, from `CarrierJump` only) | See both caveats below — the name is commonly unavailable even for an owner, and location resolution must be ID-matched, not just "latest event of the right type". |
| Journal file | The matched journal's filename | Filename only shown on the card (keeps it compact); the full path is also kept on the view model (not just the display string), same pattern as the window handle below - used by the Captain role's journal watcher (§11.5) |
| Window handle | `Process.MainWindowHandle` | Displayed as hex (`0xNNNN`); the raw `IntPtr` is also kept on the view model (not just the display string) for future use — e.g. sending keystrokes to a specific instance's window |
| Window position | `GetWindowRect` on the window handle | Screen coordinates + size |
| Monitor | `MonitorFromWindow` + `GetMonitorInfo` | Device name, primary flag, resolution, position |

  > **Update (tritium exclusion, implemented):** the `Cargo` event's
  > `Inventory` array (confirmed live: `{ "Vessel":"Ship", "Count":40,
  > "Inventory":[ { "Name":"biowaste", "Count":32, ... }, ... ] }`) is
  > summed per-commodity, skipping any entry whose (lowercase, unlocalised)
  > `Name` is `tritium`, rather than using the event's own top-level
  > `Count`. Falls back to the top-level `Count` if `Inventory` is ever
  > absent (can't exclude tritium in that case, but avoids losing the
  > field entirely on an older/unexpected journal shape).
  > **Update (CarrierId, implemented):** the same `CarrierID` used to
  > resolve `CarrierSystem`/`CarrierBody` (see the fleet carrier caveat
  > below) is now also kept on the view model (`CarrierId`, not shown on
  > the card) - used by the Captain role (§11.5) to filter
  > `CarrierJumpRequest`/`CarrierLocation` events to "this commander's own
  > carrier" the same way the existing per-instance summary already does.

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

### 11.5 Role assignment
- There are 2 roles "Captain" and "Engineer" and both should be assignable
  to one and only one running instance. They do not have to be assigned to
  the same instance, but they can be.
- When Captain is assigned start monitoring its journal file and setup an
  event handler that can update the route depending on the events received.
  This should also trigger a reread of the journal file to update the
  Roles tab with the latest information. While reading the journal file
  process CarrierJumpRequest and CarrierLocation events (matching the
  Captain's carrier ID) to update the route.
- The Engineer role cannot be assigned to a CMDR whose ship available
  capacity is zero (either they have no cargo racks or they are full), or
  whose available capacity isn't known at all.
- Clearing the Captain role - explicitly, or because its instance stops
  running - leaves the route exactly as it is; nothing is rolled back or
  reset. Assigning Captain to an instance (whether or not a different
  instance already held it) resets the whole route first, then performs
  the journal replay described above. A new instance appearing in a scan
  must not disturb the Captain/Engineer assignments already held by
  other, still-running instances.

  > **Update (implemented, `RolesViewModel`/`CarrierRouteJournalWatcher`):**
  > - Each card gets **Captain**/**Engineer** toggle buttons (top-right).
  >   Assigning a role to one card clears it from whichever other card
  >   held it; clicking an already-held role's button unassigns it. Role
  >   holders are tracked by `ProcessId` and restored onto the new
  >   `EliteInstanceViewModel` objects after every refresh (they're
  >   rebuilt from scratch each scan - see §11.3's doc comment); a role
  >   holder that's no longer in the scan results loses the role.
  >   Engineer's button is disabled exactly when `CanBeEngineer` is false -
  >   available capacity ≤ 0, **or** not known at all (`Loadout`/`Cargo`
  >   haven't been read yet this session).
  >   > **Update (reversed):** originally unknown capacity did *not* block
  >   > assignment (only positively-known-zero did), reasoning that
  >   > blocking on missing data would frustrate the user when it just
  >   > hadn't loaded yet. Reversed per explicit instruction - unknown now
  >   > blocks too, so the button can't be used to assign Engineer to an
  >   > instance the app genuinely doesn't know has any free space.
  >   > **Confirmed live:** actual journal field names, taken directly from
  >   > a real journal rather than assumed - `CarrierJumpRequest` carries
  >   > `CarrierType`, `CarrierID`, `SystemName` (the requested
  >   > destination); `CarrierLocation` carries `CarrierType`, `CarrierID`,
  >   > `StarSystem` (where it now is). Both event types are filtered to
  >   > `CarrierType == "FleetCarrier"` before matching `CarrierID` - same
  >   > squadron-carrier-must-not-be-mistaken-for-own-carrier caveat as
  >   > §11.3's `CarrierLocation` handling.
  > - `CarrierJumpRequest` -> `RowEventKind.Plotted`; `CarrierLocation` ->
  >   `RowEventKind.Arrived` (the composite step). Both are routed through
  >   the new `IRowEventTrigger` machinery (§10) via a single
  >   `ManualRowEventTrigger` instance shared between `RouteViewModel` and
  >   `RolesViewModel` (constructed once in `MainViewModel` and passed to
  >   both), so `RolesViewModel` never touches `RouteViewModel`/`Rows`
  >   directly.
  > - `CarrierRouteJournalWatcher` (`Services/`) does the actual reading:
  >   on assignment it reads the **whole** journal file first, oldest to
  >   newest, replaying every matching historical event through the row
  >   trigger in order - this is what makes "bring the route up to date"
  >   work from a single assignment even after several jumps have already
  >   happened (e.g. the app was restarted mid-journey). After that it
  >   live-tails new lines via `FileSystemWatcher`, tracking a byte offset
  >   and only consuming complete (newline-terminated) lines so a line
  >   caught mid-write by the OS isn't parsed as truncated JSON and lost.
  >   > **Verified live** against a real running instance and a real
  >   > journal containing one already-completed jump: assigning Captain
  >   > correctly marked the already-passed rows Complete (including a row
  >   > with no event of its own, caught up as a side effect of the next
  >   > row's Arrived event - see §10's Update), left the row matching the
  >   > carrier's actual current system in progress, and did not touch the
  >   > rows beyond it. A real jump takes roughly 16 minutes between
  >   > `CarrierJumpRequest` and `CarrierLocation`; the live-tail path
  >   > (as opposed to the historical-replay path) has not yet been
  >   > exercised end-to-end against a fresh in-flight jump.
  > - Assigning Captain also calls the existing `RefreshCommand` (if not
  >   already refreshing), satisfying "trigger a reread... to update the
  >   Roles tab" - the same background scan already reads the whole
  >   journal for the card fields, so no second full-file read is needed
  >   for that half of the requirement.
  > - The journal watcher runs its FileSystemWatcher callback off the UI
  >   thread; `RolesViewModel` marshals back onto the UI dispatcher
  >   (`Application.Current.Dispatcher.BeginInvoke`) before firing the row
  >   trigger, since `RouteRowViewModel`'s bound properties must only be
  >   mutated from the UI thread - the same reason `TimerSequenceTrigger`
  >   uses a `DispatcherTimer` rather than a plain `System.Threading.Timer`.
  > **Update (derived Jumping/Cooldown-clear transitions, implemented):**
  > `Jumping` and clearing `Cooldown` have no `CarrierJumpRequest`/
  > `CarrierLocation` event of their own to react to - they're derived from
  > timestamps already on those two events, applied once the real world
  > clock reaches the right instant:
  > - `CarrierJumpRequest`'s own `DepartureTime` field schedules the
  >   `Plotted` -> `Jumping` transition for its row.
  > - `CarrierLocation`'s own `timestamp` field + 5 minutes schedules
  >   clearing that row's `Status` (ending the `Cooldown` display).
  > - Two new `RowEventKind` values (`Jumping`, `CooldownElapsed`) carry
  >   these through the same `IRowEventTrigger` pipeline as `Plotted`/
  >   `Arrived` (§10's Update) - `RouteSequencer.FindTargetIndex` matches
  >   them more precisely than `Plotted`/`Arrived` (System text **and** the
  >   exact status their originating event left behind: `Jumping` only
  >   matches a row currently `Plotted`; `CooldownElapsed` only matches a
  >   Complete row currently showing `Cooldown`), so a stale/duplicate
  >   timer firing after the row has already moved on is a safe no-op.
  > - All the actual real-world scheduling (deciding whether to fire now
  >   or later, and the `Timer` that fires later) lives entirely in
  >   `CarrierRouteJournalWatcher` (`Services/`), **not** `RouteSequencer`/
  >   `Sequencing/` - see §10's Update for why: the non-negotiable
  >   event-driven-sequencing rule scopes to `Sequencing/`, and keeping
  >   all wall-clock reasoning outside it entirely (rather than arguing
  >   case-by-case whether a given timer "counts") means that rule never
  >   has to be relitigated for this feature or the next one.
  > - This is what makes "assigning Captain reflects this - either
  >   clearing immediately or scheduling for the appropriate amount of
  >   time" (the requirement that prompted this Update) work correctly
  >   without any special-casing: `ProcessLine` runs identically whether
  >   it's reading long-past history (assignment/reread) or a live
  >   `FileSystemWatcher` tick - each derived event fires immediately if
  >   its computed time has already elapsed (the common case when
  >   replaying an old journal - confirmed live: on reassignment, two
  >   already-long-completed jumps in the real test journal both cleared
  >   their `Cooldown` status immediately, rather than sitting there
  >   permanently as they had before this Update), or via a real,
  >   non-blocking one-shot `Timer` if it's still in the future (the
  >   common case for a jump just requested live).
  >   > **Verified:** a standalone harness (`CarrierRouteJournalWatcher`
  >   > pointed at a synthetic journal, no game/app involved) confirmed a
  >   > `DepartureTime` a few seconds out fires `Jumping` at the correct
  >   > wall-clock instant (accurate to ~2ms - ordinary thread-pool timer
  >   > jitter) via the `Timer` path, and that a live-appended
  >   > `CarrierLocation` line is picked up via `FileSystemWatcher` and
  >   > fires `Arrived` within milliseconds. The immediate-if-already-due
  >   > branch was verified separately, live, against the real "Haggis and
  >   > Chips" journal (§10/above). The only untested leg end-to-end is a
  >   > **real** jump's full ~16-minute `CarrierJumpRequest` ->
  >   > `CarrierLocation` gap through a live game session; the code path
  >   > is identical to what's already been verified in both halves.

  > **Update (reset-on-(re)assign, implemented):** a new `RowEventKind.Reset`
  > (not row-targeted - applies to every row: `Icon` -> `None`, `Status` ->
  > cleared) is fired by `RolesViewModel.ToggleCaptain`'s assign branch,
  > synchronously on the UI thread, before `StartCaptainWatch` kicks off the
  > new instance's replay. Since the watcher's own events are always
  > marshaled onto the UI dispatcher (`Application.Current.Dispatcher.
  > BeginInvoke` - see above), they're queued strictly after this
  > synchronous reset regardless of how quickly the watcher's background
  > read completes, so the ordering (reset, *then* replay) can't race.
  > Unassigning Captain (explicitly, or via the existing "role holder no
  > longer running" cleanup in `RefreshAsync`) never fires `Reset` - both
  > just stop the watcher and leave `Rows` untouched, which was already
  > true before this Update and needed no code change.
  > "A new instance appearing doesn't disturb existing role assignments"
  > also needed no code change: `RefreshAsync` already re-derives every
  > instance's `IsCaptain`/`IsEngineer` independently from
  > `_captainProcessId`/`_engineerProcessId` on every scan (§11.3's note on
  > `EliteInstanceViewModel` being rebuilt from scratch each refresh), so
  > an additional instance showing up alongside existing ones was already
  > handled correctly by construction.
  > **Verified live**, with two real concurrent instances running
  > ("Haggis and Chips" and "HAGGIS MACHAUL"): assigning Captain to the
  > first, then reassigning it to the second, reset the route and replayed
  > the second commander's own (different) journal history from scratch -
  > initially showing `Cooldown` still active on row 2, distinct from the
  > first commander's already-elapsed one (later found to be a bug in that
  > *distinctness*, not a feature working correctly - see the next Update).
  > Clearing Captain left the route exactly as displayed. A manual Refresh
  > while Captain was assigned (both instances rescanned) left both the
  > Captain assignment and the route untouched.

  > **Update (bug found and fixed live - startup CarrierLocation
  > misread as a fresh arrival):** the "`Cooldown` still active" result
  > just above for HAGGIS MACHAUL turned out to be wrong, not a genuine
  > in-progress cooldown. Frontier logs a `CarrierLocation` ping at session
  > start (a snapshot of wherever the carrier already was, not an
  > arrival), and the original implementation had no way to tell that
  > apart from a real one - it just scheduled `CooldownElapsed` off
  > whatever timestamp the event carried, so a startup ping timestamped
  > (say) 2 minutes ago left `Cooldown` genuinely - but wrongly - still
  > showing for 3 more minutes.
  > First fix (superseded below): treat the **first** `CarrierLocation`
  > matched during the initial replay as already elapsed for cooldown
  > purposes only, still applying the full Arrived/catch-up action to it
  > otherwise. This turned out to only paper over the timing symptom of a
  > deeper problem - see the next Update, which replaced this fix
  > entirely.

  > **Update (more serious bug found and fixed live - whole route marked
  > complete on assignment):** reported live: with only Haggis and Chips
  > running, sitting at `Col 285 Sector NL-J b23-1` (correctly shown on
  > the Roles card), assigning Captain against a **freshly-typed** route -
  > `Sol, Alpha Centauri, NL-J b23-1, NL-J b23-5, RP-F c11-17, KF-L b22-3,
  > JF-L b22-8` (`JF-L b22-8` deliberately placed *last*, not duplicated)
  > - marked the **entire** route Complete, not just up to `NL-J b23-1`.
  > Root cause: the session-start `CarrierLocation` snapshot (`JF-L
  > b22-8` - wherever the carrier was sitting before any of today's
  > jumps) was still being treated as a real `Arrived` event, just with
  > the cooldown-timing workaround above. `Arrived`'s catch-up marks every
  > row *before* the matched row Complete - and since this freshly-typed
  > route happened to place `JF-L b22-8` **last**, "before the matched
  > row" meant "the entire rest of the route", none of which the carrier
  > had actually visited via *this* route. The previous Update's fix
  > addressed the wrong layer: the timestamp wasn't the problem, treating
  > an unpaired snapshot as jump evidence at all was.
  > Fixed by recognizing what actually distinguishes the passive snapshot
  > from a real arrival: a real arrival always has a `CarrierJumpRequest`
  > immediately before it (same carrier, this session); the snapshot never
  > does, because it's logged before any jump has been requested at all.
  > `CarrierRouteJournalWatcher` now tracks a single `_hasSeenJumpRequest`
  > flag (spanning both the initial replay and live monitoring, never
  > reset) and **skips `CarrierLocation` entirely** - no `Arrived`, no
  > catch-up, no `CooldownElapsed` scheduling - until at least one
  > `CarrierJumpRequest` has been seen for that carrier. The
  > `_isInitialRead`/`_firstArrivalDuringInitialReadHandled` fields and
  > the cooldown-timing special case from the previous Update were removed
  > entirely, superseded by this simpler, more correct rule.
  > This still resolves the original mid-journey "resume after restart"
  > case, but *derived* from real evidence instead of assumed from the
  > passive snapshot: `Plotted`/`Arrived` already catch up every row
  > before their matched row (§10's Update), so the **first real**
  > `CarrierJumpRequest` of the session does exactly the same "catch up
  > everything before this row" job the skipped snapshot used to do -
  > just anchored to a row the carrier actually, deliberately jumped
  > towards, not merely a row that happens to share a name with wherever
  > it was sitting before any jump was requested.
  > **Verified live**, twice: (1) the original baseline route (`JF-L
  > b22-8` second, matching mid-route) re-run against this fix produces
  > the identical correct result as before (rows 1-3 Complete, row 4
  > InProgress) - confirming the fix doesn't regress the case it was
  > built for; (2) the reported repro route above now correctly shows only
  > rows 1-3 Complete (`Sol`, `Alpha Centauri`, `NL-J b23-1` - matching his
  > real progress) and row 4 InProgress, with rows 5-7 - including the
  > trailing `JF-L b22-8` - correctly left untouched, since the carrier
  > never actually traveled through them via this route.

  > **Update (regression found and fixed live - multi-session journeys
  > with no jump requested yet):** the previous Update's fix ("skip
  > `CarrierLocation` until a `CarrierJumpRequest` has been seen") broke
  > the legitimate case it was meant to help with: reported live after
  > restarting Elite Dangerous from the desktop (which cycles to a brand
  > new journal file) mid-journey - the new journal's *only* carrier event
  > is the passive startup snapshot, since nothing has requested a new
  > jump yet this session, so the gate never opened and the route never
  > updated on assignment at all. Flagged as the more serious of the two
  > bugs, since real fleet-carrier journeys (real-world jump cooldowns
  > measured in tens of minutes) routinely span multiple game sessions/
  > days, where restarting mid-journey with nothing freshly requested yet
  > is the *common* case, not an edge case.
  > Fixed with `CarrierRouteJournalWatcher.HasAnyJumpRequestSoFar`: before
  > the main pass, a quick pre-scan of the journal (as it stands at
  > assignment time) checks whether this carrier has a `CarrierJumpRequest`
  > *anywhere* in it. If none exists at all, `_hasSeenJumpRequest` is
  > seeded `true` from the start (gate open, snapshot trusted) rather than
  > `false` (gate closed until a real request is reached) - since in that
  > specific case the passive snapshot genuinely is the only evidence of
  > progress available, and refusing to use it would mean a long journey
  > could never resume until the next jump is manually requested. If a
  > request *does* exist anywhere in the journal, the gate still starts
  > closed as before - §11.5's route-mismatch bug fix is unaffected for
  > that case.
  > This is a best-effort heuristic, not a guarantee: it cannot distinguish
  > "no requests yet because progress genuinely stalled here" from "no
  > requests yet, and this position is unrelated to the current route" -
  > the latter can still, in principle, cause the same over-completion the
  > previous bug fix addressed. Accepted trade-off given the alternative
  > (never trusting an unpaired snapshot) breaks the common multi-day
  > case entirely; §11.6's manual "Set next system" override exists
  > specifically to rectify this heuristic (or a genuinely off-route
  > carrier) when it does guess wrong.
  > **Verified live** against the real regression: with Elite Dangerous
  > restarted (fresh journal, zero `CarrierJumpRequest` events, single
  > `CarrierLocation` reflecting the carrier's real current position) and
  > the original baseline route, assigning Captain now correctly shows
  > rows 1-3 Complete and row 4 InProgress again - matching the pre-
  > regression result.

### 11.6 Manual override - "Set next system"

Automatic detection from a Captain's journal (§11.5) is a best-effort
heuristic, not a guarantee - and a fleet carrier can simply be taken off
whatever route was typed into the app, for reasons that have nothing to
do with a detection bug. Right-clicking any row in the Route tab's table
opens a context menu with a single item, **"Set next system"**:

- Every row before the clicked one is set to Complete (icon + cleared
  status).
- The clicked row becomes the current row (in-progress icon, cleared
  status) - regardless of whatever icon/status it held before.
- Every row after the clicked one is reset to not-yet-started (no icon,
  cleared status) - regardless of whatever icon/status it held before.

This is a direct, immediate ViewModel command
(`RouteViewModel.SetNextSystemCommand`), not routed through the
`IRowEventTrigger`/`RouteSequencer` machinery §10/§11.5 use for
journal-driven updates - it isn't an external event being reported, it's
a one-shot manual correction, so there's nothing to gain from the extra
indirection.

> **Implementation note:** a `ContextMenu` is not part of the visual tree
> of the element it's attached to, so a binding inside one can't reach an
> ancestor via `RelativeSource AncestorType=...` the way the rest of this
> view does (e.g. the Roles tab's role buttons, or this same row's
> left-click-to-copy). Standard fix: the row's own `DataGridRow` style
> also sets `Tag` to the `RouteViewModel` (via the same
> `AncestorType=UserControl` binding that works fine there, since `Tag` is
> a normal property on an element still in the tree), and the
> `ContextMenu`'s `MenuItem` reaches back to both the command and its
> target row via `PlacementTarget` (`PlacementTarget.Tag` for the
> ViewModel, `PlacementTarget.DataContext` for the clicked row itself, as
> the command parameter).
> **Verified live:** right-clicking a row shows the menu; choosing it
> updates the table exactly as described, and the clicked row's grey
> selection highlight is unaffected. The menu's `Padding`/`MinHeight` were
> explicitly overridden (rather than left at the toolkit's default
> `MenuItem` sizing) after feedback that the default rendering looked too
> spacious for a single-item menu - confirmed live, visibly more compact.
> **Update:** still too spacious per further feedback ("about half" of
> the reduced amount) - vertical `Padding` taken from `6` down to `3`
> (`Padding="12,3"`).

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
19. Assigning Captain to a card assigns it to that instance only,
    unassigning it from any other card that held it; assigning Engineer
    behaves the same way independently. Verified live: assigning both
    roles to the same instance is allowed.
20. Assigning Captain replays that instance's full journal history of
    `CarrierJumpRequest`/`CarrierLocation` events (for its own fleet
    carrier only) into the Route tab in one go, correctly catching up
    rows that have no event of their own (§11.5/§13.1) — verified live
    against a real journal with one already-completed jump.
21. The Engineer button is disabled on a card whose available cargo
    capacity is zero, and also on a card whose available cargo capacity
    isn't known at all.
22. A row that receives `Cooldown` (via `Arrived`) has its status cleared
    exactly 5 minutes after that `CarrierLocation` event's own timestamp -
    immediately, if replaying history where that time has already passed;
    otherwise at the correct future moment. A row that receives `Plotted`
    transitions to `Jumping` at its `CarrierJumpRequest`'s `DepartureTime`
    the same way.
23. Unassigning Captain, or its instance no longer running, leaves the
    route exactly as displayed. Assigning Captain to an instance - whether
    or not a different instance held the role a moment ago - resets the
    whole route before that instance's journal is replayed into it. An
    additional instance appearing in a scan does not change which
    instances (if any) already hold Captain/Engineer.
24. Clicking anywhere in a row on the Route tab copies that row's system
    name to the clipboard and plays a confirmation ping; row alignment
    (`#`/`System`/`Status`) is unaffected. The clicked row's grey
    selection background is shown; no per-cell border is left behind.
25. Saving text with leading/trailing/interior blank lines and leading/
    trailing whitespace around system names produces a table with exactly
    one row per non-blank line, sequentially numbered, each `System` value
    trimmed. An empty text box shows "Paste route here, one system per
    line." as placeholder text.
26. Assigning Captain against a freshly-typed route only marks rows
    Complete up to (and including) the carrier's most recent *deliberate*
    jump (a `CarrierJumpRequest`/`CarrierLocation` pair), never based on
    the session's passive startup `CarrierLocation` snapshot alone - even
    when that snapshot's system happens to match a row anywhere else in
    the route, including the last one.
27. Assigning Captain against a journal with *no* `CarrierJumpRequest` at
    all this session (e.g. right after restarting Elite Dangerous
    mid-journey) still catches the route up to the carrier's current
    position, using the passive startup snapshot as a fallback.
28. Right-clicking any row in the Route tab's table shows a context menu
    with "Set next system"; choosing it marks every row before the
    clicked one Complete, makes the clicked row the current row, and
    resets every row after it to not-yet-started - regardless of their
    prior state.
29. The Route tab's text box has keyboard focus immediately on app
    launch, without requiring a click.
30. A row's icon does not visibly resize as it changes between hidden
    (`None`) and shown (`InProgress`/`Complete`).
31. Clicking Edit returns to the text box with its contents unchanged and
    keyboard focus on it; clicking Save afterward always produces a fresh
    table (never carrying over old icon/status state), with row 1
    defaulting to "next" unless a Captain is assigned, in which case the
    table is immediately re-derived from that Captain's journal instead.

---

## 13. Open Questions / Future Considerations

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

### 13.1 Row-addressable triggers — implemented for §11.5

> **Update: implemented.** The triggering events anticipated below are now
> defined (§11.5's `CarrierJumpRequest`/`CarrierLocation`), so this was
> scheduled and built. Kept here (rather than deleted) as a record of what
> was originally anticipated vs. what was actually built, and because the
> harder open question below (out-of-order/concurrent row events) is
> deliberately still unaddressed.

A row-addressable trigger identifies **which row** it applies to (by the
route's own System text - not by row index/position), rather than always
meaning "advance the current row in the queue." The original
`RouteSequencer` design assumed a strictly ordered, pre-built queue of
actions built once at Start, with no notion of jumping to an arbitrary row
out of order; the timer-paced queue (§7) still works exactly that way and
is unchanged.

What was actually built (see §10's Update, `Sequencing/RowEvent.cs`,
`Sequencing/IRowEventTrigger.cs`, `RouteSequencer.SetRows`/
`AttachRowTrigger`):
- A new trigger/event shape that carries a system name instead of a row
  identifier (`RowEvent { RowEventKind Kind, string SystemName }`) - a row
  identifier wasn't available at the source (a journal only knows system
  names), so matching by System text against `RouteRowViewModel.SystemText`
  turned out simpler than inventing a row-ID concept the event source
  would have had no way to populate.
- This runs as a second mechanism alongside `BuildSteps`/
  `RunNextStepImmediately`, not a rework of them - see §10's Update for why
  they were kept separate rather than unified.
- What happens when a row-specific event arrives "out of order": resolved
  for the single case this was built for (an event names a row further
  along than the current progress) by treating it as *catch-up* - every
  not-yet-complete row before the match is silently marked Complete. The
  harder cases flagged originally - two rows in flight at once, or an
  event for a row *behind* current progress - remain unaddressed; the
  current `ApplyRowEvent` finds the first matching row that isn't already
  Complete, so an event for an already-completed system is a silent no-op
  rather than an error, but there's still no real model for "in flight"
  beyond a single InProgress row at a time.