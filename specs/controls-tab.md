[← Back to spec index](../SPEC.md)

## 6. Controls Tab

Manages named key-binding sets for the app's automation: record
keypresses/clicks against a running instance, capture as an editable
human-readable script, replay later. A macro selected for a role (§5.5)
is what Auto Pilot (§4.2) plays; nothing else here is auto-wired.

Four vertically-stacked sections: Options, Key Bindings, Running
Instances (fixed height at top); Recorded Macros fills the rest (each
section scrolls internally, no whole-page scroll).

### 6.1 Options

Two settings, side by side:
- **Auto Pilot delay (ms)**, default `5000`. Auto Pilot (§4.2) waits
  this long — on top of Cooldown itself — both after Cooldown clears
  before the Captain plots, and after Cooldown starts before the
  Engineer refuels (§4.7), letting the game's UI settle. No effect on
  Cooldown's own timing (§5.7) or a manual Play (§6.5).
- **Auto wait (ms)**, default `300`. Applied automatically after every
  leaf instruction (played, stepped, or Auto-Pilot-triggered) unless the
  next instruction is itself a `WAIT`, or (when stepping) there's no next
  instruction.

A second row, purely a testing aid (never consulted for an Auto
Pilot-triggered run, which always resolves both placeholders live):
- **Test {NEXT_SYSTEM}**, default `Sol` — what `PASTE {NEXT_SYSTEM}`
  resolves to for a manual Play/Step from this tab.
- **Test {TRITIUM_LOOPS}**, default `1` — what `REPEAT {TRITIUM_LOOPS}`
  resolves to for a manual Play/Step, skipping the CMDR rescan (§6.4)
  entirely.

**Play**/**Step** (§6.3, §6.5) are disabled while either field is blank.
Session-only, like "Auto Copy To Clipboard" (§4.6) — never persisted.

### 6.2 Key Bindings

Nine named actions, each bound to a key (optionally with modifiers, e.g.
`Ctrl+Shift+J`) — a recorded script's token vocabulary (§6.4).

| Action | Default Key Binding |
|---|---|
| UP | Up Arrow |
| DOWN | Down Arrow |
| LEFT | Left Arrow |
| RIGHT | Right Arrow |
| SELECT | Space |
| PREV_PANEL | Del |
| NEXT_PANEL | End |
| EXIT | Backspace |
| RIGHT_PANEL | 4 |

- Each shown as a button labelled with its current binding. Clicking
  enters capture mode ("Press a key…"); the next key becomes its new
  binding (a bare modifier alone doesn't complete capture — only a full
  chord ending in a non-modifier does). `Escape` cancels capture.
- Persisted individually, restored on launch, defaulting to the table
  above until rebound.

### 6.3 Running Instances

Lists running instances — scanned independently of Roles (its own
process/journal scan), Roles-styled cards limited to commander name/
window position/monitor. **Refresh** rescans on demand (also runs once
on tab construction); same empty-state message as Roles.

One shared selection for both recording and playback (§6.4, §6.5). With
exactly one instance it's auto-selected (left alone if a second later
appears); with more than one, click a card to select (clicking a
different card moves, doesn't add to, the selection).

Button row: **Refresh** (left), **New Script** (§6.5) + media-style
**Record**/**Stop**/**Play** (right, New Script icon-only). A single Stop
covers both recording and playback (only one active at a time):
- **New Script**: creates a new empty macro, opens it in the editor —
  for writing/pasting a script by hand. Enabled under the same
  nothing-else-recording/playing/stepping condition as Record, but needs
  no selected instance and works even with the editor already open for
  another macro.
- **Record**: enabled only with an instance selected, nothing already
  recording/playing, and the editor closed.
- **Play**: enabled with both an instance and a macro selected, nothing
  recording. Any instance can play any macro. Stays enabled while
  playing: pressing again cancels the running playback, starts the new
  one.
- **Stop**: enabled while recording or playing; cancels whichever is
  active immediately, wherever in the script.

### 6.4 Recording

**Record** foregrounds the selected instance's window (same as Play),
then captures keyboard/mouse system-wide, filtered to moments the target
window is foreground (input elsewhere, including back at ED:FC Auto
Pilot's own window, isn't recorded). **Stop** ends capture into a new
named macro (§6.5). Only one recording at a time.

Each captured key/left-click is classified by hold duration: ≤400ms is a
tap/click; longer is a hold, recorded with duration. Click position is
window-client-relative (`0,0` top-left), taken from button-down (drags
aren't recorded; other buttons/scrolling aren't either). A ≥150ms gap
between inputs records as an explicit wait, preserving original pacing.

Script grammar:

```
UP                    # tap the action bound to UP
KEY Control+A         # tap a raw key with no bound action
HOLD RIGHT 800        # hold an action's key for 800ms
CLICK 240,160         # left-click at a position relative to the window
HOLD CLICK 240,160 600 # left-click-and-hold at a position for 600ms
CLICK {CENTRE},{CENTRE} # left-click the exact centre of the window
WAIT 500              # pause 500ms before the next step
PASTE {NEXT_SYSTEM}   # paste text via the clipboard + Ctrl+V
REPEAT 3
    UP
    WAIT 200
END
MACRO refuel
    RIGHT_PANEL
    SELECT
END
CALL refuel
```

- A bound action name taps its current key; `KEY <chord>` taps an
  arbitrary key/chord. `HOLD` takes the same token plus a ms duration.
- `CLICK <x>,<y>` clicks that position; `HOLD CLICK <x>,<y> <ms>` clicks
  and holds. Either coordinate may be `{CENTRE}`, resolved at play time
  to that axis' current window-client midpoint — reliable even after a
  resize since it was recorded/written.
- `REPEAT <n> … END`/`MACRO <name> … END` nest; `MACRO` blocks are
  top-level only (never inside a `REPEAT`) and never play inline — `CALL
  <name>` invokes one anywhere, including inside a `REPEAT`.
- `PASTE <text>` sets the clipboard and sends `Ctrl+V`. May contain the
  literal `{NEXT_SYSTEM}`, resolved at play time to the Route tab's
  in-progress row's system (empty text if none) for an Auto Pilot run,
  or the Options **Test {NEXT_SYSTEM}** field for a manual Play/Step.
  Never produced by recording — manual `Ctrl+V` during recording is
  captured as an ordinary key chord — only ever hand-added.
- `{TRITIUM_LOOPS}` is usable anywhere a number is expected (most usefully
  `REPEAT {TRITIUM_LOOPS}`). Unlike `{NEXT_SYSTEM}`/`{CENTRE}` (resolved
  live per-instruction), it's resolved once, textually, before parsing —
  a REPEAT count must be known before the script splits into steps.

  For an **Auto Pilot-triggered run** of a script containing this
  placeholder, ED:FC Auto Pilot first refreshes CMDR info for every
  running instance, then resolves it against: this tab's own selected
  instance if one is selected, otherwise the currently-assigned Engineer
  (the normal case). The value is the number of full ship-loads of
  tritium that instance still needs to fill its carrier's fuel depot to
  1000t *and* top off its own cargo hold, net of tritium already carried
  (cargo capacity, carrier fuel level, tracked onboard tritium — §5.3).
  Unknown/zero cargo capacity, or unknown carrier fuel level, resolves to
  `0` rather than risk dividing by an unknown capacity or treating an
  unknown fuel level as an empty depot. A script without the placeholder
  skips the rescan entirely and plays immediately — the rescan is a real,
  sometimes multi-second delay, and paying it unconditionally risks the
  target's in-game state drifting before the script starts.

  That value is then capped down (never up) so the refuel script can't
  take longer than **4 minutes 45 seconds** to play — Cooldown's own
  window is 5 minutes (§5.7), triggered right around its start (§4.7);
  this margin keeps a refuel from still running when the Captain's next
  plot cancels it out from under itself (panic mode, §4.7, treats that
  as an immediate hard stop). The cap is derived by estimating the
  script's own execution time (leaf durations, `AutoWaitMs` pacing,
  `REPEAT`/`CALL` expanded as playback would) at 1 loop and at 2,
  extrapolating linearly — exact for the standard `REPEAT
  {TRITIUM_LOOPS}` shape. If a full refill's loops wouldn't fit, this
  deliberately plays however many *do* fit rather than the full amount —
  a gradually depleting depot is an accepted, safe outcome (eventually a
  jump can't be requested at all, which panic mode already catches).

  For a **manual Play or Step**, the CMDR rescan never happens — the
  placeholder resolves directly to the Options **Test {TRITIUM_LOOPS}**
  field instead.
- Blank lines and `#`-prefixed lines are ignored; malformed lines are
  skipped, not rejected (the text is meant to be hand-edited).
- Multiple steps on one line, separated by `;` (whitespace around it
  ignored), e.g. `UP; WAIT 200; DOWN` — a readability convenience with no
  effect on playback order. Never produced by recording, only by hand.

### 6.5 Recorded Macros

Lists every macro recorded (or hand-written via **New Script**, newest
last). Compact list by default, one row per macro:
- **Name**, with the rest of the row's background also clickable
  (except the pencil/Delete icons) — selects it as the Play target
  (§6.3), highlighting the row, without opening it for editing.
- A **pencil** icon opens the full-size editor for that macro.
- A **Delete** icon, always available; deleting a currently-selected/
  edited macro clears that state.

Empty state (no macro yet): italic hint — "No macros recorded yet. See
the docs' sample scripts for ready-to-use examples, or click New Script
above to write one by hand." — "sample scripts" links to the docs site's
[Sample scripts](https://haggisandchips.github.io/RouteJumper/docs/macro-scripting.html#sample-scripts)
section. Disappears for good once the first macro exists.

The source commander a macro was recorded against shows as a caption
under its name — display only, no restriction on which instance plays
it. Omitted (no blank line) for a **New Script**-created macro (no source
commander).

**New Script** (§6.3's button row) creates a new empty macro (blank
script, no source commander), opens it in the editor. Persists and
behaves identically to a recorded macro from that point (Play, Step,
rename, delete, role-macro selection all work the same).

Clicking the pencil replaces the list with a full-size **editor**,
opening also selects that macro as the Play target:
- Editable **name** field.
- A large editable **script text** box beside a narrow fixed-width
  grammar reference panel (§6.4), grouped **Actions** (`ACTION_NAME`,
  `KEY`, `HOLD`, `CLICK`, `HOLD CLICK`, `PASTE`, `# comment`),
  **Controls** (`WAIT`, `REPEAT`, `MACRO`, `CALL`), **Variables**
  (`{CENTRE}`, `{NEXT_SYSTEM}`, `{TRITIUM_LOOPS}`).
- **Save** below the script box returns to the list — edits to either
  field save as they're made, independent of Save (pure navigation).
- Key Bindings (§6.2) stays visible above the editor. **Record** is
  disabled while the editor is open.
- A **Step** button beside Save, labelled with the next leaf instruction
  (e.g. "Next: RIGHT" — a REPEAT counted by unrolled iterations, a CALL
  by the macro it inlines). Runs just that instruction against the
  selected instance, then stops. `WAIT` steps are skipped entirely
  (nothing to observe). Re-foregrounds the target window each time
  (since it won't stay foreground after clicking back on the app).
  Wraps to the beginning at the end; editing the script restarts
  stepping from the beginning. Enabled under the same conditions as
  Play, plus a macro open in the editor with at least one step. **Stop**
  also cancels a step in progress.

**Record** again while macros exist always starts a brand-new recording
as an additional macro — never overwrites or appends to an existing one.

Playing (§6.3) foregrounds the instance, then executes steps in order
via simulated system-wide input (not window messages, since a DirectX
game reads actual input device state) — resolving `PASTE {NEXT_SYSTEM}`
against the Route tab's live state. The configured Auto wait (§6.1)
applies after each step unless the next is itself a `WAIT` — same
whether started manually or by Auto Pilot (§4.7). A new playback cancels
a previous in-progress one, same as **Stop** — cancellable at any point,
not just between steps.

If the target window loses focus mid-playback, playback aborts
immediately and a closeable, solid-fill warning banner shows above the
tab area (visible regardless of active tab) until dismissed or another
playback starts. A user-initiated Stop is not treated as this failure
and shows no message.

### 6.6 Persistence
See §7.
