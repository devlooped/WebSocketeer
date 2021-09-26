namespace Devlooped.Net;

/// <summary>
/// Provides access to the underlying <see cref="WebSocket"/> status.
/// </summary>
public class WebSocketStatus
{
    readonly WebSocket webSocket;

    internal WebSocketStatus(WebSocket webSocket) => this.webSocket = webSocket;

    /// <summary>
    /// Indicates the reason for the close handshake.
    /// </summary>
    public WebSocketCloseStatus? CloseStatus => webSocket.CloseStatus;

    /// <summary>
    /// Allows describing the reason why the connection was closed.
    /// </summary>
    public string? CloseStatusDescription => webSocket.CloseStatusDescription;

    /// <summary>
    /// Returns the current state of the underlying <see cref="WebSocket"/> connection.
    /// </summary>
    public WebSocketState State => webSocket.State;

}
