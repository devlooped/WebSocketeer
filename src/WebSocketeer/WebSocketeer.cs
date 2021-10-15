namespace Devlooped.Net;

/// <summary>
/// Factory class for <see cref="IWebSocketeer"/> instances.
/// </summary>
public static class WebSocketeer
{
    /// <summary>
    /// Creates the <see cref="IWebSocketeer"/> over a newly created and connected 
    /// <see cref="WebSocket"/> to the Azure Web PubSub service using the given 
    /// <paramref name="serviceUri"/> using the protobuf subprotocol.
    /// </summary>
    /// <param name="serviceUri">.The Azure Web PubSub service endpoint to connect to.</param>
    /// <param name="cancellation">Optional token to cancel the connection attempt.</param>
    /// <remarks>
    /// The client is considered fully connected when it has received the <c>Connected</c> system 
    /// message from the Azure Web PubSub service using the protobuf WebSocket subprotocol.
    /// </remarks>
    public static async Task<IWebSocketeer> ConnectAsync(Uri serviceUri, CancellationToken cancellation = default)
    {
        var websocket = new ClientWebSocket();
        websocket.Options.AddSubProtocol("protobuf.webpubsub.azure.v1");

        await websocket.ConnectAsync(serviceUri, default);

        return await ConnectAsync(websocket, cancellation);
    }

    //var client = new ClientWebSocket();
    //client.Options.AddSubProtocol("protobuf.webpubsub.azure.v1");
    //    await client.ConnectAsync(GetServiceUri(), default);
    //    return client;

    /// <summary>
    /// Creates the <see cref="IWebSocketeer"/> over the given <paramref name="websocket"/> and 
    /// waits for it to become fully connected.
    /// </summary>
    /// <param name="websocket">The <see cref="WebSocket"/> to use for the underlying communications.</param>
    /// <param name="cancellation">Optional token to cancel the connection attempt.</param>
    /// <remarks>
    /// The client is considered fully connected when it has received the <c>Connected</c> system 
    /// message from the Azure Web PubSub service using the protobuf WebSocket subprotocol.
    /// </remarks>
    public static Task<IWebSocketeer> ConnectAsync(WebSocket websocket, CancellationToken cancellation = default)
        => new DefaultSocketeer(websocket).ConnectAsync(cancellation);

    /// <summary>
    /// Creates the <see cref="IWebSocketeer"/> over the given <paramref name="websocket"/> and 
    /// waits for it to become fully connected.
    /// </summary>
    /// <param name="websocket">The <see cref="WebSocket"/> to use for the underlying communications.</param>
    /// <param name="displayName">Friendly name to identify this channel while debugging or troubleshooting.</param>
    /// <param name="cancellation">Optional token to cancel the connection attempt.</param>
    /// <remarks>
    /// The client is considered fully connected when it has received the <c>Connected</c> system 
    /// message from the Azure Web PubSub service using the protobuf WebSocket subprotocol.
    /// </remarks>
    public static Task<IWebSocketeer> ConnectAsync(WebSocket websocket, string displayName, CancellationToken cancellation = default)
        => new DefaultSocketeer(websocket, displayName).ConnectAsync(cancellation);
}
