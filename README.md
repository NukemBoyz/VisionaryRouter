# Visionary Router

A Windows desktop control panel for routing Visionary AV-over-IP encoders to one or more decoders.

**[Download Visionary Router for Windows (.zip)](https://github.com/NukemBoyz/VisionaryRouter/releases/download/v1.2.0-beta.1/VisionaryRouter-Windows-x64.zip)**

1. Download the ZIP and choose **Extract All**.
2. Open the extracted **Visionary Router** folder.
3. Double-click **VisionaryRouter.Desktop.exe**. Keep the `server` folder alongside it.

No installer, browser, or separate .NET installation is needed. Windows x64 only.

> This is an unsigned preview. Windows Smart App Control may block it. Trusted signing is not yet available; this release does not solve that restriction.

## Routing and Deck mode

Select an encoder, select one or more decoders, then press **Route**. Routes are checked against the devices rather than assumed successful.

Click **Deck mode** for a resizable AV-style control surface with large encoder/decoder buttons and a button-size slider. Deck mode has no HDMI/USB-C controls: it routes the encoder's current stream. Encoder input switching stays separate in the main window.

The desktop also includes device discovery and management, destination groups, presets, and diagnostics. Search and paging keep large device lists manageable; the device table uses virtualization. Selections persist across pages.

Saved devices and settings are kept outside the app folder, normally in `C:\ProgramData\VisionaryRouter`, with a per-user fallback when necessary. Closing the main window stops only a routing engine started by that window; it does not change the hardware's existing routes.

## Preview validation

- Desktop and Deck code build successfully.
- Earlier desktop checks exercised 10,000 synthetic devices while rendering at most 24 routing cards. This is a UI check, not a hardware capacity claim.
- New Deck checks compile, but Windows Application Control blocked their execution on the development PC. Deck runtime/hardware verification remains pending.
- The last engine regression run had 80 passing tests; 17 were blocked by Windows Application Control loading the rebuilt assembly.

## Build from source

Use the .NET 10 SDK on Windows:

```powershell
dotnet build VisionaryRouter.slnx -c Release
dotnet test tests/VisionaryRouter.Tests -c Release
dotnet run --project tests/VisionaryRouter.DesktopChecks -c Release
./scripts/publish-desktop.ps1
```

The desktop uses WPF and a bundled local routing engine. The browser surface is disabled in desktop packages. No account or cloud service is needed to operate the router.

See [Deck mode](docs/DECK-MODE.txt), [release/signing plans](docs/RELEASE-DISTRIBUTION.txt), and [legacy server/protocol documentation](docs/LEGACY-SERVER.md).

## License

[MIT](LICENSE). Bundled dependencies retain their [third-party licenses](licenses/README.txt).
