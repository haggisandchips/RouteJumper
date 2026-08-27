[← Back to spec index](../SPEC.md)

## 11. Acceptance Criteria

Each item below is a short testable checklist entry; see the referenced
section for the full mechanism/exact timing/wording.

1. Launch shows Route/Roles/Controls tabs, left-edge headings; Route
   selected by default (§3.1).
2. Route tab starts in Edit state: empty text box, Save disabled, Cancel
   enabled, focus on the box (§4.1).
3. Typing enables Save; on a never-saved launch, Cancel clears the box
   (§4.1).
4. Saving N non-blank lines produces N numbered rows, `System` matching
   input verbatim, `Status` empty, only row 1 shows an icon (§4.3).
5. Edit restores the text unchanged with focus; re-Save always produces
   a fresh table (re-derived from the assigned Captain's journal, or row
   1 defaults in-progress); Cancel instead discards changes and leaves
   the table exactly as it was (§4.1, §4.2).
6. Auto Pilot flips its label and disables/enables Edit; drives the
   route via the Captain's macro while engaged (§4.2, §4.7).
7. Clicking a row copies its system, plays a confirmation sound, shows
   the clipboard icon (§4.2, §4.6).
8. "Set next system" marks earlier rows Complete, the clicked row
   current, later rows reset — regardless of prior state (§4.2).
9. Zero/N running `EliteDangerous64.exe` processes shows the correct
   empty state or N correctly-attributed cards (§5.1).
10. Restarting one instance updates only its own card on refresh (§5.1).
11. Captain/Engineer each assign to exactly one instance independently;
    both can share an instance (§5.4).
12. Assigning Captain resets the route, then applies a single
    catch-up result from the whole journal — including skipped rows —
    per §5.7's algorithm, never a naive line-by-line replay.
13. `Jumping` starts 3 min before `DepartureTime`; the composite Arrived
    step fires 1 min after arrival, on the *next* row; `Cooldown` only
    ever appears for a live-observed arrival, clearing 4 min later
    (§5.7, §4.4).
14. Engineer is disabled for zero/unknown cargo capacity (§5.4).
15. "Auto Copy To Clipboard" copies the in-progress row on toggle-on, and
    the next row on each live arrival (§4.6).
16. Route, window bounds, and Captain assignment all persist and
    restore on relaunch (with a fresh journal replay); "Auto Copy To
    Clipboard" always resets off (§7).
17. Explicitly unassigning a role clears its persisted FID (§7).
18. `CarrierJumpCancelled` clears `Status` on the in-progress row and
    cancels any already-scheduled `Jumping` transition for it (§5.7).
19. A live `CarrierStats` event triggers an automatic Roles refresh; the
    same event during historical replay does not (§5.1).
20. Carrier fuel shows as a `Nt fuel` sub-line once known, resolved
    against the commander's own carrier only (§5.3).
21. `routejumper.conf` is created on first launch with journal-folder
    defaults; a hand-edit takes effect on the next Refresh with no
    restart (§5.2).
22. A ship with no `Cargo` event this session shows 0, not "Unknown";
    Engineer eligibility follows capacity alone (§5.3).
23. Clicking a card's journal filename copies it + confirmation sound,
    no clipboard-source icon (§5.3).
24. Controls tab shows all nine key-binding defaults; capture mode
    rebinds on the next chord, `Escape` cancels (§6.2).
25. Running Instances scans/lists independently of Roles; auto-selects
    with one instance; Record requires a selection and nothing else
    active (§6.3).
26. Record captures tap/hold and click/held-click (400ms threshold),
    window-relative click position, and ≥150ms gaps as waits, producing
    the documented grammar on Stop as a new macro (§6.4).
27. `{CENTRE}` resolves at play time against the target window's
    then-current client-area size (§6.4).
28. The macro list's row (minus pencil/delete) selects the Play target;
    Play works for any instance/macro pairing (§6.5).
29. The pencil opens an editor (name, script box, grammar reference)
    that saves edits as made; Record disables while open (§6.5).
30. Playing resolves `PASTE {NEXT_SYSTEM}` against the current
    in-progress row; a new playback cancels one in progress, same as
    Stop (§6.5).
31. Key bindings and recorded macros both survive a restart (§6.2, §6.6).
32. Losing focus mid-playback aborts it and shows the closeable
    solid-red warning banner; Stop/a replacement playback doesn't
    (§6.5).
33. Roles shows Captain/Engineer macro pickers; Auto Pilot's enablement
    re-evaluates immediately on any change (§5.5, §4.2).
34. Deleting a macro selected for a role clears that selection; renaming
    doesn't (§7).
35. Controls shows Auto Pilot delay (default 5000) and Auto wait
    (default 300), persisted (§6.1).
36. Auto Pilot plays the Captain's macro per §4.7's exact rule (blank →
    immediate, Cooldown → after clear+delay, already in flight → no
    replay) for the first row and every later one, until Complete or
    stopped (§4.7).
37. Step runs one leaf instruction, re-foregrounding first, labelled with
    what's next; `WAIT` steps are skipped; wraps at the end; Stop
    cancels it; Auto wait applies between steps per the same rule as
    Play (§6.5, §6.1).
38. `{TRITIUM_LOOPS}` resolution for an Auto Pilot run rescans CMDR info
    and resolves per §6.4's formula, `0` on unknown capacity/fuel; no
    rescan for a script without the placeholder (§6.4).
39. The Engineer's refuel (independent of the Captain's plot) triggers
    once per row's Cooldown, delay-gated the same way, only if Engineer
    is assigned (§4.7).
40. Plotted/Jumping/Cooldown rows show a live countdown bar and
    `Status (H:MM:SS)` text; blank/Complete show neither, same row
    height either way (§4.4).
41. Route table column widths persist per column (except `Status`,
    which always fills remaining width) (§4.2, §7).
42. Auto wait applies after every script instruction unless the next is
    a `WAIT`, uniformly regardless of trigger (§6.4, §6.5).
43. Test {NEXT_SYSTEM}/{TRITIUM_LOOPS} (defaults `Sol`/`1`) resolve a
    manual Play/Step with no rescan; Play/Step disable while either is
    blank; neither persists (§6.1).
44. New Script creates and opens an empty, persisted macro under the
    same enablement rule as Record, but without needing a selected
    instance (§6.5).
45. File menu (Preferences/Exit) and the always-visible mute button are
    present regardless of active tab (§3.4).
46. Auto Pilot speaks the 30s/5s Plotting/Refueling announcements per
    §4.8's exact scheduling and skip/forgiveness rules; muting
    suppresses both.
47. Preferences' Voice dropdown lists every installed voice (cleaned
    display name, deduped Desktop entries); Test always plays; Volume
    persists immediately (§3.5).
48. Voice/volume/muted all survive a restart with documented defaults
    (§3.5, §7).
49. Auto Pilot's `Plotting` status shows an indeterminate bar and no
    countdown text until a real `CarrierJumpRequest` arrives (§4.4,
    §4.7).
50. Help > About shows the documented content; Help > Check for Updates
    reports the outcome via message box (§3.6, §3.7).
51. The Fleet Carrier/Ship toggle defaults to Fleet Carrier and swaps
    tabs/Auto Pilot visibility/journal watcher on toggle, persisting
    across restarts (§3.4).
52. Switching to Ship mode while Auto Pilot is engaged stops it outright
    (§4.2, §8.6).
53. The Track tab scans/lists instances like Roles (commander, location,
    journal only); exactly one Track assignment at a time (§8.1, §8.2).
54. Assigning a tracked instance resets the route, then applies a single
    catch-up result from that ship's own journal, never a fleet carrier
    it might also own (§8.2, §8.3).
55. Ship mode's Status progression (`Targeted` → `Jumping` → arrived →
    `Cooldown` → blank) follows §8.3's exact event vocabulary/deferral
    rules, confirmed against real journal data.
56. The composite Arrived step is driven by the following `Music` event,
    not `FSDJump` itself; Auto Copy To Clipboard still fires off the raw
    `FSDJump` line (§8.3, §8.5).
57. An off-route `FSDTarget` clears whichever row was showing `Targeted`
    back to blank, never leaving it stuck (§8.3).
58. Unassigning the tracked instance, or switching away from Ship mode,
    leaves the table exactly as displayed; reassigning/switching back
    always re-derives via a fresh catch-up (§8.2).
59. Tracking mode and tracked instance (by FID) both survive a restart
    (§7, §8.2).
60. Distance/Star Type populate asynchronously without blocking the
    table; row 1's Distance measures from the correct mode-appropriate
    ship position (§4.9).
61. An EDSM miss/failure blanks the cell rather than blocking; a
    resolved system is reused all session; across a restart, an
    EDSM-resolved value re-queries (cheap) while a persisted
    journal/Spansh seed reuses instantly (§4.9).
62. `FSDTarget`/`NavRoute` events opportunistically seed the cache in
    both modes; an already-displayed row refreshes live once seeded
    (§4.9).
63. A `Plotted`/`Arrived` event targeting a row further ahead than
    expected completes the skipped rows too, live or replayed;
    `Targeted` never completes anything (§5.7, §8.3).
64. Ship mode's current-system tracking is driven only by
    `Location`/`FSDJump`; repeated targeting/re-targeting never marks a
    row Complete and never leaves more than one row with an icon
    (§8.3).
65. `NavRouteClear` clears whichever row shows `Targeted`, the same as a
    fresh off-route `FSDTarget` (§8.3).
66. `Location` updates current-system tracking immediately but never
    starts `Cooldown`, even live; it supersedes a pending `FSDJump`
    arrival awaiting Music confirmation (§8.3).
67. Ship mode's catch-up resolves current-system/in-flight-jump/
    still-open-target as three independent results, not one
    most-recent-wins answer (§8.3).
68. Panic mode stops Auto Pilot and shows a banner per §4.7's exact
    conditions — including a completed-but-unconfirmed Captain's plot
    or Engineer's refuel deposit.
69. `{TRITIUM_LOOPS}` for an Auto Pilot run never resolves to a script
    that would exceed 4:45 playback, estimated per §6.4's formula; a
    manual Test value is never capped this way.
70. Help menu shows Logs above About (§3.4).
71. Logs window starts empty and shows entries logged from that point,
    within about a second, without blocking other tabs (§3.8).
72. Every log line is timestamped with level/category; logging never
    performs file I/O on the calling thread (§12).
73. The Logs folder holds one date-stamped file per day, tailable live,
    rolling to a new numbered segment past the size cap (§12).
74. Log housekeeping (7 days / 10MB / 100MB defaults) keeps the folder
    bounded automatically, oldest-first, never touching the active file
    (§12).
75. Distance and Star Type each resolve via one batched EDSM request per
    chunk, never one request per row, even when only one column is
    missing (§4.9).
76. An EDSM-confirmed-unresolved system isn't re-queried for
    `EdsmUnresolvedRetryHours` (default 12h), surviving a restart within
    that window; an early resolution clears the cooldown immediately
    (§4.9).
77. Help > Check for Updates always runs regardless of the Preferences
    opt-out, which gates only the silent startup check (§3.7).
78. Import Current Route shows a Yes/No confirmation, then replaces the
    route from `NavRoute.json` (skipping the departure entry), seeding
    the cache along the way; failure is logged, not dialoged (§4.10).
79. Trim for FC requires a Captain with a known carrier location
    (else an explanatory message box, no confirmation reached);
    otherwise a Yes/No confirmation, then the greedy 500ly-hop trim
    anchored from the carrier's real location, applied immediately;
    Ship-mode-hidden; missing coordinates block it (logged) (§4.11).
80. Each Spansh tab's Calculate is independently gated/polled/
    cancellable per §4.12; success seeds the cache and replaces the
    route with no confirmation; failure leaves it untouched with an
    explanatory status message.
81. The three Spansh menu items open the correspondingly-preselected
    tab of the same dialog; the Fleet Carrier tab's Source pre-fills
    from the Captain's real carrier location when resolvable (§4.12).
82. Neutron Plotter's Range always starts blank; Source pre-fills from
    the current system; the supercharge radio defaults per the
    `_overchargebooster_mkii` suffix rule (§4.12).
83. Each Spansh tab's footnote shows only while that tab is active, at
    the dialog's full width (§4.12).
84. Distance/Star Type each resolve via one batched request per chunk
    covering every needed row, even when coordinates are already cached
    but star types aren't (§4.9).
85. A dismissible "Plot needed"/"Target needed" advisory banner appears
    only once an enrichment pass completes with at least one such row,
    resetting on every fresh Save (§4.9).
86. The Engineer's refuel/announcements are never scheduled for the
    route's last row; the final route-completion announcement is spoken
    only for that stop reason (§4.7, §4.8).
87. Galaxy Plotter's Calculate stays disabled with an explanatory status
    message until ship-build resolution completes (§4.12).
88. Galaxy Plotter's Cargo pre-fills from tracked cargo; Reserve tank/
    Algorithm/checkboxes all start at Spansh's own defaults, all
    editable (§4.12).
89. Galaxy Plotter sends only derived fuel/mass/tank-capacity numbers,
    no separate ship-build upload (§4.12, §9).
90. Neutron Plotter results show a `Jumps` column; Galaxy Plotter results
    show `Refuel`/`Inject`/`Neutron` columns; never both at once, never
    for any other route source (§4.2, §4.12).
91. Route type and per-row Jumps/Refuel/Inject/Neutron data persist and
    restore correctly across every consecutive restart; any plain Save
    reverts to Plain (§7).
92. Editing a Neutron/Galaxy route shows a Proceed/Cancel conversion
    warning; a plain route shows none; Edit's own enablement is
    unaffected (§4.2).
93. Saving/restoring a Neutron/Galaxy route forces Ship mode and
    disables the Fleet Carrier chip with the documented tooltip, with no
    confirmation; reverting to Plain re-enables the chip without
    auto-switching modes (§3.4).
94. The companion QR/link button shows only while Auto Pilot is running
    with a session actually started, and hides again for as long as any
    macro is executing (closing its popup if open); a start failure is
    logged, not surfaced (§13).
95. All four companion event kinds (Plotted/Arrived/Refueled/Panic)
    appear in the live feed shortly after the in-app event, without
    delaying Auto Pilot (§13).
96. A companion publish failure never surfaces to the CMDR and never
    stops Auto Pilot — logged only (§13).
97. Stopping Auto Pilot marks the session `completed` or `panicked`
    correctly; re-engaging on the same route reactivates that same
    session (unchanged id, `status` back to `active`, prior events kept),
    while a route edit or app restart starts a genuinely new one (§13).
98. Clicking an event's Delete button on the companion site removes it
    permanently from Firestore with no confirmation prompt; every other
    viewer of the same session link sees it disappear live (§13).
