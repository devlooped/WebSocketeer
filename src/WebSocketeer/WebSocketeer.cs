using System.Net.WebSockets;

namespace Devlooped.Net;

/// <summary>
/// Factory class for <see cref="IWebSocketeer"/> instances.
/// </summary>
public static class WebSocketeer
{
    /// <summary>
    /// Creates the <see cref="IWebSocketeer"/> over the given <paramref name="websocket"/>.
    /// </summary>
    /// <param name="websocket">The <see cref="WebSocket"/> to use for the underlying communications.</param>
    public static IWebSocketeer Create(WebSocket websocket) => new DefaultSocketeer(websocket);
}
