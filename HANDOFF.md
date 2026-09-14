# Visionary Router — Implementation Handoff

**Canonical repository:** `C:\Users\Admin\Documents\Programming\VisionaryRouter`
**Last audited:** 2026-09-14
**Audited commit before this documentation update:** `c816c92` (`master`, since renamed `main`)
**Hardware status:** initial E4200/D4200 query and routing validation completed; visual and full acceptance testing remain. The clear-then-reroute fix of 2026-09-14 is simulator-tested only - the bench link dropped before it could be confirmed on the decoders that showed the fault.

This file is the continuity point for Codex, Claude, or another engineer. Read it with `README.md` before changing the project. Treat the code and tests as authoritative when an older planning document disagrees.

## Current State

The first functional vertical slice is already implemented. This is not a research-only folder.

Implemented:

- .NET solution with Core, Server, Simulator, Probe, and Tests projects
- Visionary command builder and tolerant response parser
- UDP unicast transport with timeout, retry, cancellation, sender-IP filtering, and per-endpoint locking
- Manual device add/edit/delete, probe, identify, enable/disable, and friendly names
- SQLite-backed device persistence
- Verified 1-to-1 and 1-to-many decoder routing with bounded concurrency
- Per-decoder partial-failure results
- Decoder `STREAM.HOST` polling and encoder health polling
- Detection of externally changed routes
- Source-centric browser UI, device page, and diagnostics page
- Simplified Operators tab with only source cards, destination cards, and routing action
- Live UI updates using Server-Sent Events (SSE)
- Fake Visionary endpoints, including deliberate timeout and accepted-but-not-switched failures
- Standalone real-hardware probe CLI
- Route/source color visualization in the UI
- Verified per-encoder HDMI 1, HDMI 2, and USB-C input controls
- Explicit IPv4 `/24` device scan that adds confirmed endpoints and refreshes existing matches
- Decoder groups and routing presets, stored as JSON in the `Settings` table
- Optional HTTP basic authentication, required before the UI may bind to all interfaces
- Diagnostics UI for selecting specific listening interfaces by stable Windows adapter ID
- SQLite-backed Web UI port selection with automatic startup collision recovery
- Tested self-contained Windows x64 ZIP requiring no installed .NET runtime or installer
- Self-contained Ubuntu x64/ARM64 packages with interactive systemd installation
- Credential redaction at the protocol boundary

Not implemented:

- Full real-hardware acceptance testing (basic E4200/D4200 routing has passed)
- Visual confirmation that a routed display actually changes picture
- Multicast discovery on `226.0.0.19`
- Traditional matrix view
- Windows Service integration and installer
- Backup/export/import
- HTTPS (basic authentication exists; the transport is still plain HTTP)
- Capability profiles for distinct 4-Series/5-Series/X5K behaviors

## Verified Build State

On 2026-09-13, using Windows x64 and SDK `10.0.400`:

```text
dotnet build VisionaryRouter.slnx --configuration Release --no-restore
Build succeeded. 0 warnings, 0 errors.

dotnet test VisionaryRouter.slnx --configuration Release
Passed: 97, Failed: 0, Skipped: 0.
```

The repository was clean before this handoff/README documentation change.

## Runtime and Packaging Decision

Every project targets `net10.0`, and the direct Microsoft package references are `10.0.11`.
The deployment choice is now a self-contained Windows x64 package, so the operator machine
does not need an installed .NET runtime. Preserve `net10.0` unless the owner explicitly asks
for a coordinated retarget and full regression pass.

`scripts/publish-windows.ps1` runs Release tests, publishes using
`Properties/PublishProfiles/Windows-x64.pubxml`, and creates
`artifacts/VisionaryRouter-win-x64.zip`. The package contains the single-file
`VisionaryRouter.exe`, static Web UI assets, configuration, and `README-FIRST.txt`. It is
portable rather than installed and is not currently code-signed.

`scripts/publish-ubuntu.ps1` builds `linux-x64` and `linux-arm64` self-contained archives.
Each contains an interactive installer, uninstaller, quick-start guide, and hardened systemd
unit. Linux deployment uses `/opt/visionary-router` for application files,
`/var/lib/visionary-router` for durable data, and `/etc/visionary-router` for protected
configuration. Server-side invariant globalization removes the ICU package dependency.

## Implemented Architecture

```text
Visionary endpoints / simulator
          |
          | UDP unicast, normally port 8000
          v
VisionaryRouter.Core
  Protocol/VisionaryCommand.cs
  Protocol/VisionaryResponse.cs
  Protocol/VisionaryUdpTransport.cs
  Devices/VisionaryDeviceClient.cs
  Routing/RoutingService.cs
          |
          v
VisionaryRouter.Server
  Minimal ASP.NET Core APIs
  DeviceRegistry + SQLite DeviceStore
  PollingService
  EventStream (SSE)
  static HTML/CSS/JavaScript UI
```

The early design proposed React/TypeScript and SignalR. The implemented UI deliberately uses plain HTML/CSS/JavaScript with no Node build and SSE for server-to-browser updates. This is working and should not be rewritten merely to match the planning document.

## Protocol Contract Used by the Code

```text
UDP control port: 8000
Documented multicast control address: 226.0.0.19
Frame start: CMD=START
Frame end: CMD=END
Fields: ampersand-separated KEY=VALUE pairs
Keys: case-sensitive
```

Commands generated by `VisionaryCommand`:

```text
CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END
CMD=START&UNIT.ID=ALL&QUERY.ALL=TRUE&CMD=END
CMD=START&UNIT.ID=ALL&QUERY.KEY=STREAM.HOST&CMD=END
CMD=START&UNIT.ID=ALL&STREAM.HOST=<ENCODER_IP>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
CMD=START&UNIT.ID=ALL&UNIT.FU=TRUE&CMD=END
```

The route command is sent to each decoder. One encoder to many decoders is a set of independent decoder operations. A switch acknowledgement is not sufficient: `RoutingService` waits, queries `STREAM.HOST`, and only reports `Verified` when it equals the requested encoder IP.

The command builder refuses `SAVE`, `UNIT.SAVE`, `UNIT.RESET`, and `UNIT.REBOOT`. Routine switching must remain volatile. Do not add persistence writes without confirming current official API semantics and receiving an explicit product requirement.

`VisionaryResponse.Parse` redacts credential-bearing fields before returning, so a secret disclosed by an endpoint never enters application memory. Tested firmware returns `UNIT.PASSWORD` in `QUERY.ALL`, and raw responses reach the probe console, debug logs, the HTTP API and the browser activity panel. Redaction is therefore done once at the boundary rather than at each of those call sites. See `VisionaryRedaction`; the simulator reproduces the disclosure so the behavior is testable without hardware. Do not add a code path that returns the unredacted text.

## Runtime Defaults

```text
Web UI: http://localhost:8088
Listen on selected network interfaces: none (localhost only)
Open browser on start: true
UDP query timeout: 1000 ms
UDP retry count: 1 additional attempt
Route verification delay: 400 ms
Route verification retries: 3
Verification retry interval: 400 ms
Maximum route/poll concurrency: 8
Decoder route poll interval: 3 seconds
Encoder heartbeat interval: 10 seconds
```

The preferred setup is **Diagnostics → Network Access**. It lists every active IPv4 adapter,
allows the operator to select only the interfaces that should serve the UI, and requires an
administrator password before any non-loopback interface can be saved. Selections and the
PBKDF2-SHA256 password hash are stored in SQLite; plaintext passwords are never stored.
Adapter IDs, rather than DHCP addresses, are persisted and resolved at the next startup.
Changes require an application restart. Localhost always remains enabled.

The legacy `Router:ListenOnAllInterfaces=true` configuration is still supported and requires
`Router:AdminPassword`; the server throws at startup rather than serving an unauthenticated
control surface to the LAN. The transport is still plain HTTP, so use this only on trusted
networks. Windows Firewall must separately allow inbound TCP on port 8088; the application
does not create or modify firewall rules.

The SQLite database is normally:

```text
C:\ProgramData\VisionaryRouter\router.db
```

If that location cannot be created, the server falls back to the current user's local application-data `VisionaryRouter` folder.

## Running Locally

From the repository root, open two terminals:

```text
dotnet run --project src/VisionaryRouter.Simulator
dotnet run --project src/VisionaryRouter.Server
```

The simulator exposes fake endpoints on loopback addresses in the `127.0.0.10`–`127.0.0.24` range. The README identifies the intentionally broken endpoints. Add the simulated devices from the Devices page and exercise verified and failed routes.

Run checks with:

```text
dotnet build VisionaryRouter.slnx --configuration Release
dotnet test VisionaryRouter.slnx --configuration Release
```

## First Real-Hardware Gate

Use the probe before trusting the server UI:

```text
dotnet run --project src/VisionaryRouter.Probe -- info <DECODER_IP>
dotnet run --project src/VisionaryRouter.Probe -- info <ENCODER_IP>
dotnet run --project src/VisionaryRouter.Probe -- route <DECODER_IP> <ENCODER_IP>
```

Record the complete raw TX/RX output, endpoint models, firmware versions, and whether the display actually changes. `STREAM.HOST` matching is necessary but does not prove that multicast video, HDCP, EDID, or display output is working.

Required hardware acceptance sequence:

1. Probe one encoder and one decoder.
2. Route Encoder A to Decoder 1 and visually confirm picture plus API verification.
3. Route Encoder B to Decoder 1 and confirm again.
4. Route one encoder to at least three decoders.
5. Include one powered-off decoder and confirm partial failure without blocking others.
6. Change a route in Vision Lite and confirm this UI reconciles within a few seconds.
7. Restart the application and confirm it does not send route commands.
8. Test representative 4-Series and 5-Series/X5K units if both families are in scope.

## Production API Validation

Request the current official **5-Series/X5K API specification** from Visionary Solutions before production release. Confirm at minimum:

- UDP port and response source behavior
- Exact `STREAM.HOST` routing semantics
- `STREAM.CONNECT` acknowledgement values
- Empty/no-route representation
- `QUERY.STATUS`, `QUERY.ALL`, and `QUERY.KEY` responses
- `UNIT.SAVE` and volatility behavior
- Model and firmware metadata keys
- Differences between 4-Series, 5-Series, Duet, and X5K devices

The simulator was built from the same public documentation as the application. It validates our software behavior, not the vendor protocol assumptions.

## Important Implementation Rules

1. Keep raw protocol construction inside `VisionaryCommand`.
2. Keep socket behavior inside `VisionaryUdpTransport`.
3. Preserve unknown response keys and case sensitivity.
4. Keep one outstanding query per endpoint.
5. Keep network calls asynchronous, cancellable, and timed out.
6. Verify routes from decoder-reported `STREAM.HOST`.
7. Never auto-route at startup.
8. Return independent results for every decoder in a multi-route.
9. Preserve the distinction between `AssignedEncoderIp` and decoder-reported `CurrentEncoderIp`.
10. Do not claim hardware validation based on simulator results.
11. Do not expose destructive or configuration-changing commands in V1.
12. Preserve unrelated user changes and keep the handoff current.

## Recommended Next Work

The highest-value next step is to finish **real-hardware validation**, starting with visual
confirmation of the successful API routes recorded below. Then test source changes, partial
failure, external Vision Lite changes, and application restart behavior.

The next product milestone should be presets/scenes and decoder groups, since the core routing
view is working well and those features reuse the verified route engine. After that, add
Windows Service packaging, backup/export, and authentication/HTTPS before enabling LAN-wide
tablet access. Leave multicast discovery behind a clearly experimental action until it is
validated on the target network and bound to an explicitly selected NIC.

## Known Planning-vs-Code Differences

| Topic | Early plan | Implemented state |
|---|---|---|
| Framework | .NET 8 in original request; plan later mentioned .NET 10 | All projects use .NET 10 |
| Frontend | React + TypeScript | Plain HTML/CSS/JavaScript |
| Realtime updates | SignalR | Server-Sent Events |
| Persistence | SQLite/EF Core suggested | Direct `Microsoft.Data.Sqlite` store |
| Discovery | Planned multicast/subnet phases | Explicit `/24` unicast scan built; multicast not built |
| Windows Service | Planned | Not built |

These are not automatically defects. Evaluate them against deployment needs before replacing working code.

## Source Documents

- `README.md` — operator/developer overview and simulator workflow
- `docs/VISIONARY_API_NOTES.md` — compact protocol assumptions, confidence, and public sources
- `docs/VISIONARY_AV_ROUTER_PLAN.md` — broad original architecture and future roadmap; partially aspirational

## Handoff Maintenance

At the end of each meaningful work session:

1. Update `Current State`, `Verified Build State`, `Recommended Next Work`, and the work log.
2. Include the exact last successful test/build command.
3. Record hardware models/firmware and raw protocol differences when tested.
4. Record blockers with exact error text and the last successful step.
5. Commit code and documentation together when appropriate.

## Work Log

### 2026-09-13 — repository audit

- Located the actual implementation repository after initially inspecting the research-only `Vis_con` folder.
- Confirmed five projects in `VisionaryRouter.slnx` and 42 tracked files before this update.
- Confirmed all projects target .NET 10.
- Release build passed with zero warnings and zero errors.
- All 50 tests passed.
- Confirmed simulator-only status and identified the unbuilt roadmap items above.
- Added this canonical handoff and linked it from the README.

### 2026-09-13 — first hardware route

- Probed E4200 encoder `192.168.0.16` and D4200 decoders `192.168.0.18` and `192.168.0.19`, all on firmware 2.3.152.
- Routed encoder `.16` to both decoders. Both acknowledged `queue_next_switch` and verified `STREAM.HOST=192.168.0.16`.
- Observed successful query status variant `API.STATUS=SUCCESS_QUERY_X`; the parser's `SUCCESS` prefix behavior is correct.
- Observed that `QUERY.ALL` returns a `UNIT.PASSWORD` value. Raw probe/debug output must be redacted before logs are shared or productionized.
- Visual display confirmation is still pending from the operator.

### 2026-09-13 — full application hardware test

- Started `VisionaryRouter.Server` from the Release build at `http://localhost:8088`; process remains running for operator testing.
- Added four real devices to the SQLite database: E4200 Encoder 1 (`192.168.0.16`), DuetE-2 Encoder 2 (`192.168.0.17`), D4200 Decoder 1 (`192.168.0.18`), and D4200 Decoder 2 (`192.168.0.19`). All run firmware 2.3.152, report multicast mode, and were online.
- Routed Encoder 1 to both decoders through `POST /api/routes`, exercising the actual server, registry, routing service, UDP transport, persistence, and API response path.
- Application result: 2 verified, 0 failed. Both decoders reported `STREAM.HOST=192.168.0.16`, and the API mapped the current source name to `Encoder 1`.
- The pre-existing eight simulator records remain in the database and appear offline while the simulator is stopped. They were intentionally preserved rather than deleted without owner direction.
- The WebUI was queued/opened at `http://localhost:8088` for hands-on operator testing.

### 2026-09-13 — fullscreen and responsive routing UI

- Added a header fullscreen toggle using the browser Fullscreen API, including live Enter/Exit labeling, Escape synchronization, accessibility state, and an unsupported-browser fallback.
- Added adaptive spacing and responsive routing tile grids. At tablet width, stacked Sources and Destinations panels use multiple tile columns; narrow phones use compact single-column tiles and a sticky Route bar.
- Visually tested the live UI at 768×1024 and the narrow default in-app viewport.
- Confirmed fullscreen entry and exit in the running application.
- Browser console check returned no warnings or errors.
- Release build passed with zero warnings/errors and all 50 tests passed.

### 2026-09-13 — encoder input selection and device scan

- Added verified encoder input switching for `HDMI1`, `HDMI2`, and `USB-C` using `STREAM.VIDEO=<input>&STREAM.CONNECT=TRUE` followed by a `QUERY.KEY=STREAM.VIDEO` readback.
- Encoder input state is populated during probe, cached in SQLite for display continuity, and reconciled during encoder polling.
- Added an explicit Devices-page IPv4 `/24` scan. It probes 254 host addresses with bounded concurrency, adds only confirmed Visionary responses, uses endpoint IDs as default names, classifies models, enriches metadata, refreshes existing matches, and avoids duplicate IP records.
- Live empty-database scan of `192.168.0.0/24` found and added E4200 `192.168.0.16`, DuetE-2 `192.168.0.17`, and D4200 devices `192.168.0.18` and `192.168.0.19`.
- A second scan found the same four devices, added zero duplicates, refreshed all four, and left the total at four.
- Both live encoders accepted and verified `STREAM.VIDEO=HDMI1`. The API also correctly rejected input selection against decoder records.
- Visually verified the encoder buttons and the responsive scan UI in the running browser; the browser showed scan progress and the final 0-added/4-refreshed result.
- Forced Release rebuild succeeded with zero warnings/errors. Test suite now passes 55 tests.

### 2026-09-13 — commit of pending work and credential redaction

- Found the previous session's work entirely uncommitted: 20 modified files, 1155 insertions, plus `HANDOFF.md`, `BasicAuthenticationMiddleware.cs`, `DiscoveryService.cs` and `RoutingConfigurationStore.cs` untracked. Committed it unmodified as `d81201b` before making any changes, so the hardware-validated state is recorded separately from the follow-up.
- Fixed the credential disclosure recorded earlier the same day. `UNIT.PASSWORD` from `QUERY.ALL` was reaching the probe console and its parsed key dump, the `raw` field of `POST /api/devices/{id}/probe`, the browser activity log line `<device> responded: <raw>`, and `UDP RX` debug logging. It had been documented but not remediated.
- Redaction is applied in `VisionaryResponse.Parse`, so the value does not survive into the application and no later call site can leak it by omission. `VisionaryRedaction` matches `PASSWORD`, `PASSWD`, `PASSPHRASE`, `SECRET`, `CREDENTIAL` and `TOKEN` against key names only. A bare `KEY` is deliberately not matched: `QUERY.KEY` is a command parameter and blanking it would break route verification.
- The simulator's `QUERY.ALL` now returns a `UNIT.PASSWORD` value, matching observed 2.3.152 behavior, so redaction can be regression-tested without hardware.
- Verified end to end against the simulator: the endpoint transmitted `UNIT.PASSWORD=sim-password-should-never-be-displayed` and `probe info 127.0.0.10` printed `***REDACTED***` in both the raw line and the parsed dump.
- Corrected this handoff, which had drifted: groups, presets and authentication were listed as not implemented despite existing, and the recorded test count was stale.
- `dotnet build VisionaryRouter.slnx --configuration Release` — 0 warnings, 0 errors.
- `dotnet test VisionaryRouter.slnx --configuration Release` — 76 passed, 0 failed.
- Still outstanding and unchanged: nobody has confirmed a display actually changes picture when routed.

### 2026-09-14 — device-name editor focus fix

- Fixed the Devices page rebuilding its tables during a live SSE update while an operator was typing a friendly name. Replacing the focused input caused partial text and moved the cursor away from the field.
- Device-table rendering now pauses only while a `.name-input` has focus. The completed name still saves through the existing change handler, and the next live update redraws normally.
- Browser-tested by keeping an unsaved temporary name focused while forcing a real encoder probe/SSE update. The same input remained connected and focused with the complete value; the page was then reloaded so the test value was never saved.
- `node --check src/VisionaryRouter.Server/wwwroot/app.js` passed.
- `dotnet build VisionaryRouter.slnx --configuration Debug` — 0 warnings, 0 errors.
- `dotnet test VisionaryRouter.slnx --configuration Debug --no-build` — 80 passed, 0 failed.

### 2026-09-14 — simplified Operators tab

- Added an Operators tab that exposes only online/enabled source cards, online/enabled destination cards, and a Route button. It intentionally omits physical input switching, clear-route actions, filters, groups, presets, device management, and diagnostics.
- The Operators and full Routing tabs share the same selected source/destinations and live device state; there is no duplicate routing model.
- Verified that selecting a source and destination enables the Operator Route button without issuing a route during the test.
- Tightened the four-tab mobile header layout. At a 322px viewport, document width now equals viewport width with no horizontal overflow.
- Browser console check returned no warnings or errors.
- `node --check src/VisionaryRouter.Server/wwwroot/app.js` passed.
- `dotnet build VisionaryRouter.slnx --configuration Debug` — 0 warnings, 0 errors.
- `dotnet test VisionaryRouter.slnx --configuration Debug --no-build` — 80 passed, 0 failed.

### 2026-09-14 — interface-specific Web UI access

- Added **Diagnostics → Network Access** with an explicit checkbox for each active IPv4
  interface. Localhost is always enabled; no external interface is selected by default.
- Interface choices are persisted in SQLite by stable Windows adapter ID and resolved to
  current IPv4 addresses at startup, so Wi-Fi and Ethernet can be exposed independently.
- Enabling any external interface requires an administrator password. The password is stored
  only as a salted PBKDF2-SHA256 hash and protects the whole UI/API after restart using
  username `admin`.
- Current machine choices shown by the UI include Wi-Fi `192.168.1.29` and Ethernet
  `192.168.0.67`. To serve the operator's phone, select Wi-Fi, set the password, save, restart,
  and ensure Windows Firewall permits inbound TCP 8088 on the Wi-Fi network.
- The application deliberately does not create Windows Firewall rules. Basic authentication
  is still transported over plain HTTP and is intended only for a trusted LAN.
- Browser-verified the panel in the live app at the narrow phone/tablet layout. All adapters
  rendered unchecked by default and the browser console contained no warnings or errors.
- `node --check src/VisionaryRouter.Server/wwwroot/app.js` passed.
- Release Server build passed with 0 warnings and 0 errors.
- Test suite passes 84 tests, including password hashing/verification and malformed-hash
  rejection.

### 2026-09-14 — no-install Windows package

- Added a `win-x64`, self-contained, compressed single-file publish profile. The distributable
  executable is named `VisionaryRouter.exe`; no .NET runtime, SDK, Node, SQLite service, or
  installer is required on the target PC.
- Added `scripts/publish-windows.ps1`, which validates all target paths, runs Release tests,
  publishes the application, and creates `artifacts/VisionaryRouter-win-x64.zip`.
- Included `README-FIRST.txt` in the package with double-click startup, shutdown, LAN access,
  firewall, authentication, and persistent-data instructions.
- Final archive size is approximately 44.1 MB. Archive contents were inspected and confirmed
  to include the executable, Web UI assets, configuration, and quick-start file.
- Smoke-tested the packaged executable directly in Production mode: `/` returned HTTP 200,
  the expected page title was present, the API loaded all four devices, and all four polled
  online. The running process name is `VisionaryRouter`.
- Release tests passed: 84 passed, 0 failed, 0 skipped.
- Diagnosed the first desktop-launch failure as a port collision with the development package
  that this session had left running on port 8088; the desktop archive itself was complete.
- Added a per-Windows-session named mutex. A second launch now opens the existing localhost UI
  (when browser auto-open is enabled) and exits with code 0 instead of colliding on the port.
- Rebuilt both the repository artifact and the copies on the owner's Desktop. Verified the
  Desktop executable is the process serving port 8088, all four devices are online, and a
  second launch exits successfully while the primary instance remains reachable.

### 2026-09-14 — configurable Web UI port

- Added a validated Web UI port field to **Diagnostics → Network Access**. Ports 1024–65535
  are accepted, stored in SQLite, and applied with interface changes after restart.
- Startup checks active TCP listeners. If the requested port is occupied by another
  application, Visionary Router selects and saves the next available port rather than
  terminating; the console and diagnostics API report the active port.
- The active port is stored separately so a second launch opens the primary instance even
  when a new port selection is waiting for restart.
- Network URLs returned after saving use the requested port. The UI reminds operators that
  Windows Firewall rules must match it.
- Added deterministic port validation, selection, collision, and wraparound tests. The suite
  now passes 97 tests with 0 failures and 0 skipped.
- Rebuilt and replaced both the extracted Desktop package and Desktop ZIP, then ran the
  updated portable executable against all four online devices.

### 2026-09-14 — Ubuntu Server packages

- Added self-contained `linux-x64` and `linux-arm64` publish targets for Intel/AMD NUCs and
  64-bit Raspberry Pi systems. Neither package requires an installed .NET runtime.
- Added an architecture-checking interactive installer that creates a dedicated unprivileged
  `visionary-router` service account, installs under `/opt/visionary-router`, keeps durable
  state under `/var/lib/visionary-router`, and protects configuration under
  `/etc/visionary-router`.
- Added a hardened systemd unit with automatic restart, filesystem protection, a private temp
  directory, restricted address families, and a writable data-directory exception.
- First installation asks for a Web UI port and password, reads the password through a
  root-owned file rather than a process environment variable, bootstraps authenticated LAN
  access, and offers to add a UFW rule when UFW is active. Interface selection can then be
  narrowed through Diagnostics.
- Added an uninstaller that preserves the database by default and removes it only with an
  explicit `--purge` option. Re-running the installer upgrades the app while preserving data
  and the existing password.
- Ubuntu shell syntax checks passed and systemd-analyze accepted the unit syntax. WSL's
  mounted source permissions produced expected warnings that the installer normalizes during
  installation.
- The first Linux smoke test exposed an ICU dependency. Enabled invariant globalization in
  the Ubuntu profile, rebuilt, and then ran the x64 ELF successfully in Ubuntu without ICU.
  HTTP `/api/system/status` returned 200 on port 18088 using `/tmp` data.
- Verified first-run server mode separately: it bound `0.0.0.0:18089`, rejected an
  unauthenticated request with HTTP 401, and accepted username `admin` plus the configured
  password with HTTP 200.
- Both archives include the app, Web UI assets, installer, uninstaller, service unit,
  architecture marker, and Ubuntu quick-start guide. Release tests remain 97/97 passing.

### 2026-09-14 — clear-then-reroute produced no video

- Operator report: routing an encoder to both decoders worked, then Clear followed by routing the same encoder produced no picture. Routing a different encoder worked, and routing back to the first worked. Clear followed by the same source failed again.
- Reproduced and traced on hardware. Re-routing a decoder to the encoder it already holds answers `&STREAM.HOST=NO_CHANGE&STREAM.CONNECT=ALREADY_CONNECTED&API.STATUS=SUCCESS` and does nothing, including after `STREAM.CONNECT=FALSE`. The decoder keeps the correct `STREAM.HOST` and passes no video.
- The old verification could not see this. `STREAM.HOST` is a setting rather than a connection and read back correctly throughout, so the route was reported Verified against a black display.
- Confirmed with `QUERY.VIDEO_TIMING`: encoder `192.168.0.16` reported a live 3840x2160 30Hz table while decoder `192.168.0.18` reported `Not Available`. That query is the only reliable proof a route took effect and is now part of verification.
- `STREAM.CONNECT` is not usable as a state check: `QUERY.KEY=STREAM.CONNECT` returns `NOT_DEFINED`, and a bare `STREAM.CONNECT=TRUE` returns `NO_VALID_API_COMMAND`.
- Recovery requires `STREAM.HOST` to actually change. `RoutingService` now detects the no-op reply and moves the decoder to a parking address and back, confirming each step by reading the address back. Fixed sleeps are not sufficient - switches are queued, and a second command issued too early is coalesced with the first. During investigation that left a decoder parked on the intermediate address with nothing on screen until it was restored.
- The parking address is not another encoder, so a re-establish does not flash an unrelated source onto the display.
- HDCP was eliminated as a cause: the encoder reports `HDCP: [Off]`.
- The simulator reproduces the wedge. Without it the suite would keep passing a case that fails on hardware.
- **Not verified on hardware.** The bench Ethernet link went down mid-session - all four endpoints became unreachable over raw UDP, the adapter left the Up list, and Windows began routing `192.168.0.0/24` out of Wi-Fi on a different subnet. The fix passes against the simulator only and still needs the original failing sequence re-run on the real decoders.
- That outage is also the argument for the outstanding control-NIC binding item: with Ethernet, Wi-Fi, Tailscale, IPVanish, WSL and two Hyper-V adapters present, the application depends on the OS choosing the right path, and the OS chose wrong.
- `dotnet build VisionaryRouter.slnx --configuration Release` — 0 warnings, 0 errors.
- `dotnet test VisionaryRouter.slnx --configuration Release` — 97 passed, 0 failed.
