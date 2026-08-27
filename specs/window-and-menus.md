[← Back to spec index](../SPEC.md)

## 3. Window Structure

### 3.1 Main Window
- Single top-level window, `TabControl` with left-side tab headings
  (`TabStripPlacement="Left"`). Route is always first and selected by
  default. Roles/Controls show in Fleet Carrier mode (default); Track (§8)
  shows in Ship mode instead — switching modes (§3.4) swaps them
  immediately, whichever tab is currently showing.
- Resizable, content reflows (no fixed-pixel layouts).
- Title: **"ED:FC Auto Pilot"**. Icon: `Resources/AppIcon.ico`, a
  `RocketLaunch` glyph at 16/32/48/256px, matching row-icon style/color.

### 3.2 Startup window placement
- On launch: positioned against the right edge of the physically
  rightmost connected monitor (10px margin, vertically centered) — the
  monitor with the greatest `Left` coordinate, determined fresh each
  launch — unless a persisted, still-reachable position exists (§7), in
  which case that's restored instead.
- Positioned at `SourceInitialized` (HWND exists, not yet painted), so no
  visible jump on launch. Monitor bounds are converted to WPF DIUs via the
  window's own composition-target transform, correct under per-monitor DPI.

### 3.3 Controls tab
See §6.

### 3.4 Menu bar, mode toggle, and mute button
A `Menu` above the tab area, always visible, with **File**, **Spansh**,
**Help**:
- **File**: **Preferences…** (modal, §3.5). **Exit** (persists bounds,
  §7, and quits).
- **Spansh** (its own top-level entry, not nested — the app's only such
  integration): **Fleet Carrier…**/**Neutron Plotter…**/**Galaxy
  Plotter…** each open the same modal dialog (§4.12) with the
  corresponding tab pre-selected; all three tabs stay reachable from
  inside regardless, in either tracking mode, from either main tab.
- **Help**: **Logs** opens the non-modal Logs window (§3.8) — stays open
  and live-updating unlike Preferences/About; a second click re-focuses
  the existing window rather than opening a duplicate. **Check for
  Updates** manually triggers the same download-and-apply-on-next-exit
  check run silently on launch (§3.7), always regardless of the
  Preferences opt-out, reporting the outcome via a message box (the
  silent check never does); disabled while a check it started is in
  flight. **About** opens the About dialog (§3.6, modal).

Between the menu and the mute button, centred: a **Fleet Carrier / Ship**
choice-chip toggle (§8). Switching swaps visible tabs (§3.1), shows/hides
and force-stops Auto Pilot (§4.2), and switches which journal watcher runs
(Roles' Captain watcher or Track's ship watcher — exactly one at a time).
Persists immediately; defaults to Fleet Carrier until first changed. Each
chip shows its own tooltip (explaining what switching to it does) only
while it is *not* the currently-selected chip.
- The **Fleet Carrier** chip is disabled for as long as the saved route
  is Neutron- or Galaxy Plotter-typed (§4.12) — a carrier's own Auto
  Pilot has no notion of neutron boosts/FSD injections. The instant such
  a route is saved or restored on launch, Ship mode is forced silently
  (no confirmation), overriding any previously-active or persisted Fleet
  Carrier mode; the chip re-enables the instant the route reverts to
  Plain (Edit's confirmation, Import Current Route, or Trim for FC)
  without auto-switching back — the CMDR chooses that. While disabled,
  its tooltip reads "Not appropriate for Neutron Plotter routes." or
  "Not appropriate for Galaxy Plotter routes." as appropriate, followed
  by "Edit the route to revert it to a plain route, then you can switch
  back to Fleet Carrier mode." in place of its usual text.

Right-aligned beside the menu: an always-visible mute icon button toggles
the spoken announcements (§4.8) — icon swaps plain/crossed-out speaker.
Has no effect on the Preferences dialog's own Test control (§3.5), which
always plays.

### 3.5 Preferences dialog
Modal (File > Preferences…) for announcement voice/volume plus the
update-check opt-out — the mute toggle itself lives only on the main
window (§3.4).
- **Voice**: dropdown of every installed OS voice, cleaned-up display
  name (the "Microsoft " prefix stripped; a duplicate "... Desktop" entry
  for the same voice dropped entirely), plus a clear button resetting to
  blank (the engine's own default — the dropdown has no blank entry of
  its own).
- **Test** (icon button): speaks a fixed sample phrase at the current
  voice/volume, always audible even while muted.
- **Volume**: slider, 0-100.
- **Automatically check for updates on startup**: checkbox, checked by
  default, gates only the *silent* startup check (§3.7) — no effect on
  Help > Check for Updates.
- Every change saves immediately (§7, same "edits save as they're made"
  convention as Controls' Options); **Close** is pure navigation.

### 3.6 About dialog
Modal (Help > About), purely informational: app icon, "ED:FC Auto
Pilot", installed version plus its real GitHub release date/time
(`dd MMM yyyy HH:mm`, local time, fetched on demand from the GitHub API
per §9's Network section) or "Development build" for an unpackaged run,
a one-line description, the fan-made/not-affiliated disclaimer, a GitHub
repo link (default browser), copyright/license line. Single **Close**
button. The version line starts blank and fills in once the release-date
lookup resolves (or immediately for a "Development build", which needs
no lookup); a failed/slow lookup falls back to showing just the version
number, never blocking the dialog opening.

### 3.7 Automatic updates
- On every launch, silently checks GitHub Releases for a newer version
  (via Velopack); downloads in the background and applies on the *next*
  normal exit. No-ops for an unpackaged run (`dotnet run`/F5).
- Turned off entirely via Preferences' "Automatically check for updates
  on startup" checkbox (§3.5, on by default) — persists immediately,
  re-checked fresh every launch.
- Help > **Check for Updates** (§3.4) runs the identical check on demand,
  always regardless of that checkbox, and reports via a message box: up
  to date / downloaded (installs next exit) / not available (unpackaged
  build) / check failed (network) — the only case a failure surfaces at
  all, since the silent check never reports anything.

### 3.8 Logs window
Non-modal (Help > Logs), live-tails the logging stream (§12) — the
on-disk file is the durable record; this window is a live in-memory view.
- Opens **empty** — shows only entries logged while open, nothing
  earlier; closing/reopening always starts blank. **Clear** empties the
  visible text without touching the on-disk file.
- **Wrap text** checkbox, off by default (unwrapped, matching raw log
  layout; scrollbar hidden while wrapped). Session-only.
- **Open Logs Folder** opens the on-disk Logs directory (§12) in
  Explorer.
- **Close** dismisses it. Owned by the main window, closes alongside it.
