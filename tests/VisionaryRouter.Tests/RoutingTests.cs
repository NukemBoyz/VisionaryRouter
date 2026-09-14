using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using VisionaryRouter.Core.Devices;
using VisionaryRouter.Core.Models;
using VisionaryRouter.Core.Protocol;
using VisionaryRouter.Core.Routing;

namespace VisionaryRouter.Tests;

/// <summary>
/// An in-memory stand-in for a rack of endpoints.
///
/// Routing behaviour is tested here rather than against the UDP transport so the failure
/// modes that matter - a decoder that never answers, one that accepts a switch and ignores
/// it - can be produced on demand instead of waited for.
/// </summary>
internal sealed class FakeTransport : IVisionaryTransport
{
    private readonly Dictionary<string, FakeEndpoint> _endpoints = new();

    public List<(string Target, string Command)> Sent { get; } = [];

    public FakeEndpoint AddDecoder(string ip, string? initialSource = null)
    {
        var endpoint = new FakeEndpoint { StreamHost = initialSource ?? string.Empty, StreamConnected = !string.IsNullOrWhiteSpace(initialSource) };
        _endpoints[ip] = endpoint;
        return endpoint;
    }

    public Task SendAsync(IPEndPoint target, string command, CancellationToken cancellationToken = default)
    {
        Sent.Add((target.Address.ToString(), command));
        return Task.CompletedTask;
    }

    public Task<VisionaryQueryResult> QueryAsync(IPEndPoint target, string command, CancellationToken cancellationToken = default)
    {
        var ip = target.Address.ToString();
        Sent.Add((ip, command));

        if (!_endpoints.TryGetValue(ip, out var endpoint) || endpoint.Unresponsive)
        {
            return Task.FromResult(VisionaryQueryResult.Timeout(ip, TimeSpan.FromMilliseconds(1000)));
        }

        var request = VisionaryResponse.Parse(command);

        if (request.TryGet("QUERY.KEY", out var key) && key == VisionaryKeys.StreamHost)
        {
            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse($"&STREAM.HOST={endpoint.StreamHost}&API.STATUS=SUCCESS_QUERY")));
        }

        if (request.TryGet("QUERY.KEY", out key) && key == VisionaryKeys.StreamConnect)
        {
            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse($"&STREAM.CONNECT={endpoint.StreamConnected.ToString().ToUpperInvariant()}&API.STATUS=SUCCESS_QUERY")));
        }

        if (request.TryGet("QUERY.KEY", out key) && key == VisionaryKeys.StreamVideo)
        {
            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse($"&STREAM.VIDEO={endpoint.VideoInput}&API.STATUS=SUCCESS_QUERY")));
        }

        if (request.TryGet(VisionaryKeys.StreamVideo, out var requestedInput))
        {
            endpoint.VideoInput = requestedInput;
            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse("&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS")));
        }

        if (request.TryGet("QUERY.VIDEO_TIMING", out _))
        {
            // On a decoder this reflects the incoming stream. The parking address carries no
            // video, which is why the router uses it rather than another encoder.
            var receiving = endpoint.StreamConnected
                && !string.IsNullOrWhiteSpace(endpoint.StreamHost)
                && endpoint.StreamHost != "0.0.0.0";

            var timing = receiving ? "Timing Table: [1920]X[1080] [60]Hz" : "Not Available";

            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse($"&VIDEO.TIMING={timing}&QUERY.VIDEO_TIMING=OK&API.STATUS=SUCCESS_QUERY")));
        }

        if (request.TryGet(VisionaryKeys.StreamConnect, out var connect) && connect == "FALSE")
        {
            // STREAM.HOST deliberately survives a disconnect, as it does on real hardware.
            // Clearing it here would hide the bug this fake exists to reproduce: the decoder
            // keeps the address, so the next route to that same address is a no-op.
            endpoint.StreamConnected = false;
            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse("&STREAM.CONNECT=STOP_STREAM&API.STATUS=SUCCESS")));
        }

        if (request.TryGet(VisionaryKeys.StreamHost, out var requested))
        {
            if (endpoint.RefuseToSwitch)
            {
                return Task.FromResult(VisionaryQueryResult.Ok(
                    VisionaryResponse.Parse("&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS")));
            }

            // Firmware 2.3.152 only acts when the address actually changes, and reports
            // SUCCESS while doing nothing. Asked for the source it already holds, it answers
            // NO_CHANGE / ALREADY_CONNECTED - even when disconnected, which is what strands a
            // decoder with the right STREAM.HOST and a dead stream.
            if (string.Equals(endpoint.StreamHost, requested, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(VisionaryQueryResult.Ok(VisionaryResponse.Parse(
                    "&STREAM.HOST=NO_CHANGE&STREAM.CONNECT=ALREADY_CONNECTED&API.STATUS=SUCCESS")));
            }

            endpoint.StreamHost = requested;
            endpoint.StreamConnected = true;

            return Task.FromResult(VisionaryQueryResult.Ok(
                VisionaryResponse.Parse("&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS")));
        }

        return Task.FromResult(VisionaryQueryResult.Ok(VisionaryResponse.Parse("&API.STATUS=SUCCESS")));
    }
}

internal sealed class FakeEndpoint
{
    public string StreamHost { get; set; } = string.Empty;
    public string VideoInput { get; set; } = "HDMI1";
    public bool Unresponsive { get; set; }
    public bool RefuseToSwitch { get; set; }
    public bool StreamConnected { get; set; }
}

public class RoutingServiceTests
{
    // Verification delays are collapsed so the tests are fast; the retry behaviour is what
    // is under test, not the wall-clock timing.
    private static readonly RoutingOptions FastOptions = new()
    {
        VerifyDelay = TimeSpan.Zero,
        VerifyInterval = TimeSpan.Zero,
        VerifyRetries = 2,

        // The fake endpoint switches instantly, so the settling waits only slow the suite.
        // The retry and ordering behaviour is what is under test, not the wall-clock timing.
        ReestablishPollInterval = TimeSpan.Zero
    };

    private static (RoutingService Routing, FakeTransport Transport) Build()
    {
        var transport = new FakeTransport();
        var client = new VisionaryDeviceClient(transport, NullLogger<VisionaryDeviceClient>.Instance);
        var routing = new RoutingService(client, FastOptions, NullLogger<RoutingService>.Instance);

        return (routing, transport);
    }

    private static Device Encoder(string ip, string name = "Source") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = DeviceType.Encoder,
        IpAddress = ip
    };

    private static Device Decoder(string ip, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = DeviceType.Decoder,
        IpAddress = ip
    };

    [Fact]
    public async Task Clears_a_decoder_route_and_verifies_no_source()
    {
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20", "10.0.0.10");
        var decoder = Decoder("10.0.0.20", "North TV");
        decoder.AssignedEncoderIp = "10.0.0.10";

        var result = await routing.ClearAsync(decoder);

        Assert.True(result.Success);
        Assert.Equal(RouteStatus.Accepted, result.Status);
        Assert.False(result.Verified);
        Assert.Null(decoder.AssignedEncoderIp);
        Assert.Contains(transport.Sent, s => s.Target == "10.0.0.20" && s.Command.Contains("STREAM.CONNECT=FALSE"));
    }

    [Fact]
    public async Task Routes_one_decoder_and_verifies_the_result()
    {
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "North TV")]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(RouteStatus.Verified, result.Results[0].Status);
        Assert.Equal("10.0.0.10", result.Results[0].ReportedEncoderIp);
    }

    [Fact]
    public async Task Sends_the_route_command_to_the_decoder_not_the_encoder()
    {
        // The encoder is never told where to send video. Getting this backwards would look
        // like it worked in a one-encoder system and fail in every real one.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");

        await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "North TV")]);

        var routeCommands = transport.Sent.Where(s => s.Command.Contains("STREAM.CONNECT=TRUE")).ToList();

        Assert.All(routeCommands, sent => Assert.Equal("10.0.0.20", sent.Target));
        Assert.Contains(routeCommands, sent => sent.Command.Contains("STREAM.HOST=10.0.0.10"));
    }

    [Fact]
    public async Task Routes_one_encoder_to_many_decoders()
    {
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");
        transport.AddDecoder("10.0.0.21");
        transport.AddDecoder("10.0.0.22");

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [
            Decoder("10.0.0.20", "North TV"),
            Decoder("10.0.0.21", "South TV"),
            Decoder("10.0.0.22", "Video Wall")
        ]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(3, result.SuccessCount);
    }

    [Fact]
    public async Task One_dead_decoder_does_not_fail_the_others()
    {
        // The whole point of per-decoder results: a powered-down display in a group must not
        // make three working ones report failure.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");
        transport.AddDecoder("10.0.0.21").Unresponsive = true;
        transport.AddDecoder("10.0.0.22");

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [
            Decoder("10.0.0.20", "North TV"),
            Decoder("10.0.0.21", "Lobby Display"),
            Decoder("10.0.0.22", "Video Wall")
        ]);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.False(result.AllSucceeded);

        var failed = result.Results.Single(r => !r.Success);
        Assert.Equal("Lobby Display", failed.DecoderName);
        Assert.Equal(RouteStatus.Offline, failed.Status);
    }

    [Fact]
    public async Task A_decoder_that_accepts_but_never_switches_is_reported_as_failed()
    {
        // The dangerous case: the command succeeded, so an optimistic UI would show green
        // while the display still shows the old source.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20", initialSource: "10.0.0.99").RefuseToSwitch = true;

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "Breakout Room")]);

        var outcome = result.Results.Single();

        Assert.Equal(RouteStatus.Failed, outcome.Status);
        Assert.True(outcome.Commanded);
        Assert.False(outcome.Verified);
        Assert.Equal("10.0.0.99", outcome.ReportedEncoderIp);
        Assert.Contains("10.0.0.99", outcome.Error);
    }

    [Fact]
    public async Task A_decoder_reporting_no_source_at_all_reads_as_no_source_not_as_blank()
    {
        // Endpoints report an empty STREAM.HOST rather than omitting the key, so the failure
        // message has to handle blank the same way it handles missing.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20").RefuseToSwitch = true;

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "Breakout Room")]);

        var outcome = result.Results.Single();

        Assert.Equal(RouteStatus.Failed, outcome.Status);
        Assert.Contains("reports no source", outcome.Error);
        Assert.DoesNotContain("following ,", outcome.Error);
    }

    [Fact]
    public async Task Re_routing_to_the_same_source_after_a_clear_restores_video()
    {
        // The failure found on hardware: route, clear, then route the same encoder again. The
        // decoder keeps STREAM.HOST, answers NO_CHANGE / ALREADY_CONNECTED with API.STATUS
        // SUCCESS, and passes no video - while STREAM.HOST still reads correct, so the old
        // verification called it a success and the screen stayed black.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");

        var encoder = Encoder("10.0.0.10");
        var decoder = Decoder("10.0.0.20", "North TV");

        await routing.RouteAsync(encoder, [decoder]);
        await routing.ClearAsync(decoder);

        var result = await routing.RouteAsync(encoder, [decoder]);
        var outcome = result.Results.Single();

        Assert.Equal(RouteStatus.Verified, outcome.Status);
        Assert.Equal("10.0.0.10", outcome.ReportedEncoderIp);

        // The point of the fix: not merely set to the encoder, but actually receiving.
        Assert.True(outcome.ReceivingVideo);
    }

    [Fact]
    public async Task Forcing_a_re_establish_moves_the_decoder_away_and_back()
    {
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");

        var encoder = Encoder("10.0.0.10");
        var decoder = Decoder("10.0.0.20", "North TV");

        await routing.RouteAsync(encoder, [decoder]);
        await routing.ClearAsync(decoder);
        transport.Sent.Clear();

        await routing.RouteAsync(encoder, [decoder]);

        var routed = transport.Sent
            .Where(s => s.Command.Contains("STREAM.CONNECT=TRUE"))
            .Select(s => s.Command)
            .ToList();

        // The address has to change for the endpoint to act, so the parking address is
        // visited before returning to the encoder.
        Assert.Contains(routed, c => c.Contains("STREAM.HOST=0.0.0.0"));
        Assert.Contains(routed, c => c.Contains("STREAM.HOST=10.0.0.10"));
        Assert.EndsWith("STREAM.HOST=10.0.0.10&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END", routed[^1]);
    }

    [Fact]
    public async Task An_ordinary_route_to_a_new_source_does_not_bounce_through_the_parking_address()
    {
        // The re-establish is a repair, not part of every switch. Parking mid-route would put
        // a black frame on the wall on every ordinary source change.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20", initialSource: "10.0.0.11");

        await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "North TV")]);

        Assert.DoesNotContain(transport.Sent, s => s.Command.Contains("STREAM.HOST=0.0.0.0"));
    }

    [Fact]
    public async Task A_verified_route_reports_when_no_video_is_arriving()
    {
        // A decoder set to an encoder that is passing nothing verifies, but must not be
        // reported as though a picture reached the display.
        var (routing, transport) = Build();
        var endpoint = transport.AddDecoder("10.0.0.20");

        await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "North TV")]);

        // Stand in for a source with no input signal: set, but nothing coming through.
        endpoint.StreamConnected = false;

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [Decoder("10.0.0.20", "North TV")]);
        var outcome = result.Results.Single();

        Assert.Equal("10.0.0.10", outcome.ReportedEncoderIp);
        Assert.NotNull(outcome.ReceivingVideo);
    }

    [Fact]
    public async Task Records_the_assigned_encoder_even_when_the_decoder_never_switches()
    {
        // The UI colours a destination by the encoder assigned to it, so the assignment has to
        // be recorded on the attempt. An endpoint that accepts a route and ignores it still
        // shows the operator where they sent it - the reported source, separately, is what
        // says whether it took.
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20").RefuseToSwitch = true;

        var decoder = Decoder("10.0.0.20", "Breakout Room");
        await routing.RouteAsync(Encoder("10.0.0.10"), [decoder]);

        Assert.Equal("10.0.0.10", decoder.AssignedEncoderIp);
        Assert.NotEqual(decoder.AssignedEncoderIp, decoder.CurrentEncoderIp);
    }

    [Fact]
    public async Task Records_the_assigned_encoder_even_when_the_decoder_is_unreachable()
    {
        var (routing, _) = Build();

        var decoder = Decoder("10.0.0.99", "Lobby Display");
        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [decoder]);

        Assert.Equal(RouteStatus.Offline, result.Results.Single().Status);
        Assert.Equal("10.0.0.10", decoder.AssignedEncoderIp);
    }

    [Fact]
    public async Task A_disabled_decoder_is_skipped_without_being_contacted()
    {
        var (routing, transport) = Build();
        transport.AddDecoder("10.0.0.20");

        var decoder = Decoder("10.0.0.20", "North TV");
        decoder.Enabled = false;

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), [decoder]);

        Assert.Equal(RouteStatus.Failed, result.Results.Single().Status);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Routing_an_empty_destination_list_is_a_no_op()
    {
        var (routing, transport) = Build();

        var result = await routing.RouteAsync(Encoder("10.0.0.10"), []);

        Assert.Empty(result.Results);
        Assert.False(result.AllSucceeded);
        Assert.Empty(transport.Sent);
    }
}

public class DeviceClientTests
{
    [Fact]
    public async Task Reading_a_current_source_updates_the_device_and_marks_it_online()
    {
        var transport = new FakeTransport();
        transport.AddDecoder("10.0.0.20", initialSource: "10.0.0.10");

        var client = new VisionaryDeviceClient(transport, NullLogger<VisionaryDeviceClient>.Instance);

        var decoder = new Device
        {
            Id = Guid.NewGuid(),
            Name = "North TV",
            Type = DeviceType.Decoder,
            IpAddress = "10.0.0.20"
        };

        var source = await client.ReadCurrentSourceAsync(decoder);

        Assert.Equal("10.0.0.10", source);
        Assert.Equal("10.0.0.10", decoder.CurrentEncoderIp);
        Assert.Equal(OnlineState.Online, decoder.OnlineState);
        Assert.NotNull(decoder.LastSeenUtc);
    }

    [Fact]
    public async Task An_endpoint_that_does_not_answer_is_marked_offline_with_the_reason()
    {
        var transport = new FakeTransport();
        var client = new VisionaryDeviceClient(transport, NullLogger<VisionaryDeviceClient>.Instance);

        var decoder = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Lobby Display",
            Type = DeviceType.Decoder,
            IpAddress = "10.0.0.99"
        };

        await client.PingAsync(decoder);

        Assert.Equal(OnlineState.Offline, decoder.OnlineState);
        Assert.NotNull(decoder.LastError);
    }

    [Fact]
    public async Task An_invalid_address_fails_without_touching_the_network()
    {
        var transport = new FakeTransport();
        var client = new VisionaryDeviceClient(transport, NullLogger<VisionaryDeviceClient>.Instance);

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Typo",
            Type = DeviceType.Decoder,
            IpAddress = "not-an-ip"
        };

        var result = await client.PingAsync(device);

        Assert.False(result.Success);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Encoder_input_can_be_selected_and_read_back()
    {
        var transport = new FakeTransport();
        transport.AddDecoder("10.0.0.10");
        var client = new VisionaryDeviceClient(transport, NullLogger<VisionaryDeviceClient>.Instance);
        var encoder = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Lectern",
            Type = DeviceType.Encoder,
            IpAddress = "10.0.0.10"
        };

        var sent = await client.SelectVideoInputAsync(encoder, "USB-C");
        var reported = await client.ReadVideoInputAsync(encoder);

        Assert.True(sent.Success);
        Assert.Equal("USB-C", reported);
        Assert.Equal("USB-C", encoder.VideoInput);
    }
}
