using System.Diagnostics;
using System.Net;
using System.Text.Json;
using VisionaryRouter.Core.Devices;
using VisionaryRouter.Core.Models;
using VisionaryRouter.Core.Protocol;
using VisionaryRouter.Core.Routing;
using VisionaryRouter.Server.Data;
using VisionaryRouter.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

var webPort = builder.Configuration.GetValue("Router:WebPort", 8088);
var listenOnAllInterfaces = builder.Configuration.GetValue("Router:ListenOnAllInterfaces", false);
var openBrowser = builder.Configuration.GetValue("Router:OpenBrowserOnStart", true);

// Loopback by default. Serving the UI to the whole network is useful for a wall tablet, but
// that should be a decision someone made, not something that happens because the app ran.
builder.WebHost.UseUrls(listenOnAllInterfaces
    ? $"http://0.0.0.0:{webPort}"
    : $"http://localhost:{webPort}");

var dataDirectory = ResolveDataDirectory();
var databasePath = Path.Combine(dataDirectory, "router.db");

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

builder.Services.AddSingleton(new VisionaryTransportOptions());
builder.Services.AddSingleton(new RoutingOptions());
builder.Services.AddSingleton(new PollingOptions());

builder.Services.AddSingleton<IVisionaryTransport, VisionaryUdpTransport>();
builder.Services.AddSingleton<VisionaryDeviceClient>();
builder.Services.AddSingleton<RoutingService>();

builder.Services.AddSingleton(new DeviceStore(databasePath));
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<EventStream>();
builder.Services.AddHostedService<PollingService>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Startup: load saved configuration, then learn current state from the network.
// Nothing is routed here. Restarting this application must never change what is
// on the displays.
// ---------------------------------------------------------------------------

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var store = app.Services.GetRequiredService<DeviceStore>();
var registry = app.Services.GetRequiredService<DeviceRegistry>();

store.Initialize();
registry.Load();

logger.LogInformation("Database: {Path}", databasePath);
logger.LogInformation("Loaded {Count} device(s)", registry.All().Count);

app.UseDefaultFiles();

app.UseStaticFiles(new StaticFileOptions
{
    // Revalidate every request rather than serving from cache blind. The UI is a handful of
    // small files coming off localhost, so the cost is a 304, and the alternative is an
    // operator looking at a stale control surface after an update - which is the one thing
    // this application must not do.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

// ---------------------------------------------------------------------------
// Device management
// ---------------------------------------------------------------------------

app.MapGet("/api/devices", (DeviceRegistry devices) => Results.Ok(DeviceView.From(devices)));

app.MapPost("/api/devices", async (
    DeviceRequest request,
    DeviceRegistry devices,
    VisionaryDeviceClient client,
    EventStream events) =>
{
    if (!IPAddress.TryParse(request.IpAddress, out _))
    {
        return Results.BadRequest(new { error = $"{request.IpAddress} is not a valid IP address" });
    }

    var device = new Device
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(request.Name) ? request.IpAddress : request.Name.Trim(),
        Type = ParseType(request.Type),
        IpAddress = request.IpAddress.Trim(),
        ControlPort = request.ControlPort ?? VisionaryCommand.DefaultPort
    };

    // Probe on add, so the list is useful immediately and a wrong address is obvious at the
    // moment it is typed rather than the first time someone tries to route.
    await client.ProbeAsync(device);

    devices.Add(device);
    events.Publish("devices", DeviceView.From(devices));

    return Results.Ok(DeviceView.From(device, devices));
});

app.MapPut("/api/devices/{id:guid}", (
    Guid id,
    DeviceRequest request,
    DeviceRegistry devices,
    EventStream events) =>
{
    var device = devices.Find(id);
    if (device is null)
    {
        return Results.NotFound();
    }

    if (!string.IsNullOrWhiteSpace(request.Name))
    {
        device.Name = request.Name.Trim();
    }

    if (!string.IsNullOrWhiteSpace(request.IpAddress))
    {
        if (!IPAddress.TryParse(request.IpAddress, out _))
        {
            return Results.BadRequest(new { error = $"{request.IpAddress} is not a valid IP address" });
        }

        device.IpAddress = request.IpAddress.Trim();
    }

    if (request.ControlPort is { } port)
    {
        device.ControlPort = port;
    }

    if (request.Type is not null)
    {
        device.Type = ParseType(request.Type);
    }

    if (request.Enabled is { } enabled)
    {
        device.Enabled = enabled;
    }

    devices.Save(device);
    events.Publish("devices", DeviceView.From(devices));

    return Results.Ok(DeviceView.From(device, devices));
});

app.MapDelete("/api/devices/{id:guid}", (Guid id, DeviceRegistry devices, EventStream events) =>
{
    if (!devices.Remove(id))
    {
        return Results.NotFound();
    }

    events.Publish("devices", DeviceView.From(devices));
    return Results.NoContent();
});

app.MapPost("/api/devices/{id:guid}/probe", async (
    Guid id,
    DeviceRegistry devices,
    VisionaryDeviceClient client,
    EventStream events) =>
{
    var device = devices.Find(id);
    if (device is null)
    {
        return Results.NotFound();
    }

    var result = await client.ProbeAsync(device);
    devices.Save(device);
    events.Publish("devices", DeviceView.From(devices));

    return Results.Ok(new
    {
        device = DeviceView.From(device, devices),
        success = result.Success,
        error = result.Error,
        raw = result.Response?.Raw
    });
});

app.MapPost("/api/devices/{id:guid}/identify", async (
    Guid id,
    DeviceRegistry devices,
    VisionaryDeviceClient client) =>
{
    var device = devices.Find(id);
    if (device is null)
    {
        return Results.NotFound();
    }

    var result = await client.IdentifyAsync(device);

    return Results.Ok(new { success = result.Success, error = result.Error, raw = result.Response?.Raw });
});

// ---------------------------------------------------------------------------
// Routing
// ---------------------------------------------------------------------------

app.MapPost("/api/routes", async (
    RouteRequest request,
    DeviceRegistry devices,
    RoutingService routing,
    EventStream events,
    ILogger<Program> log) =>
{
    var encoder = devices.Find(request.EncoderId);
    if (encoder is null)
    {
        return Results.BadRequest(new { error = "Unknown encoder" });
    }

    if (encoder.Type != DeviceType.Encoder)
    {
        return Results.BadRequest(new { error = $"{encoder.Name} is not an encoder" });
    }

    var decoders = new List<Device>();

    foreach (var decoderId in request.DecoderIds)
    {
        var decoder = devices.Find(decoderId);

        if (decoder is null)
        {
            return Results.BadRequest(new { error = $"Unknown decoder {decoderId}" });
        }

        if (decoder.Type != DeviceType.Decoder)
        {
            return Results.BadRequest(new { error = $"{decoder.Name} is not a decoder" });
        }

        decoders.Add(decoder);
    }

    if (decoders.Count == 0)
    {
        return Results.BadRequest(new { error = "Select at least one destination" });
    }

    // A unicast encoder cannot feed several decoders at once. Warn rather than block:
    // stream configuration is out of scope for this application, and the operator may know
    // something we do not.
    string? warning = null;
    if (decoders.Count > 1 &&
        string.Equals(encoder.StreamMode, "unicast", StringComparison.OrdinalIgnoreCase))
    {
        warning = $"{encoder.Name} reports STREAM.MODE=unicast. Routing one unicast encoder to " +
                  "several decoders is unlikely to work; set the encoder to multicast in Vision " +
                  "Lite or its own web interface.";
        log.LogWarning("{Warning}", warning);
    }

    var result = await routing.RouteAsync(encoder, decoders);

    events.Publish("devices", DeviceView.From(devices));

    return Results.Ok(new
    {
        encoder = encoder.Name,
        encoderIp = encoder.IpAddress,
        warning,
        successCount = result.SuccessCount,
        failureCount = result.FailureCount,
        results = result.Results.Select(r => new
        {
            decoderId = r.DecoderId,
            decoder = r.DecoderName,
            status = r.Status.ToString(),
            verified = r.Verified,
            reportedEncoderIp = r.ReportedEncoderIp,
            error = r.Error
        })
    });
});

// ---------------------------------------------------------------------------
// Live updates and diagnostics
// ---------------------------------------------------------------------------

app.MapGet("/api/events", async (HttpContext context, EventStream events, DeviceRegistry devices) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    using var subscription = events.Subscribe(out var reader);

    // Send current state immediately, so a newly opened tab is correct before the next poll
    // rather than blank for a few seconds.
    var snapshot = JsonSerializer.Serialize(DeviceView.From(devices), json);
    await context.Response.WriteAsync($"event: devices\ndata: {snapshot}\n\n");
    await context.Response.Body.FlushAsync();

    try
    {
        await foreach (var frame in reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync(frame, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // The browser navigated away or closed the tab.
    }
});

app.MapGet("/api/system/status", (DeviceRegistry devices, VisionaryTransportOptions transport) =>
{
    var all = devices.All();

    return Results.Ok(new
    {
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
        host = Environment.MachineName,
        webPort,
        listenOnAllInterfaces,
        controlPort = transport.Port,
        databasePath,
        deviceCount = all.Count,
        encoderCount = all.Count(d => d.Type == DeviceType.Encoder),
        decoderCount = all.Count(d => d.Type == DeviceType.Decoder),
        onlineCount = all.Count(d => d.OnlineState == OnlineState.Online),
        offlineCount = all.Count(d => d.OnlineState == OnlineState.Offline),
        interfaces = NetworkInterfaces()
    });
});

// ---------------------------------------------------------------------------

var url = $"http://localhost:{webPort}";
logger.LogInformation("Web UI: {Url}", url);

if (openBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() => LaunchBrowser(url, logger));
}

app.Run();

return;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static DeviceType ParseType(string? type) => type?.Trim().ToLowerInvariant() switch
{
    "encoder" => DeviceType.Encoder,
    "decoder" => DeviceType.Decoder,
    _ => DeviceType.Unknown
};

static string ResolveDataDirectory()
{
    // ProgramData is where this belongs on a machine running the router as a service. If it
    // cannot be created - locked-down machine, user without rights - fall back to the user's
    // own app data rather than refusing to start.
    var preferred = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VisionaryRouter");

    try
    {
        Directory.CreateDirectory(preferred);
        return preferred;
    }
    catch (Exception)
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisionaryRouter");

        Directory.CreateDirectory(fallback);
        return fallback;
    }
}

static object[] NetworkInterfaces() =>
    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses
            .Where(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(addr => (object)new
            {
                name = nic.Name,
                description = nic.Description,
                address = addr.Address.ToString()
            }))
        .ToArray();

static void LaunchBrowser(string url, ILogger logger)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        // Failing to open a browser is not a reason to refuse to run.
        logger.LogWarning(ex, "Could not open a browser automatically. Open {Url} manually.", url);
    }
}

// ---------------------------------------------------------------------------
// Request shapes
// ---------------------------------------------------------------------------

internal sealed record DeviceRequest(
    string? Name,
    string IpAddress,
    string? Type,
    int? ControlPort,
    bool? Enabled);

internal sealed record RouteRequest(Guid EncoderId, Guid[] DecoderIds);
