# OverWatch ELD Overlay

This folder adds the first Route Advisor-style overlay window for OverWatch ELD.

## Current behavior

- Launches automatically from `App.xaml.cs` after the login window opens.
- Creates a borderless, transparent, always-on-top WPF overlay.
- Positions itself near the top-right of the screen.
- Shows starter ELD sections for duty, HOS, load, truck data, and maintenance.
- `F9` toggles overlay visibility when the overlay has keyboard focus.
- `F10` toggles locked/unlocked mode when the overlay has keyboard focus.
- When unlocked, drag the overlay to reposition it.

## Next step

Map real ATS/ELD telemetry values into `OverlayWindow.RefreshOverlayData()`.

Suggested fields:

- Duty status
- Drive time remaining
- Active load
- Pickup and delivery city
- Truck speed
- Fuel
- Maintenance status

## Note

This starter intentionally avoids DirectX/game injection. It is safer for ATS compatibility and easier to maintain.
