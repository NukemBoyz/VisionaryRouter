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

Built and working against a simulator. **Not yet validated against real hardware** — see
[Before trusting this on real equipment](#before-trusting-this-on-real-equipment).

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

To reach the UI from a tablet or another PC, set `Router:ListenOnAllInterfaces` to `true`.
It is off by default — serving a control surface to the whole network should be a decision
someone made, not a side effect of launching the app.

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

Colour says one thing only: **the source that is on that display right now.** Ticking a
destination means "include this one" — it does not preview what the display is about to
become. What you are about to route is on the highlighted source card and in the summary line
above the button.

Colour slots are assigned in the order encoders were added, so adding or renaming a source
never recolours the ones already on screen. Online/offline stays green/red, and source
colours are always bars or swatches — never status dots — so the two are never told apart by
hue alone. A decoder following nothing, or following something outside the device list, shows
neutral grey.

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
WSL, and VPN adapters. Do not assume the AV control network is the first one — the
Diagnostics tab lists them all with their addresses.

## Not built yet

Subnet scan and multicast discovery (`226.0.0.19`), decoder groups, presets, the matrix view,
and Windows Service packaging. Manual device entry works first on purpose: it is the part
that behaves the same on every customer network.

## Reference

Protocol details and sources: [docs/VISIONARY_API_NOTES.md](docs/VISIONARY_API_NOTES.md).
