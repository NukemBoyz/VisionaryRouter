using VisionaryRouter.Simulator;

// A small rack of fake Visionary endpoints, so the router can be developed and demonstrated
// without hardware.
//
// Each endpoint answers on its own loopback address using the real control port 8000, which
// keeps the addressing identical to a live system: routing works by telling a decoder which
// encoder IP to follow, so the encoders have to be distinguishable by address.
//
// Two of the decoders misbehave on purpose. The failure paths are the ones most likely to be
// wrong and the hardest to reproduce against working equipment.

var devices = new List<SimulatedDeviceConfig>
{
    new()
    {
        Name = "Lectern PC",
        Type = SimulatedDeviceType.Encoder,
        Address = "127.0.0.10",
        Model = "E5100",
        Serial = "SIM-ENC-001",
        MacAddress = "00:1B:44:11:3A:01"
    },
    new()
    {
        Name = "Teams PC",
        Type = SimulatedDeviceType.Encoder,
        Address = "127.0.0.11",
        Model = "E5200",
        Serial = "SIM-ENC-002",
        MacAddress = "00:1B:44:11:3A:02"
    },
    new()
    {
        Name = "Lobby Signage Player",
        Type = SimulatedDeviceType.Encoder,
        Address = "127.0.0.12",
        Model = "DuetE-5",
        Serial = "SIM-ENC-003",
        MacAddress = "00:1B:44:11:3A:03"
    },
    new()
    {
        Name = "Training Room North",
        Type = SimulatedDeviceType.Decoder,
        Address = "127.0.0.20",
        Model = "D5100",
        Serial = "SIM-DEC-001",
        MacAddress = "00:1B:44:11:3B:01",
        InitialStreamHost = "127.0.0.10"
    },
    new()
    {
        Name = "Training Room South",
        Type = SimulatedDeviceType.Decoder,
        Address = "127.0.0.21",
        Model = "D5100",
        Serial = "SIM-DEC-002",
        MacAddress = "00:1B:44:11:3B:02",
        InitialStreamHost = "127.0.0.11"
    },
    new()
    {
        Name = "Video Wall",
        Type = SimulatedDeviceType.Decoder,
        Address = "127.0.0.22",
        Model = "D5200",
        Serial = "SIM-DEC-003",
        MacAddress = "00:1B:44:11:3B:03",
        InitialStreamHost = "127.0.0.10"
    },
    new()
    {
        Name = "Lobby Display",
        Type = SimulatedDeviceType.Decoder,
        Address = "127.0.0.23",
        Model = "D4100",
        Serial = "SIM-DEC-004",
        MacAddress = "00:1B:44:11:3B:04",
        Unresponsive = true
    },
    new()
    {
        Name = "Breakout Room",
        Type = SimulatedDeviceType.Decoder,
        Address = "127.0.0.24",
        Model = "DuetD-5",
        Serial = "SIM-DEC-005",
        MacAddress = "00:1B:44:11:3B:05",
        RefuseToSwitch = true
    }
};

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");

Log("Visionary endpoint simulator");
Log($"Simulating {devices.Count} endpoints on UDP 8000. Ctrl+C to stop.");
Log("");

foreach (var device in devices)
{
    var quirk = device switch
    {
        { Unresponsive: true } => "  <- never answers (simulates powered down)",
        { RefuseToSwitch: true } => "  <- accepts routes but never switches",
        _ => string.Empty
    };

    Log($"  {device.Address,-12} {device.Type,-8} {device.Model,-10} {device.Name}{quirk}");
}

Log("");

var endpoints = devices.Select(config => new SimulatedEndpoint(config, Log)).ToList();

try
{
    await Task.WhenAll(endpoints.Select(endpoint => endpoint.RunAsync(shutdown.Token)));
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C.
}

Log("Simulator stopped.");
