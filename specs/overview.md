[← Back to spec index](../SPEC.md)

## 1. Purpose

ED:FC Auto Pilot is a desktop utility for Elite Dangerous commanders, with two
mutually exclusive tracking modes (§3.4). In **Fleet Carrier mode** (the
default), a user pastes a list of star systems (one per line) to form a
route; once a Captain's game instance is assigned on the Roles tab,
ED:FC Auto Pilot watches that commander's journal file and automatically tracks
the carrier's progress along the route, updating each row's status as the
carrier plots, jumps, arrives, and cools down — with an optional Auto
Pilot that drives the whole route via recorded macros. In **Ship mode**
(§8), a solo commander instead pastes a manually hand-picked route (e.g.
from a neutron-highway plotter) and assigns their own running instance on
the Track tab; ED:FC Auto Pilot tracks the same row-by-row progress against
that commander's own ship journal, purely passively — there is no
automation, since the CMDR must plot and fly every jump themselves.

---

## 2. Scope

| In scope | Out of scope |
|---|---|
| Route tab: paste a route, save it to a table, track progress against a real journal | Itemized cargo inventory (commodity-by-commodity) — only total tonnage is shown |
| Roles tab: detect running Elite Dangerous instances; assign Captain/Engineer roles | Configurable journal-match tolerance or cooldown timing |
| Event-driven row-progress engine, driven by a Captain's journal (Fleet Carrier mode) or a commander's own ship journal (Ship mode, §8) | Persisting per-row progress (icon/status), or the last-selected tab |
| Manual "Set next system" override for correcting automatic detection | |
| Selecting a macro for each of Captain/Engineer (§5.5), gating Auto Pilot on that selection, and Auto Pilot playing the Captain's macro to plot each jump and (if Engineer is assigned) the Engineer's to refuel each Cooldown (§4.7) | Auto Pilot retrying a jump after `CarrierJumpCancelled` (§4.7) |
| Persistence of route text, window bounds, Captain/Engineer role assignment, tracking mode, and tracked instance (§7) | |
| "Auto Copy To Clipboard" for the next system, plus a clipboard-source indicator | |
| Controls tab: key bindings, running-instance scan, and recording/playback of macros (§6) | |
| Spoken lead-time announcements before Auto Pilot plots/refuels (§4.8), a File menu (§3.4) with Preferences (voice/volume/test, plus an update-check opt-out) and Exit, a Help menu with Logs (§3.8), Check for Updates (§3.7), and About (§3.6), a Fleet Carrier/Ship mode toggle, and an always-visible mute button | |
| **Ship mode** (§8): a Track tab for picking a single instance to passively track via that commander's own ship journal, with no Auto Pilot/macro automation at all | Fuel management (warning how many jumps remain before running dry) — a planned future enhancement, not built yet |
| A top-level **Spansh** menu (§3.4) holding **Fleet Carrier…**/**Neutron Plotter…**/**Galaxy Plotter…** (§4.12): calculate a route between two systems via Spansh's own route-plotting API and import it straight into the Route tab, available in both tracking modes - the Galaxy Plotter tab additionally uses the CMDR's own real ship build (re-read from that instance's journal in the background) to plot an exact route accounting for fuel usage and supercharge/injection options | Proper on-screen credit for EDSM specifically (§4.9) — a planned future enhancement, not built yet. (Spansh, §4.12, already carries its own in-dialog credit.) |
| Material Design styling | |
| Importing whatever route is currently plotted in-game ("Import Current Route", §4.10), unconditionally, in both tracking modes; trimming a saved route down to a max-500ly-per-hop series of jumps (§4.11, "Trim for FC", Fleet Carrier mode only) - both apply immediately after a Yes/No confirmation, with no in-between review step | |
| A companion site (§13): a QR code/link next to the Auto Pilot button opens a small, mobile-friendly Angular site (hosted alongside the docs via GitHub Pages) showing a live feed of an in-progress Auto Pilot run - route/jump plotted, arrived in system, refueled, panic-mode stop - plus a per-event Delete button to permanently remove a clutter/unwanted event | Any authentication beyond a session's own unguessable URL, and any companion-site control over Auto Pilot itself (e.g. remotely starting/stopping/plotting) - a planned future enhancement, not built yet |
