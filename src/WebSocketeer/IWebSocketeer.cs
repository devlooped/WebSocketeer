namespace Devlooped.Net;

/// <summary>
/// Represents a connected client to the Azure Web PubSub service using the 
/// protobuf WebSocket subprotocol.
/// </summary>
/// <remarks>
/// The <see cref="WebSocketeer"/> factory class can be used to acquire instances of 
/// this interface, to interact with the service through an underlying <see cref="WebSocket"/>.
/// The documentation for the subprotocol can be found at https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-protobuf-webpubsub-subprotocol.
/// <para>
/// When the <see cref="IAsyncDisposable.DisposeAsync"/> is invoked (explicitly or 
/// implicitly via an <c>await using</c>), the underlying <see cref="WebSocket"/> will 
/// be gracefully closed before being disposed. <see cref="IDisposable.Dispose"/>, 
/// on the other hand, will just dispose the <see cref="WebSocket"/>.
/// </para>
/// <para>
/// Incoming messages can be accessed by either joining a group via <see cref="JoinAsync"/> 
/// and subscribing to it or directly by subscribing to the entire <see cref="IWebSocketeer"/>.
/// </para>
/// </remarks>
public interface IWebSocketeer : IObservable<KeyValuePair<string, ReadOnlyMemory<byte>>>, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The connection identifier for this connected client.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// The user identifier for this connected client.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Gets the underlying <see cref="WebSocket"/> status.
    /// </summary>
    public WebSocketStatus SocketStatus { get; }

    /// <summary>
    /// Joins a group and returns an <see cref="IWebSocketeerGroup"/> to 
    /// send and receive messages within the group.
    /// </summary>
    /// <param name="group">The name of the group.</param>
    /// <param name="cancellation">Optional cancellation token to cancel the process.</param>
    ValueTask<IWebSocketeerGroup> JoinAsync(string group, CancellationToken cancellation = default);

    /// <summary>
    /// Leaves the group.
    /// </summary>
    /// <param name="group">The name of the group.</param>
    /// <param name="cancellation">Optional cancellation token to cancel the process.</param>
    ValueTask LeaveAsync(string group, CancellationToken cancellation = default);

    /// <summary>
    /// Sends a message to the given <paramref name="group"/>.
    /// </summary>
    /// <param name="group">The name of the group.</param>
    /// <param name="message">The message payload.</param>
    /// <param name="cancellation">Optional cancellation token to cancel the process.</param>
    ValueTask SendAsync(string group, ReadOnlyMemory<byte> message, CancellationToken cancellation = default);

    /// <summary>
    /// Starts listening for incoming messages.
    /// </summary>
    /// <param name="cancellation">Optional cancellation token to cancel the process.</param>
    /// <remarks>
    /// The returned task completes if the instance is disposed (either via <see cref="IDisposable"/> or 
    /// <see cref="IAsyncDisposable"/>) or the underlying <see cref="WebSocket"/> is closed (by the client or the 
    /// server).
    /// </remarks>
    Task RunAsync(CancellationToken cancellation = default);
}