[← Back to spec index](../SPEC.md)

## 10. Styling — Material Design

- `App.xaml` merges a `materialDesign:BundledTheme` (`PrimaryColor="Blue"`,
  `SecondaryColor="Cyan"`, `BaseTheme="Light"`) plus
  `MaterialDesign2.Defaults.xaml`. No dark/light theme toggle.
- `MainWindow` uses `MaterialDesignWindow` so the title bar is themed too.
- Row/status icons and the "Auto Copy To Clipboard" switch use the
  toolkit's `PackIcon`/`ToggleButton` (`MaterialDesignSwitchToggleButton`),
  colored from `MaterialDesign.Brush.Secondary`, never hardcoded colors.
