[← Back to spec index](../SPEC.md)

## 9. Non-Functional Requirements

- **Framework:** WPF, .NET 8 (`net8.0-windows`), MVVM throughout — no
  business logic in code-behind. The window-placement, focus-management,
  clipboard-monitoring, and Preferences-dialog-opening code in
  `MainWindow.xaml.cs`/`RouteView.xaml.cs` is the sole deliberate
  exception (needs direct HWND/message-pump access a ViewModel lacks).
- **Responsiveness:** the UI stays interactive throughout a Roles tab
  refresh (background-thread scan).
- **No external dependencies** beyond .NET/WPF base libraries,
  `MaterialDesignThemes`/`MaterialDesignColors`, `Microsoft.Data.Sqlite`,
  `System.Speech` (§4.8), and `QRCoder` (companion QR code, §13).
- **Network:** Distance/Star Type (§4.9) is the first outbound
  third-party call — [EDSM](https://www.edsm.net), free, no API key,
  best-effort, cached for the session (persisted indefinitely only for
  the rarer EDSM-can't-resolve case, §4.9/§7), sending only system names.
  The Spansh dialog (§4.12) is the second — used with the author's
  express permission against undocumented endpoints; the Fleet
  Carrier/Neutron Plotter tabs send only the two chosen system
  names/ids; Galaxy Plotter also sends derived fuel/mass/tank-capacity
  numbers from `Loadout` (§4.12), never raw journal data or anything
  commander-identifying. The companion site's Firestore REST calls
  (§13) are the third — unauthenticated, sending only route/row system
  names, status text, and timestamps — never commander/journal data.
  The fourth is a direct call to GitHub's own REST API (§3.6) to read
  the installed release's real `published_at` date/time for the About
  dialog — unauthenticated, on demand only (when About is opened), never
  on launch, sending nothing but the version tag being looked up; this is
  separate from Velopack's own internal GitHub Releases polling (§3.7),
  which has no such metadata seam. Every call here is
  best-effort/fire-and-forget; a failure never blocks Auto Pilot or
  tracking, only logged (category `Companion`/`Update`, §12).
