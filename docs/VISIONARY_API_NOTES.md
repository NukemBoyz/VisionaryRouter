# Visionary PacketAV API notes

What this application relies on, and how confident we are in each part.

## Wire format

Human-readable key/value pairs separated by `&`, wrapped in a `CMD=START` / `CMD=END`
envelope. **Keys are case-sensitive.**

```
CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END
```

Responses come back as ampersand-separated pairs, usually with a leading `&`:

```
&STREAM.HOST=192.168.8.101&API.STATUS=SUCCESS_QUERY
```

The API is reachable over HTTP GET, HTTP POST, UDP unicast, and UDP multicast. This
application uses **UDP unicast on port 8000** — no HTTP authentication to deal with, and it
suits short control exchanges.

The documented API multicast address is `226.0.0.19`.

## Commands used

| Purpose | Command |
|---|---|
| Liveness / status | `CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END` |
| Full config dump | `CMD=START&UNIT.ID=ALL&QUERY.ALL=TRUE&CMD=END` |
| Read current source | `CMD=START&UNIT.ID=ALL&QUERY.KEY=STREAM.HOST&CMD=END` |
| Route a decoder | `CMD=START&UNIT.ID=ALL&STREAM.HOST=<encoder-ip>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END` |
| Identify (flash LEDs ~10s) | `CMD=START&UNIT.ID=ALL&UNIT.FU=TRUE&CMD=END` |

A successful switch answers something like:

```
&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS
```

`queue_next_switch` is the important detail: the endpoint has *accepted* the command, not
completed it. Verification has to wait for the switch to settle, which is why the routing
service delays before reading `STREAM.HOST` back and retries a few times.

## The routing model

A decoder is routed by telling it which encoder IP to follow. The encoder is never told
anything. One-to-many is the same command sent to each decoder separately; there is no group
concept on the endpoints.

## Keys we read

`UNIT.ID`, `UNIT.MODEL`, `UNIT.SERIAL`, `UNIT.FIRMWARE`, `UNIT.FIRMWARE_DATE`,
`UNIT.MAC_ADDRESS`, `IP.ADDRESS`, `STREAM.MODE`, `STREAM.HOST`, `API.STATUS`.

Not every field applies to every model, and firmware updates add fields. The parser keeps
every key it receives rather than only the ones listed here, and never fails on an unknown
one.

`API.STATUS` has several success variants (`SUCCESS`, `SUCCESS_QUERY`, …), so success is
matched on the `SUCCESS` prefix rather than an exact value.

## Volatility

API changes are volatile unless explicitly saved. For routing that is exactly what we want,
so route commands never include `SAVE` or `UNIT.SAVE`. `VisionaryCommand.Build` throws if
anything tries to add them.

## Model naming

```
E4100  E4200  E5100  E5200  DuetE-*    encoders
D4100  D4200  D5100  D5200  DuetD-*    decoders
```

Classification is rule-based (`DuetE`/`DuetD` first, then first letter `E`/`D`) rather than a
model lookup table, so hardware released after this was written still classifies. A bare
`Duet` with no role letter returns Unknown on purpose — that hardware can be either.

## Confidence

**Documented and relied upon:** the key/value format, UDP port 8000, the `STREAM.HOST`
routing model, `STREAM.CONNECT`, `QUERY.KEY`, group routing by commanding each decoder,
status and query behaviour, and the identify command. The 5-series uses the same API and
port as the 4-series.

**Not yet confirmed on hardware.** Everything above comes from documentation. The simulator
in this repository was written from the same documentation, so it cannot independently
validate any of it — it agrees with our assumptions by construction. The first real test is
the probe tool against one encoder and one decoder.

**Worth obtaining:** Visionary makes the full API available to qualified system integrators
through their support. The public 4-series specification is enough to build routing, but get
the current package before extending into 5-series configuration territory.

## Network requirements (not our problem, but adjacent)

Visionary recommends IGMP snooping on all switches, an IGMP querier on at least one, jumbo
frames, managed switches, and separate VLANs for AV-over-IP, audio, and control. Published
bandwidth is roughly 200–300 Mbps for 1080p60 and 250–750 Mbps for 2160p30.

This application carries no video — only small control datagrams. So a working control path
and a broken video path are different failures, and the UI is built to make that distinction
visible: a decoder can report the correct `STREAM.HOST` while the display shows nothing, and
that points at multicast, IGMP, switch configuration, HDCP, or EDID — not at the router.

## Sources

- Visionary AV-over-IP products — https://visionary-av.com/products/av-over-ip/
- Visionary FAQ / network recommendations — https://visionary-av.com/faq/
- E5200 / D5200 user manual — https://visionary-av.com/wp-content/uploads/2024/10/VISIONARY-ED5200-UserManual-241023.pdf
- Public 4-series / Duet control protocol specification — https://audiobrains.com/data/visionarysolutions/others/duet_4X00_controiprotcol_2_x.pdf
- Bitfocus Companion module request with 5-series API reference — https://github.com/bitfocus/companion-module-requests/issues/1587
