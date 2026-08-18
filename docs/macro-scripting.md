---
title: Macro Scripting
---

# Macro Scripting

A macro is a small, line-oriented, hand-editable text script. Recording
(Controls tab) produces this text automatically, but you're free to
write or edit it by hand — the parser is deliberately forgiving (a
malformed line is just skipped, not rejected).

See the [User Guide](index.md#controls-tab) for how recording, playback,
and the editor work in the app itself.

---

## Syntax reference

| Syntax | Meaning |
|---|---|
| `UP`, `DOWN`, `LEFT`, `RIGHT`, `SELECT`, `PREV_PANEL`, `NEXT_PANEL`, `EXIT`, `RIGHT_PANEL` | Tap the key currently bound to that action (Controls tab) |
| `KEY <chord>` | Tap an arbitrary key not tied to a named action, e.g. `KEY Control+A` |
| `HOLD <token> <ms>` | Press-and-hold an action or `KEY ...` token for `<ms>` milliseconds |
| `CLICK <x>,<y>` | Left-click at a position relative to the game window's client area |
| `HOLD CLICK <x>,<y> <ms>` | Click-and-hold at a position for `<ms>` milliseconds |
| `{CENTRE}` | Usable in place of an `x` or `y` coordinate — resolves to that axis' midpoint at play time |
| `WAIT <ms>` | Pause before the next step |
| `PASTE <text>` | Sets the clipboard to `<text>` and sends Ctrl+V |
| `{NEXT_SYSTEM}` | Placeholder inside a `PASTE`, resolved at play time to the Route tab's current next system |
| `REPEAT <n>` … `END` | Repeats its body `n` times (nestable) |
| `{TRITIUM_LOOPS}` | Placeholder usable anywhere a number is expected (most often `REPEAT {TRITIUM_LOOPS}`) — resolved to how many full ship-loads of tritium are still needed to top off the carrier's fuel depot and the ship's own hold, capped (during an Auto Pilot-triggered run only) so the script can never take longer than 4m45s to play, however many loops the CMDR's cargo/fuel actually implies |
| `MACRO <name>` … `END` | Defines a named, reusable sub-routine (top level only) |
| `CALL <name>` | Invokes a macro defined with `MACRO` |
| `# comment` | Ignored, like a blank line |
| `A; B; C` | Multiple steps on one line, separated by `;`, purely for readability |

Every leaf instruction is automatically followed by the configured
**Auto wait** delay (skipped only if the next instruction is itself a
`WAIT`), so a script doesn't need an explicit `WAIT` after every single
step.

---

## Sample scripts

Three ready-to-use scripts are included below to get you started —
covering the Captain's jump-plotting routine and both ways an Engineer
can refuel the carrier (buying Tritium from a station market, or
transferring it from the carrier's own hold). To use one of these:

1. On the **Controls** tab, click the **New Script** icon (the
   file-plus icon next to Record/Stop/Play, in the Running Instances
   section) — this creates a new, empty macro and opens it straight in
   the editor.
2. Give it a clear **name** (e.g. "Plot Next System"), then paste one of
   the scripts below into the script box.
3. On the **Roles** tab, pick it under **Captain plots via** or
   **Engineer refuels via** as appropriate.

All three assume the in-game **Right Panel** key binding is set to open
the ship's right-hand panel from the **Home** tab (per their own
`NOTE` comments) — make sure `RIGHT_PANEL` (Controls tab) matches your
in-game binding before using them.

### 1. Plot Next System (Captain)

Plots a jump to the route's next system via the Galaxy Map. Only
appropriate for the **Captain** role.

```
#######################################################################
#                                                                     #
# Plots the next system.                                              #
# This script is only appropriate for the Captain role.               #
#                                                                     #
# NOTE: This script requires Right Panel to be set on the Home tab.   #
#                                                                     #
#######################################################################


# Open Right Panel
RIGHT_PANEL; WAIT 3000

# Ensure top left
REPEAT 3; UP; LEFT; END

# Open Carrier Management
DOWN; RIGHT; SELECT; WAIT 5000

# Open Galaxy Map
DOWN; SELECT; WAIT 500; SELECT; WAIT 5000

# Select System
UP; SELECT; PASTE {NEXT_SYSTEM}; WAIT 500; DOWN; SELECT; WAIT 5000

# Plot Jump
HOLD CLICK {CENTRE},{CENTRE} 1000; WAIT 5000

# Exit
REPEAT 12; DOWN; END; SELECT
```

### 2. Refuel Via Market (Engineer)

Refuels the carrier by buying Tritium from a docked station's Commodity
Market, then donating it (and anything already in the ship's hold) to
the carrier's Tritium Depot — looped `{TRITIUM_LOOPS}` times. Assumes
Tritium is the only commodity for sale at that market.

```
#######################################################################
#                                                                     #
# Refuels the carrier by purchasing Tritium from the Market.          #
#                                                                     #
# It assumes that Tritium is the only commodity for sale. If that is  #
# not the case then adjust the script to select Tritium correctly.    #
#                                                                     #
#######################################################################


MACRO DonateTritium
    # Enter Titium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Click Donate Tritium
    SELECT

    # Confirm deposit (does nothing if no tritium on ship)
    UP; SELECT

    # Exit Tritium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Return Top Left
    UP; UP
END

MACRO BuyTritium
    # Enter Commodity Market
    RIGHT; RIGHT; SELECT; WAIT 1000

    # Buy Tritium
    RIGHT; SELECT; WAIT 500; UP; UP; HOLD RIGHT 5000; WAIT 500; DOWN; SELECT; WAIT 1000

    # Exit Commodity Market
    LEFT; REPEAT 4; DOWN; END; SELECT; WAIT 1000
END

#
# Start Routine
#

# Enter Carrier Services
SELECT; WAIT 5000

REPEAT {TRITIUM_LOOPS}
    # Donate anything the ship is carrying
    CALL DonateTritium

    # Buy more ... this is the last step so jump is plotted with this tritium effectively
    # missing and therefore not counted towards the tritium required by the jump.
    CALL BuyTritium
END

# Exit
EXIT
```

### 3. Refuel Via Transfer (Engineer, carrier owner only)

Refuels the carrier by transferring Tritium straight out of the
carrier's own cargo hold, rather than buying it — only appropriate when
the Engineer's commander is the carrier's owner. Reliable only when
Tritium is the *only* commodity in the carrier's hold (otherwise its position in the
transfer screen isn't fixed).

```
#######################################################################
#                                                                     #
# Refuels the carrier by transfering Tritium from the carrier's hold. #
# This script is only appropriate when Engineer is the carrier owner. #
#                                                                     #
# It is only reliable when Tritium is the only commodity in the       #
# carrier's hold. If not the location of Tritium in the transfer      #
# screen is indeterminate and changes depending on the ship's hold    #
# contents.                                                           #
#                                                                     #
# NOTE: This script requires Right Panel to be set on the Home tab.   #
#                                                                     #
#######################################################################

MACRO DonateTritium
    # Enter Titium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Click Donate Tritium
    SELECT

    # Confirm deposit (does nothing if no tritium on ship)
    UP; SELECT

    # Exit Tritium Depot
    DOWN; DOWN; SELECT; WAIT 1000

    # Return Top Left
    UP; UP
END

MACRO TransferTritium
    # Open Right Panel
    RIGHT_PANEL; WAIT 3000

    # Access Transfer (allowing for Inventory already containing something)
    RIGHT; UP; UP; RIGHT; SELECT

    # Select Tritium (REQUIRES TRITIUM TO BE THE ONLY ITEM IN THE HOLD)
    # NOTE: If Tritium is NOT the only item it is indeterminate whether it will be at the top or elsewhere
    UP

    # Transfer Max Tritium
    HOLD LEFT 5000; SELECT; SELECT

    # Close Right Panel
    EXIT; EXIT
END

#
# Start Routine
#

# Enter Carrier Services
SELECT; WAIT 5000

# Open Right Panel and navigate to Inventory, then close panel
RIGHT_PANEL; WAIT 3000; REPEAT 4; NEXT_PANEL; END; EXIT

REPEAT {TRITIUM_LOOPS}
    # Donate anything the ship is carrying
    CALL DonateTritium

    # Transfer ... this is the last step so jump is plotted with this tritium effectively
    # missing and therefore not counted towards the tritium required by the jump.
    CALL TransferTritium
END

# Open Right Panel and navigate to Home, then close panel
RIGHT_PANEL; WAIT 2000; REPEAT 4; PREV_PANEL; END; EXIT

# Exit
EXIT
```
