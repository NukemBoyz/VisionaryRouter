# Visionary AV-over-IP Windows Router
## Research, Architecture, and Codex Build Plan

**Project working name:** Visionary Router  
**Target platform:** Windows  
**UI:** Browser-based WebUI served by the Windows application  
**Primary function:** Add/manage Visionary encoders and decoders, then route any encoder to one or many decoders.

---

# 1. Project Goal

Build a lightweight Windows-based control application for Visionary AV-over-IP endpoints.

The application is **not** intended to replace every function in Vision Lite. Its first job is deliberately narrow:

1. Add Visionary encoders.
2. Add Visionary decoders.
3. Verify that endpoints are online.
4. Display the currently selected encoder for each decoder.
5. Route one encoder to one decoder.
6. Route one encoder to many decoders.
7. Allow friendly names so the user sees names such as:
   - `Lectern PC`
   - `Teams PC`
   - `Lobby Signage`
   - `Training Room North`
   - `Video Wall`
8. Persist the device list and UI configuration across application restarts.
9. Run reliably as a native Windows application/service.

The initial release should be simple, easy to troubleshoot, and safe to run on an existing Visionary deployment.

---

# 2. What We Confirmed About Visionary Control

Visionary PacketAV devices expose a human-readable key/value API.

Public Visionary API documentation for the 4-series states that the API is accessible using:

- HTTP GET
- HTTP POST
- UDP unicast
- UDP multicast

The API is case-sensitive.

Commands begin with:

```text
CMD=START
```

and end with:

```text
CMD=END
```

Key/value pairs are separated by `&`.

Example:

```text
CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END
```

## UDP Control

Visionary devices listen for API commands on:

```text
UDP 8000
```

The documented Visionary API multicast address is:

```text
226.0.0.19
```

Public 5-series information also confirms that the 5-series encoder/decoder family uses the common API and UDP port 8000.

For this project, **UDP unicast should be the primary control method** because it is simple, fast, does not require HTTP authentication, and is directly suited to routing commands.

---

# 3. The Important Routing Discovery

Visionary routing is refreshingly simple.

A decoder is routed by telling that decoder which **encoder IP address** it should follow.

## Route Decoder to Encoder

Send this command to the **decoder's IP address on UDP port 8000**:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=<ENCODER_IP>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

Example:

Encoder:

```text
192.168.8.101
```

Decoder:

```text
192.168.8.111
```

Command sent to `192.168.8.111:8000`:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=192.168.8.101&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

Visionary documentation shows a successful switch response similar to:

```text
&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS
```

The important concept is:

> The encoder is not told where to go. Each decoder is told which encoder to receive.

That makes our software architecture very simple.

---

# 4. One-to-One Routing

For a 1-to-1 route:

```text
Encoder A -> Decoder 1
```

the application sends one command to Decoder 1:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=<ENCODER_A_IP>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

---

# 5. One-to-Many Routing

For:

```text
Encoder A
   |
   +--> Decoder 1
   +--> Decoder 2
   +--> Decoder 3
   +--> Decoder 4
```

the application sends the same routing command independently to each decoder.

Example:

```text
Decoder 1 -> STREAM.HOST=192.168.8.101
Decoder 2 -> STREAM.HOST=192.168.8.101
Decoder 3 -> STREAM.HOST=192.168.8.101
Decoder 4 -> STREAM.HOST=192.168.8.101
```

Visionary's own API documentation explicitly shows group routing this way: each decoder receives its own command.

The application therefore does **not** need to construct or manage the actual multicast video stream itself. Visionary endpoints handle that portion.

---

# 6. Current Route Query

To determine which encoder a decoder is currently receiving, query the decoder for:

```text
STREAM.HOST
```

Command:

```text
CMD=START&UNIT.ID=ALL&QUERY.KEY=STREAM.HOST&CMD=END
```

Example response:

```text
&STREAM.HOST=192.168.8.101&API.STATUS=SUCCESS_QUERY
```

This is extremely useful.

It means the WebUI does not need to assume that its last routing command is still correct. It can periodically ask each decoder what it is actually tuned to.

This also means that if somebody changes a route using:

- Vision Lite
- Q-SYS
- Crestron
- another control system
- another instance of our software

our UI can detect the change and update itself.

---

# 7. Device Status / Probe Commands

A basic endpoint status query is:

```text
CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END
```

A larger configuration/status response can be requested with:

```text
CMD=START&UNIT.ID=ALL&QUERY.ALL=TRUE&CMD=END
```

The returned data can expose information such as:

```text
UNIT.ID
UNIT.MODEL
UNIT.SERIAL
UNIT.FIRMWARE
UNIT.FIRMWARE_DATE
UNIT.MAC_ADDRESS
IP.ADDRESS
STREAM.MODE
STREAM.HOST
```

Not every returned field applies to every model.

The application should parse responses generically as `KEY=VALUE` pairs rather than hard-coding the entire response string.

---

# 8. Identify / Flash Endpoint

Visionary exposes a useful physical-identification command:

```text
CMD=START&UNIT.ID=ALL&UNIT.FU=TRUE&CMD=END
```

The documented behavior is to flash/chase the endpoint's front-panel LEDs for approximately ten seconds.

This should become an **Identify** button on the Devices page.

That will be extremely useful during installation.

---

# 9. Stream Mode

Visionary exposes:

```text
STREAM.MODE
```

with documented values:

```text
multicast
unicast
```

One-to-many systems will normally use multicast.

For V1:

- Read and display the stream mode when possible.
- Do **not** automatically change it.
- If a user attempts to route one encoder to multiple decoders and the encoder reports `unicast`, show a warning.
- Leave actual stream/network configuration to Vision Lite or the endpoint WebUI for now.

We are building a router first, not a network configuration utility.

---

# 10. Important API Behavior

Visionary documentation states that API changes are volatile unless explicitly saved.

That is actually desirable for routing.

## V1 MUST NOT append SAVE to route commands.

A normal route should remain:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=<IP>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

Do not add:

```text
SAVE=TRUE
```

or:

```text
UNIT.SAVE=TRUE
```

to routine switching commands.

There is no reason to continually write routing state to nonvolatile storage.

---

# 11. V1 Safety Boundary

The first release should intentionally support only safe operational controls.

## Allowed

- Query device
- Query current route
- Add/remove a device from our local database
- Rename a device inside our application
- Flash/identify endpoint
- Route a decoder
- Route multiple decoders
- Read device metadata
- Read online/offline status

## Explicitly NOT part of V1

Do not expose API controls for:

- Factory reset
- Firmware updates
- Changing endpoint IP settings
- Changing passwords
- Changing EDIDs
- Changing VLAN settings
- Rebooting devices
- Mass Configuration
- Automatically changing stream mode
- Dante configuration
- USB/KVM configuration
- RS-232 configuration

Visionary's Mass Configuration feature is powerful, but their documentation warns that it should be used cautiously and that video can be interrupted while it is in use.

There is no reason for our routing application to touch it.

---

# 12. Recommended Windows Software Architecture

Use native .NET rather than Python for this project.

## Recommended stack

### Backend

```text
C#
.NET 10 LTS
ASP.NET Core
```

Responsibilities:

- UDP Visionary communications
- Device state
- Routing engine
- Polling
- SQLite persistence
- REST API for the WebUI
- SignalR live WebUI updates
- Logging
- Windows Service support

### Frontend

Recommended:

```text
React
TypeScript
Vite
```

The UI is still served by the ASP.NET Core application.

The React frontend is appropriate because the routing screen will eventually benefit from dynamic:

- device cards
- selection states
- status updates
- matrices
- groups
- drag/drop or multi-select
- realtime updates

Avoid creating two separately deployed applications.

The final publish should package everything into the Windows application.

---

# 13. Windows Deployment Model

The production application should be capable of running as a Windows Service.

Example concept:

```text
VisionaryRouter.exe
        |
        +-- ASP.NET Core / Kestrel
        +-- Visionary UDP engine
        +-- SQLite database
        +-- WebUI
```

The WebUI could be reached locally at something such as:

```text
http://localhost:8088
```

Optionally, when explicitly enabled:

```text
http://<WINDOWS_PC_IP>:8088
```

This allows tablets or other PCs on the control network to use the router without installing client software.

## Production folders

Use something similar to:

```text
C:\ProgramData\VisionaryRouter\
```

Suggested contents:

```text
C:\ProgramData\VisionaryRouter\router.db
C:\ProgramData\VisionaryRouter\appsettings.json
C:\ProgramData\VisionaryRouter\logs\
```

During development, normal project-local files are fine.

---

# 14. Application Architecture

Suggested solution layout:

```text
VisionaryRouter/
|
+-- VisionaryRouter.sln
|
+-- src/
|   |
|   +-- VisionaryRouter.Server/
|   |   +-- Program.cs
|   |   +-- Controllers/
|   |   +-- Hubs/
|   |   +-- Services/
|   |   +-- Data/
|   |
|   +-- VisionaryRouter.Core/
|   |   +-- Models/
|   |   +-- Protocol/
|   |   +-- Routing/
|   |   +-- Interfaces/
|   |
|   +-- VisionaryRouter.Web/
|       +-- React / TypeScript UI
|
+-- tests/
|   +-- VisionaryRouter.Tests/
|
+-- docs/
    +-- VISIONARY_API_NOTES.md
```

---

# 15. Core Backend Components

## 15.1 VisionaryCommandBuilder

Do not scatter raw protocol strings throughout the application.

Create one component responsible for constructing commands.

Example methods:

```text
BuildStatusQuery()
BuildQueryAll()
BuildRouteCommand(encoderIp)
BuildCurrentRouteQuery()
BuildIdentifyCommand()
```

Expected route output:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=192.168.8.101&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

---

# 16. VisionaryResponseParser

Responses are mostly ampersand-separated key/value data.

Example:

```text
&STREAM.HOST=192.168.8.101&API.STATUS=SUCCESS_QUERY
```

Parse it into:

```text
STREAM.HOST -> 192.168.8.101
API.STATUS  -> SUCCESS_QUERY
```

The parser should:

1. Remove leading/trailing whitespace.
2. Tolerate a leading `&`.
3. Split fields on `&`.
4. Split each field only on the first `=`.
5. Preserve unknown keys.
6. Treat keys as case-sensitive.
7. Return a dictionary plus the raw response.
8. Never fail because a newer firmware added an unknown field.

---

# 17. UDP Transport

Create a single reusable service such as:

```text
VisionaryUdpTransport
```

Responsibilities:

- Send UDP datagrams to port 8000.
- Receive responses.
- Track sender IP.
- Apply timeouts.
- Prevent multiple simultaneous queries to the same device from becoming confused.
- Return both parsed and raw results.
- Write diagnostic logging.

Suggested behavior:

```text
SendAsync(IPAddress target, string command)
QueryAsync(IPAddress target, string command, timeout)
```

Because the Visionary protocol does not appear to provide a normal transaction ID, response correlation should primarily use the **source IP address**.

Use a per-device semaphore/lock so the polling engine does not have multiple outstanding queries against one endpoint simultaneously.

---

# 18. Routing Engine

Create a higher-level service:

```text
VisionaryRoutingService
```

## Route One Decoder

Pseudo-flow:

```text
Route(decoder, encoder):

1. Confirm decoder exists.
2. Confirm encoder exists.
3. Confirm both are enabled.
4. Send route command to decoder UDP 8000.
5. Wait briefly.
6. Query decoder STREAM.HOST.
7. Compare returned IP against encoder IP.
8. Update current route.
9. Publish realtime UI update.
10. Log result.
```

## Route Multiple Decoders

Pseudo-flow:

```text
RouteMany(decoders[], encoder):

1. Validate all IDs.
2. Build one route command.
3. Send command to each decoder.
4. Use bounded parallelism.
5. Verify STREAM.HOST on each decoder.
6. Return per-decoder result.
```

A group route must **not** be treated as one all-or-nothing operation.

Example result:

```text
Training Room North  SUCCESS
Training Room South  SUCCESS
Lobby Display        FAILED - timeout
Video Wall           SUCCESS
```

The UI needs to expose partial failures rather than simply saying "Route complete."

---

# 19. Polling / State Reconciliation

The router should maintain actual state rather than relying only on button presses.

## Decoder polling

Every few seconds:

```text
QUERY.KEY=STREAM.HOST
```

Use the response to update:

```text
CurrentEncoderIp
```

Map the IP to a known encoder name.

Example:

```text
Decoder: Training Room TV
CurrentEncoderIp: 10.20.1.51
CurrentSource: Teams PC
```

## Endpoint heartbeat

Periodically run:

```text
QUERY.STATUS=TRUE
```

or another lightweight query.

State:

```text
Online
Degraded
Offline
Unknown
```

Do not hammer the network.

Polling should be configurable.

A reasonable initial design is:

```text
Decoder route status: every 2-3 seconds
General endpoint heartbeat: every 5-10 seconds
```

Do not query a device again while another query to that same device is still outstanding.

---

# 20. Device Database Model

Suggested `Device` entity:

```text
Id                  GUID
Name                string
DeviceType          Encoder | Decoder
IpAddress           string
Enabled             bool

UnitId              string?
Model               string?
SerialNumber        string?
MacAddress          string?
Firmware            string?
FirmwareDate        string?

StreamMode          string?
CurrentEncoderIp    string?

OnlineState         enum
LastSeenUtc         datetime?
LastError           string?

CreatedUtc          datetime
UpdatedUtc          datetime
```

The user-defined `Name` belongs to our program.

Do not require renaming the actual Visionary endpoint.

---

# 21. Device Add Workflow

The Devices page should support manual addition from day one.

## Add Device dialog

Fields:

```text
Friendly Name
IP Address
Expected Type:
    Auto Detect
    Encoder
    Decoder
```

Buttons:

```text
Probe
Save
Cancel
```

## Probe flow

Send a status/all query.

Parse:

```text
UNIT.MODEL
UNIT.ID
UNIT.SERIAL
UNIT.FIRMWARE
IP.ADDRESS
STREAM.MODE
```

Determine encoder/decoder using model information.

Examples:

```text
E4100 -> Encoder
E4200 -> Encoder
E5100 -> Encoder
E5200 -> Encoder
DuetE-* -> Encoder

D4100 -> Decoder
D4200 -> Decoder
D5100 -> Decoder
D5200 -> Decoder
DuetD-* -> Decoder
```

Do not make classification depend only on a hardcoded model list.

Also fall back to:

```text
Model starts with E -> Encoder
Model starts with D -> Decoder
Model contains DuetE -> Encoder
Model contains DuetD -> Decoder
```

Unknown devices can be manually classified.

---

# 22. Devices Page

Suggested layout:

```text
DEVICES

[ + Add Device ] [ Discover ] [ Refresh All ]

ENCODERS
-------------------------------------------------------
Name            IP             Model      Status
Teams PC        10.10.1.21     E5100      Online
Lectern         10.10.1.22     E4200      Online
Signage         10.10.1.23     DuetE-5    Offline

DECODERS
-------------------------------------------------------
Name            IP             Model      Current Source
North TV        10.10.1.31     D5100      Teams PC
South TV        10.10.1.32     D5100      Lectern
Video Wall      10.10.1.33     D5200      Teams PC
```

Each device should have:

```text
Edit
Delete
Probe
Identify
Open WebUI
```

`Open WebUI` simply launches:

```text
http://<device-ip>
```

in the user's browser.

---

# 23. Routing Page

This is the main page.

The first version should favor reliability and speed over a fancy matrix.

## Recommended V1 layout

### LEFT: Sources / Encoders

```text
SOURCES

[ Teams PC      ]
[ Lectern       ]
[ AirMedia      ]
[ Signage       ]
```

Clicking a source selects it.

Selected source gets a strong visual highlight.

### RIGHT: Destinations / Decoders

```text
DESTINATIONS

[ ] North TV
    Current: Teams PC

[ ] South TV
    Current: Lectern

[ ] Video Wall
    Current: Teams PC

[ ] Lobby
    Current: Signage
```

Allow one or multiple decoder selections.

Bottom controls:

```text
[ Select All ]
[ Clear ]
[ ROUTE SELECTED ]
```

Example operation:

1. Click `Teams PC`.
2. Check `North TV`.
3. Check `South TV`.
4. Check `Video Wall`.
5. Press `ROUTE SELECTED`.

Backend sends three independent route commands.

This naturally handles both:

```text
1 -> 1
```

and:

```text
1 -> many
```

without creating two separate workflows.

---

# 24. Routing Page Feedback

Do not optimistically show a route as successful just because the button was clicked.

States:

```text
Pending
Switching
Verified
Failed
Offline
```

Example decoder card:

```text
North TV
Current: Teams PC
Status: VERIFIED
```

During a switch:

```text
North TV
Switching -> Lectern
```

Failure:

```text
North TV
Route failed
No response from 10.10.1.31
```

---

# 25. Matrix View - Phase 2

Once the simple router is stable, add a traditional routing matrix.

Example:

```text
                    ENCODERS
DECODERS      Teams   Lectern   AirMedia   Signage
---------------------------------------------------
North TV        X
South TV                  X
Video Wall      X
Lobby                                        X
```

Clicking a cell performs a 1-to-1 route.

Multi-select support can later allow:

```text
Select decoder rows
Click one source column
```

The card-based view should remain available because it is easier on touchscreens.

---

# 26. Groups

Add decoder groups after individual routing works.

Example:

```text
Group: All Training Displays

Members:
- North TV
- South TV
- Video Wall
```

Routing:

```text
Teams PC -> All Training Displays
```

is simply expanded by the backend to:

```text
Teams PC -> North TV
Teams PC -> South TV
Teams PC -> Video Wall
```

Groups should exist only in our software.

There is no need to configure a Visionary group inside the endpoint infrastructure.

---

# 27. Presets / Scenes - Future

Later:

```text
Preset: Morning
North TV   -> Signage
South TV   -> Signage
Lobby      -> Signage
Video Wall -> Signage
```

Another:

```text
Preset: Presentation
North TV   -> Lectern
South TV   -> Lectern
Video Wall -> Lectern
Lobby      -> Signage
```

Again, a preset is only a list of normal decoder routing commands.

---

# 28. Discovery

Manual add must work before auto-discovery is attempted.

That keeps V1 testable even if multicast discovery behaves differently across customer networks.

Visionary documents several discovery methods and identifies multicast address:

```text
226.0.0.19
```

The API also states that the UDP API listens on unicast and multicast.

## Phase 1 Discovery

Manual add by IP.

Add a convenience function:

```text
Scan Subnet
```

User enters:

```text
192.168.8.0/24
```

The application concurrently probes addresses with a lightweight Visionary query on UDP port 8000.

Only responsive Visionary devices are shown.

Throttle concurrency so this is polite to the network.

## Phase 2 Discovery

Experiment with sending an API query to:

```text
226.0.0.19:8000
```

and collecting responses.

This appears technically compatible with the published API architecture, but it needs to be validated on actual hardware before we depend on it.

Do not make multicast discovery a requirement for V1.

---

# 29. Do Not Use MassConfig for Discovery

Newer Visionary decoders expose a Mass Configuration system that can discover all endpoints and export configuration information.

Do not use it for this application.

Reasons:

1. Visionary warns that Mass Configuration should only be enabled carefully.
2. Their documentation notes that video can drop while using it.
3. Our router only needs endpoint identity and routing state.
4. UDP probing gives us what we need without changing endpoint configuration.

---

# 30. Our Own REST API

The Windows server should expose a clean internal REST API.

Suggested endpoints:

```text
GET    /api/devices
POST   /api/devices
GET    /api/devices/{id}
PUT    /api/devices/{id}
DELETE /api/devices/{id}

POST   /api/devices/{id}/probe
POST   /api/devices/{id}/identify

GET    /api/routes
POST   /api/routes

POST   /api/discovery/scan
GET    /api/system/status
```

Example route request:

```json
{
  "encoderId": "b18c...",
  "decoderIds": [
    "98bd...",
    "82ff...",
    "722f..."
  ]
}
```

Example response:

```json
{
  "encoder": "Teams PC",
  "results": [
    {
      "decoder": "North TV",
      "success": true,
      "verified": true
    },
    {
      "decoder": "South TV",
      "success": true,
      "verified": true
    },
    {
      "decoder": "Video Wall",
      "success": false,
      "verified": false,
      "error": "UDP timeout"
    }
  ]
}
```

---

# 31. SignalR / Realtime UI

Use SignalR so the browser receives immediate updates.

Events can include:

```text
DeviceOnline
DeviceOffline
DeviceUpdated
RouteChanging
RouteChanged
RouteFailed
DiscoveryResult
```

The WebUI should not need to poll the ASP.NET backend every second.

The backend does device polling.

The backend pushes changes to connected browsers.

---

# 32. Logging

Logging is critical because this is control software.

Use structured logs.

Example:

```text
2026-08-28 19:44:12 INFO
ROUTE REQUEST
Encoder: Teams PC [10.10.1.21]
Decoder: North TV [10.10.1.31]

2026-08-28 19:44:12 DEBUG
UDP TX 10.10.1.31:8000
CMD=START&UNIT.ID=ALL&STREAM.HOST=10.10.1.21&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END

2026-08-28 19:44:12 DEBUG
UDP RX 10.10.1.31
&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS

2026-08-28 19:44:13 INFO
ROUTE VERIFIED
Decoder: North TV
STREAM.HOST=10.10.1.21
```

Do not hide the raw command/response information from the diagnostic logs.

That will make field troubleshooting dramatically easier.

---

# 33. Diagnostics Page

Add a simple diagnostics page early.

Show:

```text
Application version
Windows host name
Listening WebUI address
Detected NICs
Selected control NIC
Local IP
UDP port
Database location
Device count
Encoder count
Decoder count
Online count
Offline count
```

Provide a live command log.

Optional advanced tool:

```text
Send Query
```

Allow only read-only query commands from the UI in V1.

Do not create a general unrestricted command textbox in the production UI.

---

# 34. Network Interface Selection

A Windows PC can have:

- Ethernet
- Wi-Fi
- VPN
- Tailscale
- Hyper-V adapters
- VMware adapters
- WSL adapters

Do not assume the first NIC is the AV control network.

Settings page:

```text
Control Network Interface:
[ Intel Ethernet - 10.10.1.50 ]
```

Store the selected adapter.

Bind discovery/multicast operations to that interface.

Normal unicast route commands can use normal OS routing, but we should still display which interface Windows will use.

---

# 35. Visionary Network Requirements

Visionary recommends a properly configured managed network for AV over IP.

Their current FAQ recommends:

- IGMP Snooping on all switches
- IGMP Querier on at least one switch
- Jumbo frames
- Managed switches
- Separate VLANs for AV-over-IP, network audio, and control where appropriate

Typical published Visionary bandwidth figures include roughly:

```text
1080p60: 200-300 Mbps
2160p30: 250-750 Mbps
```

Our software does not carry the video.

It only sends lightweight control traffic.

Therefore a routing failure and a video-network failure must be treated as different things.

Example:

```text
Decoder answers API query successfully
STREAM.HOST is correct
Display still has no video
```

That indicates the control route worked and troubleshooting should move toward:

- multicast
- IGMP
- switch configuration
- encoder input status
- decoder output status
- HDCP
- EDID
- stream mode

The application should expose enough state to make that distinction obvious.

---

# 36. Error Handling

Never allow one dead endpoint to hang the entire router.

Every network call must have:

```text
Timeout
CancellationToken
Exception handling
Device-specific result
```

Group route example:

```text
4 decoders selected

3 successful
1 timeout
```

The route operation should finish and clearly identify the failed device.

---

# 37. Suggested Command Timeouts

Start conservatively and make them configurable.

Possible defaults:

```text
UDP query timeout: 1000 ms
UDP retry count: 1
Route verify delay: 250-500 ms
Route verify retries: 2-3
```

These are application defaults, not Visionary requirements.

Tune them after hardware testing.

---

# 38. Concurrency

Do not blast hundreds of simultaneous queries.

For bulk operations use bounded concurrency.

Example:

```text
Max concurrent endpoint operations: 8
```

Per endpoint:

```text
Only one outstanding query at a time.
```

Routing commands can be dispatched quickly, but verification should be controlled.

---

# 39. Startup Behavior

When the application starts:

1. Open database.
2. Load saved devices.
3. Start WebUI.
4. Start SignalR.
5. Begin endpoint heartbeat.
6. Query all decoders for `STREAM.HOST`.
7. Populate actual current routes.
8. Mark endpoints online/offline.
9. Do **not** automatically reroute anything.

A service restart should never unexpectedly change the AV system.

---

# 40. Shutdown Behavior

On application shutdown:

- Stop polling.
- Cancel outstanding operations.
- Flush logs.
- Close database.
- Do not alter current Visionary routes.
- Do not disconnect streams.

The actual Visionary system should continue operating normally without our application.

---

# 41. SQLite

SQLite is ideal for this project.

Tables can begin with:

```text
Devices
Groups
GroupMembers
Settings
```

Later:

```text
Presets
PresetRoutes
AuditLog
```

Use Entity Framework Core migrations.

Do not store volatile online state as authoritative configuration.

The current endpoint state should be rebuilt from the network after startup.

---

# 42. Initial Database Relationships

```text
Device
  Id
  Name
  Type
  IP
  Metadata...

Group
  Id
  Name

GroupMember
  GroupId
  DecoderDeviceId

Setting
  Key
  Value
```

Only decoders belong in routing destination groups.

---

# 43. Development Simulator

A device simulator will save a huge amount of time.

Create a test application or test fixture that listens on UDP port 8000 and behaves like fake Visionary endpoints.

Simulated decoder should understand:

```text
QUERY.STATUS
QUERY.ALL
QUERY.KEY=STREAM.HOST
STREAM.HOST=<IP>
UNIT.FU=TRUE
```

It should maintain an in-memory current encoder IP.

This allows Codex to build and test almost the entire application without sitting next to Visionary hardware.

---

# 44. Unit Tests

At minimum test:

## Command builder

Input:

```text
192.168.8.101
```

Expected:

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=192.168.8.101&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

## Response parser

Input:

```text
&STREAM.HOST=192.168.8.101&API.STATUS=SUCCESS_QUERY
```

Expected:

```text
STREAM.HOST = 192.168.8.101
API.STATUS = SUCCESS_QUERY
```

## Invalid response

Must not crash.

## Unknown key

Must preserve it.

## Offline endpoint

Must timeout cleanly.

## Group routing

One failed decoder must not fail successful decoder operations.

---

# 45. Hardware Acceptance Tests

## Test 1 - Probe Encoder

Expected:

```text
Encoder appears online
Model returned
IP returned
Firmware returned if available
```

## Test 2 - Probe Decoder

Expected:

```text
Decoder appears online
Current STREAM.HOST returned
```

## Test 3 - 1-to-1

```text
Encoder A -> Decoder A
```

Expected:

```text
Video changes
STREAM.HOST verifies Encoder A
UI updates
```

## Test 4 - Change Source

```text
Encoder B -> Decoder A
```

Expected:

```text
Video changes
STREAM.HOST verifies Encoder B
```

## Test 5 - 1-to-many

```text
Encoder A -> Decoder A
Encoder A -> Decoder B
Encoder A -> Decoder C
```

Expected:

```text
All three decoders switch
All three verify
```

## Test 6 - Offline Decoder

Unplug one decoder.

Route a group containing that decoder.

Expected:

```text
Other decoders switch
Offline decoder reports failure
UI does not hang
```

## Test 7 - External Route Change

Change a route using Vision Lite.

Expected:

```text
Our polling sees the new STREAM.HOST
WebUI updates automatically
```

## Test 8 - Restart Application

Expected:

```text
Device list survives
Current routes are rediscovered
No routing commands are automatically sent
```

---

# 46. Phase Plan

## Phase 0 - Project Skeleton

Create:

```text
ASP.NET Core server
React frontend
SQLite
Logging
Unit tests
```

Acceptance:

```text
Application starts on Windows.
Browser loads WebUI.
Database is created.
```

---

## Phase 1 - Visionary UDP Layer

Implement:

```text
UDP send
UDP receive
Command builder
Response parser
Timeout handling
Logging
```

Acceptance:

```text
Can manually query one endpoint by IP.
Raw TX/RX appears in logs.
```

---

## Phase 2 - Device Management

Implement:

```text
Add encoder
Add decoder
Probe
Edit friendly name
Delete
Online/offline
Identify
Open endpoint WebUI
```

Acceptance:

```text
Known devices persist through restart.
```

---

## Phase 3 - 1-to-1 Routing

Implement:

```text
Select encoder
Select decoder
Route
Verify STREAM.HOST
Realtime status
```

Acceptance:

```text
Repeated source switching works reliably on real hardware.
```

---

## Phase 4 - 1-to-Many Routing

Implement:

```text
Multi-select decoders
Route selected
Bounded concurrency
Per-decoder verification
Partial failure handling
```

Acceptance:

```text
One encoder successfully routes to multiple real decoders.
```

---

## Phase 5 - State Reconciliation

Implement:

```text
Decoder STREAM.HOST polling
Endpoint heartbeat
External route detection
SignalR updates
```

Acceptance:

```text
Vision Lite route changes appear in our UI automatically.
```

---

## Phase 6 - Discovery

Implement:

```text
Subnet scan
Auto-classification
Duplicate detection
```

Then experimentally validate:

```text
226.0.0.19 multicast API discovery
```

Acceptance:

```text
New Visionary endpoints can be discovered and selected for import.
```

---

## Phase 7 - Groups / Presets

Implement:

```text
Decoder groups
Route to group
Routing presets
```

Acceptance:

```text
Common room configurations can be recalled with one action.
```

---

## Phase 8 - Production Windows Packaging

Implement:

```text
Self-contained win-x64 publish
Windows Service support
Installer
Desktop shortcut to WebUI
Start on boot
Log rotation
Backup/export configuration
```

---

# 47. Recommended First Codex Milestone

Do not ask Codex to build everything at once.

The first useful milestone should be:

```text
Windows ASP.NET Core project
+
Visionary UDP protocol library
+
simple Devices page
+
simple Routing page
+
fake Visionary simulator
```

Specifically, the first build needs to prove these actions:

```text
Add Encoder:
10.0.0.10

Add Decoder:
10.0.0.20

Select Encoder
Select Decoder
Press Route

Generated UDP command:
CMD=START&UNIT.ID=ALL&STREAM.HOST=10.0.0.10&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END

Target:
10.0.0.20:8000

Then:
QUERY.KEY=STREAM.HOST

Expected:
10.0.0.10
```

Once that works with the simulator, take it to the real hardware.

---

# 48. Suggested Internal Classes

```text
VisionaryDevice
VisionaryEncoder
VisionaryDecoder

VisionaryCommandBuilder
VisionaryResponse
VisionaryResponseParser
VisionaryUdpTransport
VisionaryDeviceClient
VisionaryRoutingService
VisionaryPollingService
VisionaryDiscoveryService

DeviceRepository
RoutingRepository
SettingsRepository

DeviceHub
```

Do not over-engineer inheritance.

An enum-based `DeviceType` on one `VisionaryDevice` model may be enough.

---

# 49. Suggested Coding Rules

1. No raw Visionary command strings in controllers.
2. No UDP socket code in UI/controller classes.
3. All endpoint interactions pass through a device client/service.
4. All network calls async.
5. All network calls cancellable.
6. All network calls have timeouts.
7. Log raw TX/RX at debug level.
8. Never silently swallow a routing failure.
9. Do not persist assumed current routes as truth.
10. Verify routes from `STREAM.HOST`.
11. Never auto-route on application startup.
12. Never send destructive/configuration commands in V1.

---

# 50. Future Expansion

After routing is proven, the same transport layer can support additional Visionary features.

Potential future controls:

- Decoder HDMI/video output modes
- Logo/standby output
- CEC display power
- Encoder input selection
- Audio selection
- USB/KVM routing
- RS-232 over IP
- IR
- GPIO
- Video-wall controls
- LLDP/network diagnostics
- Input video timing/status
- EDID management
- Stream configuration
- Firmware information
- Q-SYS integration
- Crestron integration
- External REST API
- Room automation triggers

These should be layered onto the same device client rather than changing the core architecture.

---

# 51. External Control of Our Router

A useful long-term feature is to allow Q-SYS, Crestron, Bitfocus Companion, or other systems to control **our application** instead of each Visionary device directly.

Example:

```text
POST /api/routes
```

```json
{
  "source": "Teams PC",
  "destinations": [
    "North TV",
    "South TV"
  ]
}
```

Then our app handles:

```text
Visionary command generation
timeouts
verification
logging
state
```

This can eventually turn the Windows application into the central Visionary routing controller.

---

# 52. Key API Commands for V1

## Status

```text
CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END
```

## Query All

```text
CMD=START&UNIT.ID=ALL&QUERY.ALL=TRUE&CMD=END
```

## Query Current Decoder Source

```text
CMD=START&UNIT.ID=ALL&QUERY.KEY=STREAM.HOST&CMD=END
```

## Route Decoder

```text
CMD=START&UNIT.ID=ALL&STREAM.HOST=<ENCODER_IP>&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

## Identify Device

```text
CMD=START&UNIT.ID=ALL&UNIT.FU=TRUE&CMD=END
```

These are enough to build the first functional router.

---

# 53. Research Notes / Confidence

There are two important documentation realities.

## Confirmed

Public documentation confirms:

- Human-readable key/value API
- HTTP GET/POST
- UDP unicast/multicast
- UDP port 8000
- API multicast address 226.0.0.19
- `STREAM.HOST` decoder routing model
- `STREAM.CONNECT`
- `QUERY.KEY=STREAM.HOST`
- group routing by commanding each decoder
- 5-series use of the same basic API/routing approach
- status/query capabilities
- physical identify command

## API documentation access

Visionary's own manuals state that their full API is available to qualified system integrators through Visionary support.

The publicly available 4-series API specification provides substantial implementation detail, while public 5-series references show the same routing command and UDP port.

Before expanding the software into configuration-heavy 5-series functionality, obtain the current official API package directly from Visionary.

For the basic routing application described here, the publicly documented command set is sufficient to begin development and hardware testing.

---

# 54. Source Material

Visionary AV-over-IP products:

https://visionary-av.com/products/av-over-ip/

Visionary FAQ / network recommendations:

https://visionary-av.com/faq/

Visionary E5200 / D5200 User Manual:

https://visionary-av.com/wp-content/uploads/2024/10/VISIONARY-ED5200-UserManual-241023.pdf

Public Visionary 4-series / Duet API specification mirror:

https://audiobrains.com/data/visionarysolutions/others/duet_4X00_controiprotcol_2_x.pdf

Public Bitfocus Companion request containing 5-series API/routing reference and attached X5K API document:

https://github.com/bitfocus/companion-module-requests/issues/1587

---

# 55. Bottom Line

The difficult AV transport work is already handled by the Visionary endpoints.

Our program does not need to proxy or process video.

Our job is essentially:

```text
DEVICE DATABASE
       |
       v
SELECT ENCODER
       |
       v
SELECT DECODER(S)
       |
       v
SEND UDP ROUTING COMMAND(S)
       |
       v
QUERY STREAM.HOST
       |
       v
VERIFY + UPDATE WEBUI
```

That is a very achievable Windows application.

The critical first hardware test is simply:

```text
UDP -> DecoderIP:8000

CMD=START&UNIT.ID=ALL&STREAM.HOST=EncoderIP&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END
```

If that changes the decoder's source correctly, the core of the application is proven.

Everything after that is UI, persistence, health monitoring, grouping, and polish.
