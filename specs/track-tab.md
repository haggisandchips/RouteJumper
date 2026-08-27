[← Back to spec index](../SPEC.md)

## 8. Track Tab (Ship Mode)

Only visible in Ship mode (§3.4) — Roles/Controls hidden while shown, and
vice versa. Leaner than Roles: one role ("the tracked instance"), no
macros, no cargo gating — Ship mode has no Auto Pilot to feed.

### 8.1 Instance discovery
Reuses §5.1 verbatim — same process discovery, background-thread
Refresh, empty-state message. Cards show only commander name, current
location, journal filename (no cargo/carrier fields). Clicking the
journal filename copies + confirmation sound, same as §5.3 (no visual
indicator afterward).

### 8.2 Tracking a single instance
- A single **Track** toggle per card (in place of Captain/Engineer)
  assigns/unassigns that instance — only one tracked at a time; assigning
  a new one unassigns whichever held it.
- Assigning resets the whole route (icon → None, Status → blank), then
  applies that instance's own ship's *current* status as a single
  catch-up result (§8.3) — explicitly the CMDR's own ship, never their
  fleet carrier even if the same journal has both event families. Also
  triggers a background rescan of this tab.
- Unassigning (explicit, or the instance stopping) leaves the table
  exactly as displayed — tracking simply stops. A stopped process clears
  only the in-memory assignment; the persisted FID (§7) picks it back up
  automatically if it reappears.
- Persisted by commander FID, not `ProcessId` (§7) — a restored match
  goes through the same reset-and-replay as a fresh assignment.
- Switching away from Ship mode stops watching the journal but doesn't
  clear the assignment — switching back resumes with a fresh
  reset-and-replay.

### 8.3 Journal-driven route updates
Analogous to §5.7, driven by the ship, different event vocabulary/
timing. Unlike §5.7's single combined stream, Ship mode keeps two things
independent (real play can change one without the other — repeatedly
targeting/re-targeting without jumping):

- **Current system** (drives Complete/in-progress, like §5.7's Arrived
  step) comes **only** from `Location`/`FSDJump` — both authoritative
  "ship is now at X," never `FSDTarget`/`StartJump` (intentions/in-flight
  only). Whichever fires later wins. Named system not found in the route
  at all → **nothing marked Complete** — left as-is rather than guessed.
  - `FSDJump` isn't applied immediately (too early — see Arrived below).
  - `Location` (session start, relog, or another definite-position
    snapshot) *is* applied immediately, with no wait, and never puts the
    next row into `Cooldown` (not proof a jump just completed) — it
    supersedes an `FSDJump` arrival still awaiting Music confirmation.
- **`Targeted`** is a separate overlay, never proof of arrival, never
  completing anything: `FSDTarget` sets `Targeted` on whichever
  not-yet-complete row it names (only one at a time). Elite's own
  multi-route auto-target often fires the *next* hop's `FSDTarget` while
  the *current* hop is still `Jumping`/`Cooldown` — applying it
  immediately then would mark the wrong row, so it's deferred until that
  cycle finishes (`Cooldown` clears), then applied to whichever row is
  genuinely current. `Targeted` clears — by a fresh `FSDTarget` for a
  different/off-route system, or `NavRouteClear` — without ever leaving
  its old row looking current; a row that only showed an icon *because*
  it was `Targeted` reverts fully to none. Only the one genuine current
  row keeps its icon regardless of targeting churn elsewhere.
- **`Jumping`**: `StartJump` with `JumpType` `"Hyperspace"` sets it
  immediately (`"Supercharge"`, an in-system boost, is ignored) — no lead
  time to derive, unlike Fleet Carrier mode.
- **Arrived's `FSDJump` path**: not applied off `FSDJump` directly (fires
  while still mid-transition) — the actual signal is the following
  `Music` event (`MusicTrack` `DestinationFromHyperspace` or
  `Supercruise`, which depends on some other in-game factor). `FSDJump`
  records a pending arrival; the next qualifying `Music` resolves it. A
  generous fallback timer resolves it anyway if neither track shows up
  in time — a backstop, not the expected path.
- **`Cooldown`** (shown on the row after the arrived-at one, same as
  §5.7): clears a **provisional** fixed duration after a genuinely live
  `FSDJump`/Music resolution specifically — never off `Location`. Ships
  do have a real FSD cooldown (not cosmetic dressing) — its exact
  duration hasn't been measured yet, so a placeholder is used.
- No known journal event for an aborted ship jump (no
  `StartJumpCancelled`) — an interrupted jump leaves its row stuck on
  `Jumping` until the manual "Set next system" override (§4.2); not
  auto-recovered.
- Same catch-up principle as §5.7: on assignment (or resuming after a
  mode switch), the whole journal is read first, oldest→newest, to
  compute a single result for *each* independent piece — current system
  (last of `Location`/`FSDJump`), an in-flight jump (a Hyperspace
  `StartJump` not yet superseded), a still-open target (an `FSDTarget`
  not yet superseded by `NavRouteClear` or a since-jump/arrival) — and
  only those results apply, never one event per line. Only after this
  does live tailing begin.

### 8.4 Distinguishing the ship from a fleet carrier
A commander with both a ship and a fleet carrier has both event
families in the same journal. Never confused: the ship family
(`FSDTarget`/`StartJump`/`FSDJump`) carries no `CarrierID`/`CarrierType`
at all — structurally separate, no filtering needed.

### 8.5 Auto Copy To Clipboard
Identical to §4.6 — a live ship arrival (`FSDJump`, ahead of the delayed
Arrived/Cooldown UI transition) copies the next row's system if on.

### 8.6 No Auto Pilot
No macro automation at all — the CMDR flies every jump manually. The
Route tab's Auto Pilot button is hidden (not disabled) while Ship mode
is active (§4.2); any run in progress stops outright the instant Ship
mode is switched on.
