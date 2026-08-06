# Route Jumper

A WPF (MVVM) application, targeting .NET 8, with a two-tab main window.

## Opening the project

1. Unzip this folder anywhere.
2. Open `RouteJumper.sln` in Visual Studio 2022 (or later).
   - Requires the **.NET desktop development** workload.
   - If VS asks to retarget/restore, let it — it will pull in the SDK for
     `net8.0-windows` (install the .NET 8 SDK first if you don't have it).
3. Press **F5** to run.

## What it does

- **Route tab**: starts as a full-size, multi-line text box with **Save** and
  **Cancel** buttons underneath.
  - **Cancel** clears the text box.
  - **Save** parses the text (one line = one row) and replaces the text box
    with a table (`Icon | # | System | Status`), showing **Start** and
    **Stop** buttons underneath.
- Clicking **Start**:
  1. Puts a green triangle (▶) in the icon column of row 1.
  2. Every 2 seconds, advances one step through the sequence for the current
     row: `Plotting` → `Plotted` → `Jumping` → triangle becomes a tick (✔) →
     triangle appears on the next row (if any) → `Cooldown` → status cleared.
  3. Repeats for every remaining row, then stops automatically.
- **Stop** halts the sequence at any point; **Start** resumes a fresh run
  from row 1 (only enabled again once the previous run has stopped).
- **Control tab**: placeholder, header only, as requested.

## Why it's structured this way

The brief asked for the sequence to be built so that **each action can be
triggered by an event**, since multiple things might eventually decide when
an action should fire. That drove the design in `Sequencing/`:

- `ISequenceTrigger` — anything that can raise a "move to the next action"
  event. It knows nothing about rows, status text, or icons.
- `TimerSequenceTrigger` — the trigger actually used today: a 2-second
  `DispatcherTimer`.
- `ManualSequenceTrigger` — an example of a second trigger type (fires on
  demand via `Fire()`, e.g. from a button or an external event) to show the
  sequencer isn't tied to timing at all.
- `RouteSequencer` — builds the full ordered list of actions for the table
  up front (icon changes + status changes, row by row, matching the spec
  exactly) and executes exactly one action every time *any* attached
  trigger fires. You can call `AttachTrigger(...)` as many times as you
  like — e.g. wire up both a timer and a manual trigger — with no changes
  to the ViewModel or view.

This keeps the "what happens" (the action plan) completely separate from
"when it happens" (the trigger), so new triggers can be added later without
touching the sequence logic itself.

## Project layout

```
RouteJumper.sln
RouteJumper/
  App.xaml(.cs)
  MainWindow.xaml(.cs)            Two-tab shell (Route / Control)
  Common/
    ObservableObject.cs           INotifyPropertyChanged base class
    RelayCommand.cs                ICommand implementation
  Models/
    RowIcon.cs                     None / InProgress / Complete
  ViewModels/
    MainViewModel.cs
    RouteViewModel.cs              Route tab logic (Save/Cancel/Start/Stop)
    RouteRowViewModel.cs           One table row
    ControlViewModel.cs            Empty placeholder
  Sequencing/
    ISequenceTrigger.cs
    TimerSequenceTrigger.cs
    ManualSequenceTrigger.cs
    SequenceStep.cs
    RouteSequencer.cs
  Converters/
    BoolToVisibilityConverter.cs
    IconToGlyphConverter.cs
  Views/
    RouteView.xaml(.cs)
    ControlView.xaml(.cs)
```
