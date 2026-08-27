[← Back to spec index](../SPEC.md)

## 7. Persistence

A single generic `Settings(Key TEXT PRIMARY KEY, Value TEXT)` table in a
local SQLite database at `%LocalAppData%\EDFCAutoPilot\routejumper.db`.
Every operation opens/closes its own short-lived connection. A
persistence failure degrades to "nothing persisted" rather than the app
failing to start.

A second table, `ResolvedLookups (Kind TEXT, SystemKey TEXT, Value TEXT,
PRIMARY KEY (Kind, SystemKey))`, holds the subset of the coordinate/
star-type cache (§4.9) worth keeping forever — `Kind` one of `Coords`/
`StarType`. A value EDSM itself resolves is never written here
(session-only, §4.9) — only a journal/Spansh seed filling a
confirmed-unresolvable-by-EDSM gap is. System address (id64) is
session-only, not persisted, until a feature needs it. Its own table
rather than more `Settings` rows (a different access pattern — a point
lookup per route row). `StarType`'s `Value` is a canonical `StarClass`
code, never pre-formatted display text (§4.9).

A third table, `UnresolvedLookups (Kind TEXT, SystemKey TEXT,
LastAttemptUtc TEXT, PRIMARY KEY (Kind, SystemKey))`, backs the EDSM
retry cooldown (§4.9) — its own table for the same reason.

A fourth table, `CompanionSessions (SessionId TEXT PRIMARY KEY,
DeleteAfterUtc TEXT NOT NULL)`, backs the companion site's self-managed
housekeeping (§13) — records a finished session once its retention
window is known, deleted (from both this table and Firestore) once due.

A separate, hand-editable `routejumper.conf` sits beside the database
(§5.2) for user-editable configuration: the journal folder, §12's log
housekeeping settings, the EDSM retry cooldown
(`EdsmUnresolvedRetryHours`) and batch size (`EdsmCoordinatesBatchSize`,
§4.9), the Spansh autocomplete debounce (`SpanshAutocompleteDebounceMs`,
§4.12), the companion site's base URL (`CompanionSiteBaseUrl`, §13), and
its session retention (`CompanionSessionRetentionHours`, §13) — rather
than internal app state like the table below.

| What | Persisted when | Restored when |
|---|---|---|
| Route text | Every Save (first time or after Edit) | App startup, rebuilt via the normal Save path |
| Route type (Plain/Neutron/Galaxy) and per-row Jumps/Refuel/Inject/Neutron data (§4.2/§4.12) | Every ImportFromSpansh with a Neutron/Galaxy route type | App startup, re-applied after the normal Save path rebuilds Rows — reset to Plain/cleared by every Save (manual, Import Current Route, Trim for FC, or this same restore), so only a Save immediately following a Spansh import can leave it non-Plain |
| Window position, size, maximized state | Window closing | App startup — in place of default rightmost-monitor placement, unless no longer reachable |
| Route table column widths (icon, `#`, `System`, `Distance`, `Jumps`, `Refuel`, `Inject`, `Neutron`, `Star Type` — not `Status`) | Window closing | App startup, per column — default width until first resized |
| Journal/Spansh-seeded coordinates/star type filling a confirmed EDSM gap, in `ResolvedLookups` | First seed after EDSM confirms no record | Every later Save/restore, indefinitely — never re-fetched from EDSM once cached |
| EDSM unresolved-lookup cooldown, by system name and kind | Every lookup EDSM confirms has no record | Every later lookup, until `EdsmUnresolvedRetryHours` lapses — cleared immediately on resolution |
| Companion session id + delete-after instant, in `CompanionSessions` | Every `StartSessionAsync` (fixed 72h deadline), shortened by every `EndSession` (`CompanionSessionRetentionHours` after run ends, clamped to the 72h ceiling) | Every app launch — a session past its delete-after instant is deleted from Firestore and forgotten; one not yet due is left alone |
| Captain/Engineer role, by commander FID | Assigned/explicitly unassigned | Every Roles refresh, while unassigned in memory |
| Captain/Engineer role macro, by macro Id | Selected/cleared | Every Roles refresh, while unselected in memory |
| Controls tab options (Auto Pilot delay, Auto wait) | Every change | App startup, 5000ms/300ms defaults until first changed |
| Controls tab key bindings | Every successful capture | App startup, per action — default until rebound |
| Controls tab recorded macros | Every new recording, edit, or delete | App startup, as a single collection |
| Announcement voice, volume, muted | Every change | App startup — engine default voice, 100 volume, unmuted until changed |
| Automatic update check enabled | Every change | Checked fresh every launch before the silent check — enabled until changed |
| Tracking mode (Fleet Carrier/Ship) | Every toggle | App startup — Fleet Carrier until changed |
| Tracked instance, by commander FID | Assigned/explicitly unassigned | Every Track tab refresh, while unassigned in memory |

- Role assignment restores by FID (not `ProcessId`, which doesn't survive
  a process restart) — covers both "app just launched" and "role
  holder's game process restarted mid-session" identically; a restored
  Captain match goes through the same reset-and-replay as a fresh manual
  assignment.
- Explicitly unassigning clears the persisted FID (no auto-reassignment
  next refresh); a role holder's process stopping clears only the
  in-memory assignment, leaving the FID to pick back up automatically if
  that instance reappears.
- A role's macro persists by the macro's stable Id, not its editable
  name — renaming never loses the selection; deleting the selected macro
  clears the role's selection to unset.
- "Auto Copy To Clipboard" (§4.6) is deliberately **not** persisted.
- Not persisted: per-row progress (icon/status), the last-selected tab.
