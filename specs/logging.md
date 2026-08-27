[← Back to spec index](../SPEC.md)

## 12. Logging

A background, non-blocking facility recording significant events for
diagnosis — outbound HTTP requests, every journal event either watcher
actually *acts on* (a row transition, an opportunistic cache seed,
`CarrierStats`), journal-watch start/stop, role/track assignment, "Auto
Copy To Clipboard" firing, Auto Pilot triggers/panic stops, macro
recording/playback, update checks, persistence failures. Each line
carries only the fields that action actually used, never a raw-JSON
dump. Two sinks per entry: a durable date-stamped file (always written)
and the Logs window (§3.8, only while open).

- **Never on the hot path**: logging only enqueues onto an in-memory
  queue — never disk I/O or blocking on the calling thread. Matters most
  for the event-driven Sequencing/ engine (CLAUDE.md) and macro playback
  timing. A single background writer drains the queue.
- **Format**: `yyyy-MM-dd HH:mm:ss.fff [LEVEL ] Category: message`, e.g.
  `2026-08-15 09:05:03.250 [INFO ] Http: -> GET
  https://www.edsm.net/api-v1/systems?...`. Levels Debug/Info/Warn/
  Error; Warn/Error may carry an exception's type/message. Categories:
  `Http`, `Journal`, `AutoPilot`, `Roles`, `Track`, `Route`, `Macro`,
  `Update`, `Settings`, `Config`, `Edsm`, `Spansh`, `Companion`, `Scan`,
  `App`.
- **HTTP requests**: every direct outbound call (EDSM, Spansh, companion
  Firestore, and UpdateService's own GitHub API release-date lookup,
  §3.6/§9 — the app's only four direct-HttpClient integrations) logs
  method+URL out and status+elapsed time back (or a warning on outright
  failure). Velopack's own internal update-check calls aren't
  individually visible (no logging seam) — covered instead by an
  Info/Warn line around each check's start/outcome (§3.7).
- **File** (`%LocalAppData%\EDFCAutoPilot\Logs\routejumper-yyyy-MM-dd.log`,
  `.Dev` for an unpackaged run): one file per day, opened for
  concurrent-read append (e.g. `Get-Content -Wait`) — the writer flushes
  after draining the queue, so a concurrent tail sees new lines with
  minimal lag without a per-line flush cost. A file crossing the
  per-file size cap mid-day rolls to a new numbered segment
  (`routejumper-yyyy-MM-dd.2.log`, ...).
- **Housekeeping**: keeps the folder bounded, via `routejumper.conf`
  (written with defaults on first creation, re-read periodically so a
  hand-edit takes effect without a restart):

  | Key | Default | Meaning |
  |---|---|---|
  | `LogRetentionDays` | `7` | Files last written this many days ago or older are deleted |
  | `LogMaxFileSizeMB` | `10` | Per-file cap before rolling to a new numbered segment |
  | `LogMaxTotalSizeMB` | `100` | Total cap across every log file - oldest files are deleted first once exceeded |

  Runs at startup, on every rollover, and periodically in the
  background — never against the file currently being written to.
- **Logs window** (§3.8): a live in-memory mirror while open — never the
  durable record, never required for anything to actually be logged.
- Logging failures (e.g. a permissions issue) degrade to "not written to
  disk" rather than crashing the app or interrupting the caller — the
  same philosophy `AppSettingsStore`/`AppConfigStore` use (§7).
