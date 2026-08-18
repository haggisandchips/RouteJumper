---
title: Development
---

# Development

For getting the app running from source in the first place, see
[Building from source](https://github.com/haggisandchips/RouteJumper#building-from-source)
in the README. This page covers running the test suite and the project
layout — useful background before opening a pull request.

---

## Running the tests

The solution includes a full unit test suite (`RouteJumper.Tests`,
xUnit) covering the route-progress engine, macro script parsing, journal
parsing, settings persistence, and the ViewModel layer:

```
dotnet test RouteJumper.Tests/RouteJumper.Tests.csproj
```

## Project layout

```
RouteJumper.sln
RouteJumper/                    The application
  App.xaml(.cs)
  MainWindow.xaml(.cs)          Window shell, menu bar, mode toggle, startup placement, clipboard-change hook
  Behaviors/                    WPF attached behaviors (e.g. click-to-command on a DataGrid row)
  Common/                       ICommand implementations, ObservableObject base, clipboard helper
  Models/                       Small data types (ControlAction, RowIcon, RecordedMacro, ...)
  ViewModels/                   One ViewModel per tab (Route/Roles/Controls/Track), plus per-row/per-item ViewModels
  Sequencing/                   The route-progress engine (event-driven, no hardcoded delays - see CLAUDE.md)
  Services/                     Journal parsing/watching (Fleet Carrier + Ship mode), Auto Pilot orchestration,
                                 EDSM lookups, Spansh route calculation, macro parser/player, settings/config
                                 stores, process/window scanning, key-binding formatting, speech synthesis
  Services/Logging/              Background file logging (Log, FileLogSink) and HTTP request logging
  Converters/                   WPF IValueConverters
  Views/                        XAML for each tab, plus the Preferences/About/Logs/Spansh windows
  Resources/                    App icon
RouteJumper.Tests/               xUnit unit test suite, mirroring the layout above (plus TestSupport/ - shared fakes/test doubles)
```
