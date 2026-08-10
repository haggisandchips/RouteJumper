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

### Changed

- Default "Auto wait" delay raised from 250ms to 300ms.
- Recorded Macros list: the whole row is now clickable to select a
  macro, not just its name text (the Edit/Delete icons are still
  excluded).

---

Nothing has been tagged/released yet — the first entry here will become
`## [1.0.0] - YYYY-MM-DD` once a release is cut (see the README's
[Packaging & distributing releases via GitHub](README.md#packaging--distributing-releases-via-github)
section).
