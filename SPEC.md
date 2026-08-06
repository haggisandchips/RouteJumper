# RouteJumper — Application Specification

**Version:** 1.2
**Status:** Route tab implemented (0.5s cadence, combined completion step);
Material Design restyle in progress; Control tab reserved for future work
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
| Main window with two tabs: **Route**, **Control** | Control tab functionality (header only for now) |
| Text entry, save-to-table workflow on the Route tab | Persistence of route lists between sessions |
| Timed Start/Stop sequence over table rows | Configurable timing (fixed at 2s for v1) |
| Event-driven sequencing engine, open to future trigger types | Non-timer triggers actually wired in (framework only) |

---

## 3. Window Structure

### 3.1 Main Window
- Single top-level window containing a `TabControl` with exactly two tabs.
- Tab order: **Route**, **Control**.
- Window is resizable; content should reflow (no fixed-pixel layouts).

### 3.2 Tab 1 — "Route"
Has two mutually exclusive states, **Edit** and **Running**, described below.
Only one is visible at a time.

### 3.3 Tab 2 — "Control"
- Displays only the tab header "Control" in v1.
- Content area reserved for future requirements; must not error or block
  the rest of the app if left empty.

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
- Each action below is separated from the next by a **0.5 second** interval
  in v1.2 (changed from 0.2s during further testing; originally 2s).
- The sequence must be built so that **advancing to the next action is a
  response to an event**, not a hardcoded delay call, so that the pacing
  mechanism (today: a timer) can later be replaced or supplemented by other
  event sources (e.g. a manual "step" trigger, an external signal) without
  changing the action logic itself. See §9 for the architectural
  requirement this implies.

### 7.2 Action plan
On **Start**:

1. Immediately: show the "in-progress" icon (§7.3) in row 1's icon column.
2. Then, once per 0.5-second interval, for the **current row**, in order:
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

| State | Appearance | When shown |
|---|---|---|
| None | *(empty cell)* | Default, and after a row's cycle content is otherwise done (icon is **not** cleared — see note) | 
| In-progress | Green triangle, pointing right | From the moment a row becomes "current" until it completes step 7.2.4 |
| Complete | Green tick | From step 7.2.4 onward, permanently (for that run) |

> Note: the spec does not require clearing the icon at any point — once a
> row reaches "Complete" it keeps the tick for the remainder of the run.

### 7.4 Timing summary (example, 3 rows)

```
t=0.0s  Row1 icon -> in-progress
t=0.5s  Row1 status -> Plotting
t=1.0s  Row1 status -> Plotted
t=1.5s  Row1 status -> Jumping
t=2.0s  Row1 icon -> complete, Row2 icon -> in-progress, Row1 status -> Cooldown  (combined)
t=2.5s  Row1 status -> (cleared)
t=3.0s  Row2 status -> Plotting
...
```

---

## 8. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (net8.0-windows), MVVM pattern throughout —
  no business logic in code-behind.
- **Responsiveness:** UI must remain interactive throughout a run (Stop
  must be clickable at any point).
- **Extensibility:** Adding a new trigger type or a new action step must
  not require changes to the ViewModel or View layers (see §9).
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
- The color used for both icon states must be drawn from the active
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

### 9.3 DataGrid
- The toolkit ships its own DataGrid styling; the existing
  `DataGridTemplateColumn` (icon), `#`, `System`, and `Status` columns must
  be visually verified after the restyle — Material's header/row/selection
  chrome is more opinionated than the default WPF DataGrid style.

### 9.4 Non-goals for this pass
- No dark/light theme toggle in v1.1 (pick one `BaseTheme` and ship it).
- No change to any ViewModel, command, or sequencing logic — this is a
  view-layer-only change.

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
- v1 ships with one trigger in active use (a fast timer, currently 0.5s)
  and the framework for at least one alternative trigger type, to prove
  the decoupling holds — but wiring up additional real-world triggers
  (hardware signals, other UI events, etc.) is future scope. See §12 for
  a planned trigger type that will need this decoupling to hold.

---

## 11. Acceptance Criteria

1. Launching the app shows a window with **Route** and **Control** tabs;
   Route is selected by default.
2. Route tab initially shows an empty, full-size text box with disabled
   Save and enabled Cancel.
3. Typing text enables Save; clicking Cancel clears the text box.
4. Entering N non-empty lines and clicking Save produces a table with
   exactly N rows, numbered 1..N, `System` matching the input lines
   verbatim, `Status` empty, icon column empty.
5. Start is disabled until Save has produced at least one row, and while a
   sequence is running.
6. Clicking Start immediately shows the in-progress triangle on row 1.
7. Every 0.5 seconds thereafter, exactly one action from §7.2 occurs (the
   combined tick/next-triangle/Cooldown step counts as one), in the
   specified order, until all rows have completed.
8. Row 2's triangle appears at the point specified in §7.2 (within the
   combined step of row 1's cycle), not before.
9. Clicking Stop at any time halts further changes; Start re-enables and,
   if clicked again, restarts the full sequence from row 1.
10. After the last row completes step 7.2.5, the sequence stops itself
    without requiring Stop to be clicked, and Start re-enables.
11. Control tab displays only its header with no errors.
12. After the Material Design restyle, all of the above still hold —
    styling must not change any behavior, only appearance.

---

## 12. Open Questions / Future Considerations

- Should there be a way to return from the table view to the text box
  (e.g. an "Edit" button) without restarting the app?
- Should the 0.5 second interval be user-configurable?
- Should Stop support pausing/resuming mid-row, rather than only a full
  restart?
- What should populate the Control tab?

### 12.1 Planned: row-addressable triggers (not yet scheduled)

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