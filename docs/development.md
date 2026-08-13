---
title: Development
---

{% include nav.html %}

# Development

For getting the app running from source in the first place, see
[Building from source](https://github.com/haggisandchips/RouteJumper#building-from-source)
in the README. This page covers running the test suite, the project
layout, and cutting/publishing a release.

## Contents

- [Running the tests](#running-the-tests)
- [Project layout](#project-layout)
- [Packaging & distributing releases via GitHub](#packaging--distributing-releases-via-github)

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
  MainWindow.xaml(.cs)          Window shell, File menu, startup placement, clipboard-change hook
  Common/                       ICommand implementations, ObservableObject base, clipboard helper
  Models/                       Small data types (ControlAction, RowIcon, RecordedMacro, ...)
  ViewModels/                   One ViewModel per tab, plus per-row/per-item ViewModels
  Sequencing/                   The route-progress engine
  Services/                     Journal parsing/watching, macro parser/player, settings/config
                                 stores, process/window scanning, key-binding formatting,
                                 speech synthesis
  Converters/                   WPF IValueConverters
  Views/                        XAML for each tab, plus the Preferences dialog
  Resources/                    App icon
RouteJumper.Tests/               xUnit unit test suite, mirroring the layout above
```

---

## Packaging & distributing releases via GitHub

RouteJumper self-updates via [Velopack](https://velopack.io/) (see
`RouteJumper/Services/UpdateService.cs`): every launch checks GitHub
Releases for a newer version and, if found, downloads and applies it on
the *next* exit rather than interrupting the current session. This means
a release isn't just a built exe — it has to be packaged with Velopack's
own `vpk` tool so the installed copy and the update feed are both in the
format `UpdateManager` expects.

### Cutting a release (automated)

[`.github/workflows/release.yml`](https://github.com/haggisandchips/RouteJumper/blob/main/.github/workflows/release.yml)
does this end to end: pushing a tag matching `v*` builds RouteJumper,
packs it with `vpk`, and publishes it as a GitHub Release, which
`UpdateService` then picks up automatically on every installed copy's
next launch.

```
git tag v1.0.0
git push origin v1.0.0
```

That's it — no local `vpk` install, no personal access token, nothing
else to configure. The workflow uses GitHub Actions' own automatic,
repo-scoped `GITHUB_TOKEN` (granted `contents: write` at the top of the
workflow file) to both fetch the previous release (so Velopack can
generate a small delta package against it) and publish the new one.

Move the relevant entries from
[`CHANGELOG.md`](https://github.com/haggisandchips/RouteJumper/blob/main/CHANGELOG.md)'s
`[Unreleased]` section into a new dated, versioned section (e.g.
`[1.0.0] - 2026-08-10`) as part of the same commit that gets tagged.

### Cutting a release (manual)

The steps below are what the workflow above automates — useful for
testing a package locally, or if you ever need to cut a release without
CI.

#### 1. Install the `vpk` CLI (one-time)

```
dotnet tool install -g vpk
```

#### 2. Decide on a version and tag

Use [Semantic Versioning](https://semver.org/) tags, e.g. `v1.0.0` (pass
the bare `1.0.0`, no `v` prefix, as the pack version in step 4):

```
git tag v1.0.0
git push origin v1.0.0
```

#### 3. Publish a self-contained build

WPF apps don't support trimming reliably, so publish **self-contained**
(bundles the .NET runtime — no separate runtime install needed):

```
dotnet publish RouteJumper/RouteJumper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o publish/win-x64
```

Unlike a plain zipped release, don't add `-p:PublishSingleFile=true`
here — Velopack needs the individual output files (not one bundled exe)
so it can diff them against the previous release and generate a small
delta package.

#### 4. Pack it with Velopack

```
vpk pack `
  -u EDFCAutoPilot `
  -v 1.0.0 `
  -p publish/win-x64 `
  -e EDFCAutoPilot.exe `
  -i RouteJumper/Resources/AppIcon.ico
```

Produces `EDFCAutoPilot-win-Setup.exe` (the installer), a full `.nupkg`, and a
`RELEASES` manifest under `Releases/` — plus a delta package against the
previous version, if `Releases/` still has that previous version's
output sitting in it from the last time this was run (keep that folder
around between releases so delta generation has something to diff
against; a missing previous version just means a full-size update
instead of a small delta one, not a failure). The workflow gets this by
running `vpk download github` into `Releases/` first; doing this by hand
means running that yourself, or just accepting a full-size package.

#### 5. Upload the release to GitHub

```
vpk upload github `
  --repoUrl https://github.com/haggisandchips/RouteJumper `
  --token $env:GITHUB_TOKEN `
  --tag v1.0.0 `
  --publish
```

Unlike the automated workflow (which uses GitHub Actions' own built-in
token), a manual upload needs a real [personal access
token](https://github.com/settings/personal-access-tokens/new) with
**Contents: Read and write** on this repo (`$env:GITHUB_TOKEN = gh auth
token` reuses the `gh` CLI's own, if already logged in). Omit
`--publish` to upload as a draft release for a final check before it
goes live. First-time installers still need to download
`EDFCAutoPilot-win-Setup.exe` from the Releases page directly — `UpdateService`
only ever updates an *already-installed* copy, and no-ops entirely for
an unpackaged `dotnet run` build.
