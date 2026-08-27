[← Back to spec index](../SPEC.md)

## 5. Roles Tab

### 5.1 Process discovery
- Every refresh (initial load, or Refresh button) enumerates running
  `EliteDangerous64.exe` processes. Zero → centered italic empty-state
  message. Refresh runs on a background thread (button disables for the
  duration; app stays interactive); runs automatically on startup and on
  every click.
- While Captain is assigned, a live `CarrierStats` event for their
  carrier (logged on opening Carrier Management) also triggers a
  background refresh automatically.

### 5.2 Journal matching
- No OS-level link between a process and its journal file. Every
  candidate (`Journal.*.log`, in the configured journal folder) is
  timestamped via its own `Fileheader` event.
- The journal folder comes from `routejumper.conf`'s `JournalDirectory`
  entry (plain-text `Key=Value`, beside `routejumper.db` in
  `%LocalAppData%\EDFCAutoPilot`) — created on first read defaulting to
  Frontier's standard Saved Games location, hand-editable with no
  restart required (re-read fresh every Roles refresh).
- Each process matches the journal whose `Fileheader` timestamp is
  closest to its start time, within 5 minutes — greedy, one-to-one. No
  match within tolerance → "Not found"/"Unknown" for journal-derived
  fields.
- Live `Status.json`/`Cargo.json` are never used (per-installation, not
  per-instance) — everything instance-specific comes from that
  instance's own journal.

### 5.3 Per-instance card

| Field | Source |
|---|---|
| Commander name, FID | Latest `Commander` event |
| Cargo | Latest `Loadout` (capacity) + latest ship `Cargo` (current, defaulting to 0 if none seen this session), shown as `current / capacity`t, tritium included in the total with a separate `Nt tritium` sub-line (omitted when zero) |
| Current location | Latest of `Location`/`Docked`/`Undocked`/`FSDJump`/`CarrierJump` (while docked) |
| Fleet carrier | Name from `CarrierStats` ("Unnamed carrier" if Carrier Management never opened); location from `CarrierJump`/`CarrierLocation` matched by `CarrierID`, `FleetCarrier` type only |
| Carrier fuel | Tritium tank level (0–1000t), `Nt fuel` sub-line, omitted until known — from whichever of `CarrierStats.FuelLevel`/`CarrierDepositFuel.Total` was most recent for the commander's own resolved `CarrierID` |
| Journal file | Filename; click copies + confirmation sound (no visual indicator afterward, unlike §4.6) |
| Window handle | `Process.MainWindowHandle`, hex |
| Window position | `GetWindowRect` |
| Monitor | `MonitorFromWindow` + `GetMonitorInfo` |

- All fields found in one sequential journal pass, taking the latest
  occurrence of each event.
- Cargo excludes tritium for Engineer eligibility (§5.6) but includes it
  in the displayed total. Tritium tracked incrementally across `Cargo`/
  `CargoTransfer`/`CarrierDepositFuel`/`MarketBuy`/`MarketSell`,
  resyncing from a `Cargo` event's `Inventory` breakdown when present.
- Frontier only logs `Cargo` on an actual change, so a ship that's
  carried nothing this session never gets one — current cargo/tritium
  default to 0, not "Unknown"; Engineer eligibility follows from
  `Loadout`'s capacity alone even before any `Cargo` event.
- Carrier fuel is an absolute reading (latest wins per `CarrierID`), but
  still resolved against the commander's own confirmed `CarrierID` first
  — a squadron-carrier deposit is never shown as theirs.
- No main window yet, or exits mid-scan → window/monitor fields degrade
  to "Unknown" without failing the whole scan.
- Cards stretch full tab width, vertically-stacked scrollable list.

### 5.4 Role assignment
- **Captain**/**Engineer**, each assignable to at most one instance (not
  necessarily the same one); toggle buttons per card.
- **Engineer** can't be assigned to zero/unknown cargo capacity.
- Assigning **Captain** resets the whole route, then replays that
  instance's journal (§5.7). Clearing Captain (explicit, or its instance
  stopping) leaves the route as displayed. A new instance never disturbs
  assignments already held elsewhere. Assigning Captain also triggers a
  background rescan of all instances.

### 5.5 Role macros
Above the instance cards: **Captain plots via**/**Engineer refuels via**
— each a dropdown of every recorded macro (§6.5) by name, plus a clear
button. Belongs to the role itself, not whichever instance holds it —
persists across role reassignment, independent of whether the role is
currently assigned. A deleted selected macro clears back to unset.

Auto Pilot (§4.2) enables once Captain is assigned with a macro selected
here, and — only if Engineer is *also* assigned — a macro for Engineer
too; an unassigned Engineer imposes no macro requirement.

### 5.6 Persistence of role assignment
See §7.

### 5.7 Journal-driven route updates

Once Captain is assigned, watches that commander's journal for
`CarrierJumpRequest`/`CarrierLocation` belonging to their own fleet
carrier (filtered by `CarrierID` + `CarrierType == "FleetCarrier"`, so a
shared squadron carrier is never mistaken for theirs).

- On assignment, the whole journal is read first to derive a single
  authoritative "current status" — **not** by replaying every line as
  its own event. Decided by journal **line order** (not each event's
  `timestamp`), walking oldest→newest, tracking whichever of
  `CarrierJumpRequest`/`CarrierLocation`/`CarrierJumpCancelled` was seen
  *last*: an unresolved `CarrierJumpRequest` → its target is current,
  `Status` → `Plotted` (and `Jumping` if due). Otherwise, the carrier is
  wherever the most recent `CarrierLocation` placed it — that row (and
  everything before) → Complete, next row → in-progress. Only this one
  derived result applies, as a single event; after that, new lines
  live-tail via `FileSystemWatcher`, one at a time.
- This matters because a carrier's real path isn't necessarily a clean
  forward walk — a system can be revisited (e.g. a tritium depot).
  Replaying every historical line oldest-to-newest would complete that
  row on its *first* match, permanently excluding later genuine visits.
  Computing the single final state first avoids this.
- Matching a row to an event is always by System name
  (case-insensitive), the row's *first* occurrence (the route is always
  freshly Reset first). A match silently completes every earlier
  not-yet-complete row — how one result catches up several rows at once.
  A genuinely repeated waypoint (a circular route) is the one case
  "first occurrence" can point wrong — the manual "Set next system"
  override (§4.2) exists for exactly that.
- This "complete every earlier row" applies equally to the one-off
  catch-up and to an ordinary live `Plotted`/`Arrived` event — even one
  targeting a row further ahead than expected (the carrier's real path
  skipped a pasted row). Both are authoritative confirmed progress, live
  or replayed alike. `Targeted` (Ship mode, §8.3) never sweeps anything —
  only ever an intention.

**Transitions and timing** (measured from each journal event's own
timestamp, never read-time; fires immediately if already passed during
history replay, otherwise via a non-blocking timer):

1. `CarrierJumpRequest` → `Status` → `Plotted` immediately.
2. 3 minutes before that event's `DepartureTime` → `Status` → `Jumping`.
3. 1 minute after the matching `CarrierLocation`'s timestamp → arrived-at
   row → Complete, blank status; next row (if any) → in-progress.
   **Only if this `CarrierLocation` was observed live** does the next
   row's `Status` also → `Cooldown` (never as a side effect of
   catch-up/replay — no reliable way to place a stale cooldown window
   against the current clock). Nothing put into Cooldown with no next
   row.
4. A further 4 minutes later (5 min after `CarrierLocation` total) →
   next row's `Cooldown` clears, if showing one.

`CarrierJumpCancelled` (filtered by `CarrierID` only — no `CarrierType`
field) reverts whatever the cancelled request did: whichever row is
in-progress with `Status` `Plotted`/`Jumping` (at most one) → `Status`
clears to blank, icon stays in-progress. Found by state, not name (the
event carries none). Any already-scheduled `Jumping` transition (step 2)
is cancelled too, so a stale timer can't re-fire it.

- Assigning Captain resets every row (`Icon → None`, `Status →` blank)
  before replaying — no previous Captain's progress lingers.
- Event-driven by design (CLAUDE.md): every row mutation is a direct
  response to an event, never a hardcoded delay in the row-update logic
  itself — real-world scheduling lives in the journal watcher, which
  only ever reacts to "now".

### 5.8 Persistence of role assignment (FID-based)
See §7.
