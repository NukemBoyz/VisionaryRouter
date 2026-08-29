using System.Net;

namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Moves command strings to endpoints and brings responses back.
///
/// Kept as an interface so the routing and polling services can be exercised against a
/// fake transport without any sockets involved.
/// </summary>
public interface IVisionaryTransport
{
    /// <summary>Sends a command without waiting for a reply.</summary>
    Task SendAsync(IPEndPoint target, string command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command and waits for the endpoint's reply, retrying per options.</summary>
    Task<VisionaryQueryResult> QueryAsync(IPEndPoint target, string command, CancellationToken cancellationToken = default);
}
