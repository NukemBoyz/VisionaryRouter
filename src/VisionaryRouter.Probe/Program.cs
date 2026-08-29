using System.Net;
using Microsoft.Extensions.Logging;
using VisionaryRouter.Core.Protocol;

// Standalone Visionary endpoint probe.
//
// This exists to answer one question before anyone trusts the router application: does a
// real decoder actually accept STREAM.HOST=<encoder IP> and switch to it? The whole design
// rests on that, and a simulator cannot prove it - it would only agree with the assumption
// it was written from.
//
// Run this against real hardware first. It prints every byte sent and received, so if the
// endpoints behave differently from the published documentation, the difference is visible
// rather than buried.

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Warning)
    .AddConsole());

var options = new VisionaryTransportOptions { QueryTimeout = TimeSpan.FromMilliseconds(1500), RetryCount = 1 };
using var transport = new VisionaryUdpTransport(options, loggerFactory.CreateLogger<VisionaryUdpTransport>());

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();

try
{
    return command switch
    {
        "status" when args.Length >= 2 => await Status(args[1]),
        "info" when args.Length >= 2 => await Info(args[1]),
        "source" when args.Length >= 2 => await Source(args[1]),
        "route" when args.Length >= 3 => await Route(args[1], args[2]),
        "identify" when args.Length >= 2 => await Identify(args[1]),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed: {ex.Message}");
    return 1;
}

int Usage()
{
    PrintUsage();
    return 2;
}

void PrintUsage()
{
    Console.WriteLine("""
        Visionary endpoint probe

        Talks to a Visionary endpoint over UDP 8000 and prints the raw exchange.
        Read-only except for 'route' and 'identify'.

          probe status   <ip>                  Is it alive, and what is it?
          probe info     <ip>                  Full configuration dump
          probe source   <ip>                  What encoder is this decoder following?
          probe route    <decoder-ip> <encoder-ip>
                                               Point a decoder at an encoder, then verify
          probe identify <ip>                  Flash the front-panel LEDs for ~10 seconds

        Nothing here writes endpoint configuration to nonvolatile storage.
        """);
}

async Task<int> Status(string ip)
{
    var result = await Send(ip, VisionaryCommand.StatusQuery());
    return result is null ? 1 : 0;
}

async Task<int> Info(string ip)
{
    var result = await Send(ip, VisionaryCommand.QueryAll());

    if (result is null)
    {
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Parsed:");

    foreach (var (key, value) in result.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {key,-24} {value}");
    }

    return 0;
}

async Task<int> Source(string ip)
{
    var result = await Send(ip, VisionaryCommand.QueryCurrentRoute());

    if (result is null)
    {
        return 1;
    }

    var host = result.Get(VisionaryKeys.StreamHost);

    Console.WriteLine();
    Console.WriteLine(string.IsNullOrWhiteSpace(host)
        ? "This endpoint reports no source."
        : $"Following encoder: {host}");

    return 0;
}

async Task<int> Route(string decoderIp, string encoderIp)
{
    // The one that matters. If this does not change the picture on the display, the routing
    // model the application is built on is wrong, and nothing else is worth debugging first.
    Console.WriteLine($"Routing decoder {decoderIp} to encoder {encoderIp}");
    Console.WriteLine();

    var sent = await Send(decoderIp, VisionaryCommand.Route(encoderIp));

    if (sent is null)
    {
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Waiting for the switch to settle...");
    await Task.Delay(1500);

    var verify = await Send(decoderIp, VisionaryCommand.QueryCurrentRoute());

    if (verify is null)
    {
        return 1;
    }

    var reported = verify.Get(VisionaryKeys.StreamHost);

    Console.WriteLine();

    if (string.Equals(reported, encoderIp, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"VERIFIED - decoder reports STREAM.HOST={reported}");
        Console.WriteLine("Now confirm the display actually shows the new source.");
        return 0;
    }

    Console.WriteLine($"NOT VERIFIED - asked for {encoderIp}, decoder reports " +
                      $"{(string.IsNullOrWhiteSpace(reported) ? "nothing" : reported)}");
    return 1;
}

async Task<int> Identify(string ip)
{
    var result = await Send(ip, VisionaryCommand.Identify());

    if (result is not null)
    {
        Console.WriteLine();
        Console.WriteLine("The endpoint's front panel should be flashing now.");
    }

    return result is null ? 1 : 0;
}

async Task<VisionaryResponse?> Send(string ip, string command)
{
    if (!IPAddress.TryParse(ip, out var address))
    {
        Console.Error.WriteLine($"'{ip}' is not a valid IP address");
        return null;
    }

    var endpoint = new IPEndPoint(address, VisionaryCommand.DefaultPort);

    Console.WriteLine($"TX {endpoint}");
    Console.WriteLine($"   {command}");

    var result = await transport.QueryAsync(endpoint, command);

    if (!result.Success || result.Response is null)
    {
        Console.WriteLine($"RX (nothing) - {result.Error}");
        return null;
    }

    Console.WriteLine($"RX {ip}");
    Console.WriteLine($"   {result.Response.Raw}");

    return result.Response;
}
