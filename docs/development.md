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
  Services/Companion/            Publishes Auto Pilot's key events to the companion site (Firestore REST, QR generation)
  Converters/                   WPF IValueConverters
  Views/                        XAML for each tab, plus the Preferences/About/Logs/Spansh windows
  Resources/                    App icon
RouteJumper.Tests/               xUnit unit test suite, mirroring the layout above (plus TestSupport/ - shared fakes/test doubles)
app/                             Companion site (Angular) - see "Companion site" below
docs/                            This documentation site (Jekyll)
```

---

## Companion site (Angular)

`app/` is a separate Angular project - the mobile-friendly companion site
users reach by scanning the QR code Auto Pilot shows (see the
[user guide](index.md#companion-site)). It has its own `package.json`/
tooling and isn't part of the WPF app's build or release pipeline at all;
`docs/` and `app/` are built and deployed together to GitHub Pages by
[`.github/workflows/pages.yml`](https://github.com/haggisandchips/RouteJumper/blob/main/.github/workflows/pages.yml)
(`docs/` at `/docs`, `app/` at `/app`).

```
cd app
npm install
npm start           # ng serve, http://localhost:4200/
npm test            # ng test (Vitest)
npm run build        # ng build
```

It talks to [Firestore](https://firebase.google.com/docs/firestore)
directly from the browser, against a single Firebase project shared by
every installation. That project's config is already committed
(`app/src/environments/environment*.ts`, and the matching `ProjectId` in
`CompanionSessionPublisher.cs`), so there's nothing to set up just to
build, run, or develop against it. See `app/README.md` if you're forking
this repo to run an independent instance against your own Firebase
project instead, and [§13](../specs/companion-site.md#13-companion-site)
for the full design (including housekeeping/retention).

While iterating on the Angular app, point the desktop app's
`CompanionSiteBaseUrl` setting (`routejumper.conf`) at
`http://localhost:4200` so its QR/link opens your local `ng serve`
instance instead of the deployed site.
