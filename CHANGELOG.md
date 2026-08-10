# Changelog

All notable changes to RouteJumper are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project intends to follow [Semantic Versioning](https://semver.org/)
once the first release is tagged. Until then, everything lives under
`[Unreleased]`.

## [Unreleased]

### Added

- MIT `LICENSE`.
- `CHANGELOG.md` (this file).
- Full `README.md` covering setup, the three tabs, the macro scripting
  language, three ready-to-use sample scripts, and the release
  procedure for distributing builds via GitHub.
- `RouteJumper.Tests`, a unit test suite covering the route-progress
  engine, macro script parsing, journal parsing, settings persistence,
  and the ViewModel layer.
- **New Script** button (Controls tab): creates a new, empty macro and
  opens it straight in the editor, for writing or pasting in a script
  by hand instead of recording one.

### Changed

- Default "Auto wait" delay raised from 250ms to 300ms.
- Recorded Macros list: the whole row is now clickable to select a
  macro, not just its name text (the Edit/Delete icons are still
  excluded).

### Fixed

- A macro created via **New Script** had blank pencil/Delete icons on
  its row until the app was restarted — its row was rendered for the
  first time while collapsed (opening the editor immediately hid the
  list), which MaterialDesignThemes' icon buttons don't recover from.
  The editor now opens a moment after the row has actually rendered.

---

Nothing has been tagged/released yet — the first entry here will become
`## [1.0.0] - YYYY-MM-DD` once a release is cut (see the README's
[Packaging & distributing releases via GitHub](README.md#packaging--distributing-releases-via-github)
section).
