# Visionary Router

A Windows application that serves a local web UI for routing Visionary PacketAV AV-over-IP
endpoints. Register your encoders and decoders, pick a source, pick one or many destinations,
route.

Not a replacement for Vision Lite. It does one job — routing — and deliberately exposes no
configuration commands.

```
  SOURCES                    DESTINATIONS
  ● Lectern PC          ☑ Training Room North   Showing Lectern PC
  ● Teams PC            ☑ Training Room South   Showing Lectern PC
  ● Lobby Signage       ☐ Video Wall            Showing Lobby Signage
                        ☐ Lobby Display         No response

                                            [ Route to 2 ]
```

## Status

Built and working against a simulator. Basic query, route, and `STREAM.HOST` verification
were successfully exercised on E4200/D4200 hardware running firmware 2.3.152 on 2026-09-13.
This is an initial protocol check, not full production validation — see
[Before trusting this on real equipment](#before-trusting-this-on-real-equipment).

For the exact implementation state, verification results, remaining work, and continuation
instructions, see [HANDOFF.md](HANDOFF.md).

## Running it

Three things can run. The simulator and the server are the normal pair during development.

```bash
dotnet run --project src/VisionaryRouter.Simulator
```

```bash
dotnet run --project src/VisionaryRouter.Server
```

The server opens `http://localhost:8088` in your browser on start. The simulator provides
eight fake endpoints on `127.0.0.10`–`127.0.0.24`, which you can add through the Devices tab.
Two of them misbehave deliberately: `127.0.0.23` never answers, and `127.0.0.24` accepts route
commands and then ignores them.

Use the **Fullscreen** button in the header for a dedicated control surface. The routing
tiles automatically reflow and resize for desktop, landscape/portrait tablets, and phones.
The **Operators** tab is the minimal day-to-day surface: source cards, destination cards,
and a Route button only. Advanced controls remain on the full Routing and Devices tabs.

### Running without installing anything

Use the self-contained Windows x64 ZIP in `artifacts/VisionaryRouter-win-x64.zip`. Extract
the complete archive and double-click `VisionaryRouter.exe`; the browser opens automatically.
The target PC does not need the .NET runtime, SDK, Node, SQLite, or an installer. Keep the
console window open while the router is running and close it to stop the application.
Launching `VisionaryRouter.exe` again while it is already running reopens the existing Web UI
and exits the duplicate cleanly instead of failing because port 8088 is already in use.

Create a fresh tested package from the repository root with:

```powershell
./scripts/publish-windows.ps1
```

The script runs the test suite before publishing and will not create the ZIP if tests fail.
The archive includes `README-FIRST.txt` for the person deploying it. Because the executable
is not currently code-signed, Windows may show an unknown-publisher warning on a different
computer.

### Ubuntu Server installation

Self-contained Ubuntu packages are produced for both 64-bit Raspberry Pi and Intel/AMD NUC
hardware:

```text
artifacts/VisionaryRouter-ubuntu-linux-arm64.tar.gz  Raspberry Pi / ARM64
artifacts/VisionaryRouter-ubuntu-linux-x64.tar.gz    Intel or AMD / x64
```

Copy the matching archive to the Ubuntu server, then run:

```bash
tar -xzf VisionaryRouter-ubuntu-linux-x64.tar.gz
cd VisionaryRouter-ubuntu-linux-x64
sudo bash install.sh
```

Replace `linux-x64` with `linux-arm64` for a Pi. The installer verifies the CPU architecture,
asks for the Web UI port and administrator password, installs a hardened `systemd` service,
and offers to open the port when UFW is active. No .NET runtime is required. Application data
is preserved under `/var/lib/visionary-router` across upgrades and normal uninstalls.

```bash
sudo systemctl status visionary-router
sudo journalctl -u visionary-router -f
sudo systemctl restart visionary-router
sudo visionary-router-uninstall
```

Create both tested archives on Windows with `./scripts/publish-ubuntu.ps1`.

To change the Web UI port or reach the UI from a tablet or another PC, open
**Diagnostics → Network Access**. Choose a port from 1024–65535, tick only the interface(s)
that should serve the UI, set an administrator password when enabling LAN access, save, and
restart Visionary Router. Localhost always remains enabled. For this machine, selecting
Wi-Fi exposes `http://192.168.1.29:8088`; sign in with username `admin` and the password you
set. Windows Firewall must also allow inbound TCP 8088 on that network. The UI intentionally
does not create or broaden firewall rules on its own.

Interface selections are stored by the stable Windows adapter ID, then resolved to the
adapter's current IPv4 address at startup. This allows normal DHCP address changes without
silently exposing every network. Network access uses Basic authentication over plain HTTP,
so enable it only on trusted LANs. The legacy `Router:ListenOnAllInterfaces=true` and
`Router:AdminPassword` configuration remains available for managed deployments.

If the saved port is already occupied by another application at startup, Visionary Router
automatically selects and saves the next available port instead of terminating. The console
and Diagnostics page report the port actually in use. A firewall rule scoped to an old port
must be updated separately.

## How routing works

Visionary routing is simpler than most matrix systems, and the shape of it drives the whole
design:

> **The encoder is never told where to send video. Each decoder is told which encoder to follow.**

So a route is one command sent to the *decoder*:

```
CMD=START&UNIT.ID=ALL&STREAM.HOST=<encoder-ip>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

One-to-many is the same command sent to each decoder independently. There is no separate
code path for it, and no group state on the endpoints — a group of four is four commands.
That is also why a group route reports per-decoder results rather than one overall verdict:
one powered-down display must not make three working ones look like a failure.

Each encoder card also exposes **HDMI 1**, **HDMI 2**, and **USB-C** input buttons. The
application sends the input command to the encoder, reads `STREAM.VIDEO` back, and only marks
the button active after the encoder reports the requested input.

## What it will not do

Routing state is intentionally volatile. Every switch is sent *without* `SAVE`, and the
command builder throws if anything tries to add it — there is no reason to write routing to
nonvolatile storage on every button press.

The application sends no configuration commands at all: no factory reset, firmware update,
IP change, password change, EDID, VLAN, reboot, or Mass Configuration. Those belong in
Vision Lite or the endpoint's own web interface.

Restarting the application never changes a route. On startup it loads the saved device list,
asks every decoder what it is currently following, and displays that. It does not replay
anything.

## Reading the colours

Every encoder owns a colour. Every decoder following it is marked in the same one — the bar
down the edge of its card, its tick box, and its swatch in the device table. So "which
displays are on the same feed" is a glance, not a read.

Colour answers exactly one question: **which encoder is routed to which decoder.**

A source carries its colour as a bar down the edge of its card. A destination carries it in
its tick box, and nowhere else — grey until something is routed there, then the colour of the
encoder it was sent. One coloured mark per row, so there is nothing to mistake for a second
fact.

Selecting a source does not recolour anything. Selecting changes nothing on the wall, so it
changes nothing on screen.

The colour comes from what the decoder reports when it reports anything, falling back to what
this application last sent it. That fallback is what keeps an unplugged decoder, or one that
accepted a route and never switched, still showing where it was assigned. Whether the switch
actually took is a separate question, answered by the line under the device name — `Showing
Teams PC` against `No source reported` — and by the route results panel.

Colour slots are assigned in the order encoders were added, so adding or renaming a source
never recolours the ones already on screen. Online/offline stays green/red, and source
colours are always bars or swatches — never status dots — so the two are never told apart by
hue alone.

**A striped bar means the display is not showing anything.** Grey stripes: the decoder is
answering but reports no source. Red stripes: it is not answering at all. Stripes rather than
flat grey because flat grey reads as a missing colour, and this is a fault worth chasing.

That distinction is the point of the whole scheme. Route a source to a decoder that accepts
the command and never switches, and its bar stays striped — the application will not colour
it by the source you asked for, because that would claim a route that did not happen. If a
destination is not wearing the colour you routed to it, believe the display, not the button
you pressed.

Selections clear once a route finishes, so the next route can never quietly include displays
from the last one.

## Verified, not assumed

The UI never shows a route as successful because a button was pressed. After sending a
switch, the decoder is asked what it is actually following, and only a match counts as
verified. This matters for the failure that is otherwise invisible: an endpoint that accepts
the command, answers `SUCCESS`, and never changes source. An optimistic UI shows green while
the display still shows the old picture.

The same polling loop means routes changed by *anything else* — Vision Lite, a Crestron or
Q-SYS system, another copy of this application — appear in the UI within a few seconds
without anyone refreshing.

## Layout

```
src/
  VisionaryRouter.Core/        Protocol, device client, routing engine. No UI, no HTTP.
    Protocol/                  Command builder, response parser, UDP transport
    Devices/                   Device-level operations
    Routing/                   Route one or many decoders, verify the result
  VisionaryRouter.Server/      ASP.NET Core host, HTTP API, SQLite, web UI
    wwwroot/                   The UI: three files, no build step
  VisionaryRouter.Simulator/   Fake endpoints, including ones that misbehave
  VisionaryRouter.Probe/       Standalone CLI for the first real-hardware test
tests/
  VisionaryRouter.Tests/       Protocol and routing behaviour
```

Two rules keep the layers honest: no raw protocol strings outside `VisionaryCommand`, and no
socket code outside `VisionaryUdpTransport`.

The frontend is plain HTML, CSS, and JavaScript served as static files. No Node, no npm, no
bundler — for a page this size, being able to open the file that actually runs is worth more
than a framework. Live updates arrive over Server-Sent Events, which needs no client library.

## Before trusting this on real equipment

The simulator was written from the same documentation as the application, so it agrees with
every assumption the application makes. That makes it good for exercising failure paths and
useless for validating the protocol. Only hardware can do that.

Use the probe tool first, on one decoder and one encoder:

```bash
dotnet run --project src/VisionaryRouter.Probe -- info 10.10.1.31
```

```bash
dotnet run --project src/VisionaryRouter.Probe -- route 10.10.1.31 10.10.1.21
```

The probe prints every byte sent and received, so a real endpoint that behaves differently
from the documentation shows the difference plainly.

**The question that matters:** does the display actually change? `STREAM.HOST` verifying is
necessary but not sufficient — the control path can succeed while the video path fails. If
the decoder reports the right encoder and the screen is still blank, the problem has moved
to multicast, IGMP, switch configuration, HDCP, or EDID, and this application is not the
thing to debug.

Then work through:

| Test | Expect |
|---|---|
| Probe an encoder | Online, model, firmware returned |
| Probe a decoder | Online, current `STREAM.HOST` returned |
| Route one encoder to one decoder | Picture changes, UI shows Verified |
| Change the source | Picture changes again |
| Route one encoder to three decoders | All three switch and verify |
| Route a group containing a powered-off decoder | Others switch; the dead one reports failure; nothing hangs |
| Change a route in Vision Lite | Our UI updates on its own within a few seconds |
| Restart the application | Device list survives, current routes rediscovered, nothing re-routed |

## Things worth knowing

**Stream mode.** One unicast encoder cannot feed several decoders. If you route a
`STREAM.MODE=unicast` encoder to more than one destination, the app warns and proceeds — it
does not change stream configuration, and you may know something it does not.

**Unclassified devices.** Encoder/decoder type is read from the model string (`E*` encoder,
`D*` decoder, `DuetE`/`DuetD` for Duet parts). A device that does not answer has no model, so
it cannot be classified — set the type by hand. A bare `Duet` with no role letter is left
alone deliberately, since that hardware can be either.

**Network interface.** A Windows PC in an AV rack typically has Ethernet, Wi-Fi, Hyper-V,
WSL, and VPN adapters. Do not assume the AV control network is the first one — use
**Diagnostics → Network Access** to select the intended adapter explicitly. Changes apply
after an application restart. The rest of Diagnostics continues to list all active adapter
addresses for troubleshooting.

## Not built yet

Multicast discovery (`226.0.0.19`), the matrix view, and Windows Service packaging. Ubuntu
`systemd` service installation is implemented. Decoder
groups and routing presets are implemented. The Devices page has an explicit IPv4 `/24`
scan that finds and adds Visionary endpoints without duplicating devices already in the
database.

## Reference

Protocol details and sources: [docs/VISIONARY_API_NOTES.md](docs/VISIONARY_API_NOTES.md).
