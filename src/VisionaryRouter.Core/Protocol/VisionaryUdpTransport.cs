using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// UDP implementation of the Visionary control transport.
///
/// Each exchange gets its own short-lived socket. The protocol carries no transaction id, so
/// a long-lived shared socket would leave us matching replies to requests by source address
/// and arrival order - which breaks the moment two queries to one endpoint overlap. A socket
/// per transaction sidesteps the correlation problem entirely, and a per-endpoint lock keeps
/// us from having two conversations with the same device at once.
/// </summary>
public sealed class VisionaryUdpTransport : IVisionaryTransport, IDisposable
{
    private readonly VisionaryTransportOptions _options;
    private readonly ILogger<VisionaryUdpTransport> _logger;

    // One gate per endpoint. Bounds only per-device concurrency; unrelated devices still
    // proceed in parallel.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new();

    public VisionaryUdpTransport(VisionaryTransportOptions options, ILogger<VisionaryUdpTransport> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(IPEndPoint target, string command, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.ASCII.GetBytes(command);

        using var socket = new UdpClient(AddressFamily.InterNetwork);
        _logger.LogDebug("UDP TX {Endpoint} {Command}", target, command);
        await socket.SendAsync(payload, target, cancellationToken);
    }

    public async Task<VisionaryQueryResult> QueryAsync(
        IPEndPoint target,
        string command,
        CancellationToken cancellationToken = default)
    {
        var gate = _deviceLocks.GetOrAdd(target.ToString(), _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var attempts = Math.Max(1, _options.RetryCount + 1);

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await TryQueryOnceAsync(target, command, cancellationToken);

                if (result.Success || attempt == attempts)
                {
                    return result;
                }

                _logger.LogDebug(
                    "No reply from {Target} on attempt {Attempt} of {Attempts}, retrying",
                    target, attempt, attempts);
            }

            // Unreachable: the loop returns on success or on the final attempt.
            return VisionaryQueryResult.Failure("Query loop completed without a result");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<VisionaryQueryResult> TryQueryOnceAsync(
        IPEndPoint target,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new UdpClient(AddressFamily.InterNetwork);
            var payload = Encoding.ASCII.GetBytes(command);

            _logger.LogDebug("UDP TX {Endpoint} {Command}", target, command);
            await socket.SendAsync(payload, target, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.QueryTimeout);

            // Ignore datagrams from anything other than the device we asked, so a stray
            // broadcast cannot be mistaken for this endpoint's answer.
            while (true)
            {
                var received = await socket.ReceiveAsync(timeout.Token);

                if (!received.RemoteEndPoint.Address.Equals(target.Address))
                {
                    _logger.LogDebug(
                        "Discarding datagram from {Actual} while awaiting {Expected}",
                        received.RemoteEndPoint.Address, target.Address);
                    continue;
                }

                var raw = Encoding.ASCII.GetString(received.Buffer);
                _logger.LogDebug("UDP RX {Target} {Raw}", target, raw);

                return VisionaryQueryResult.Ok(VisionaryResponse.Parse(raw));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired rather than the caller cancelling.
            return VisionaryQueryResult.Timeout(target.ToString(), _options.QueryTimeout);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Socket error talking to {Target}", target);
            return VisionaryQueryResult.Failure($"Socket error contacting {target}: {ex.SocketErrorCode}");
        }
    }

    public void Dispose()
    {
        foreach (var gate in _deviceLocks.Values)
        {
            gate.Dispose();
        }

        _deviceLocks.Clear();
    }
}
