using System.Reactive.Subjects;

namespace Devlooped.Net;

class DefaultSocketeer : IWebSocketeer
{
    // Wait 250 ms before giving up on a Close, same as SignalR WebSocketHandler
    static readonly TimeSpan closeTimeout = TimeSpan.FromMilliseconds(250);

    readonly WebSocket webSocket;
    readonly WebSocketStatus status;
    readonly CancellationTokenSource disposeCancellation = new();
    readonly AsyncLock writerGate = new();
    readonly ArrayBufferWriter<byte> writeBuffer = new(512);
    Channel<ReadOnlyMemory<byte>> channel;

    readonly Subject<KeyValuePair<string, ReadOnlyMemory<byte>>> messages = new();

    public DefaultSocketeer(WebSocket webSocket, string? displayName = default)
    {
        if (webSocket.State == WebSocketState.Open && webSocket.SubProtocol != "protobuf.webpubsub.azure.v1")
            throw new InvalidOperationException("WebSocket.SubProtocol must be 'protobuf.webpubsub.azure.v1'.");

        this.webSocket = webSocket;
        DisplayName = displayName;
        channel = webSocket.CreateChannel(displayName);
        status = new WebSocketStatus(webSocket);
    }

    public string? DisplayName { get; private set; }

    public override string? ToString() => DisplayName ?? base.ToString();

    internal IObservable<KeyValuePair<string, ReadOnlyMemory<byte>>> Messages => messages;

    public string ConnectionId { get; private set; } = "";

    public string UserId { get; private set; } = "";

    public WebSocketStatus SocketStatus => status;

    public async Task<IWebSocketeer> ConnectAsync(CancellationToken cancellation = default)
    {
        if (webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("Expected WebSocket to be open.");

        //Ensure protocol at this stage too, in case the webSocket was connected after initial creation.
        if (webSocket.SubProtocol != "protobuf.webpubsub.azure.v1")
            throw new InvalidOperationException("Subprotocol protobuf.webpubsub.azure.v1 is required.");

        // Read until we receive the connected system mesage.
        await foreach (var item in channel.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
        {
            var message = DownstreamMessage.Parser.ParseFrom(item.Span);

            if (message.MessageCase == DownstreamMessage.MessageOneofCase.SystemMessage &&
                message.SystemMessage.ConnectedMessage != null)
            {
                ConnectionId = message.SystemMessage.ConnectedMessage.ConnectionId;
                UserId = message.SystemMessage.ConnectedMessage.UserId;
                if (DisplayName == null)
                {
                    DisplayName = UserId;
                    // Recreate channel with the new display name
                    channel = webSocket.CreateChannel(UserId);
                }

                return this;
            }
        }

        throw new OperationCanceledException();
    }

    public async ValueTask<IWebSocketeerGroup> JoinAsync(string group, CancellationToken cancellation = default)
    {
        using var cts = GetCancellation(cancellation, out var token);
        using var sync = await writerGate.LockAsync(token).ConfigureAwait(false);

        new UpstreamMessage
        {
            JoinGroupMessage = new UpstreamMessage.Types.JoinGroupMessage
            {
                Group = group
            },
        }.WriteTo(writeBuffer);

        await channel.Writer.WriteAsync(writeBuffer.WrittenMemory, token).ConfigureAwait(false);
        writeBuffer.Clear();

        return new DefaultSocketeerGroup(this, group);
    }

    public async ValueTask LeaveAsync(string group, CancellationToken cancellation = default)
    {
        using var cts = GetCancellation(cancellation, out var token);
        using var sync = await writerGate.LockAsync(token).ConfigureAwait(false);

        new UpstreamMessage
        {
            LeaveGroupMessage = new UpstreamMessage.Types.LeaveGroupMessage
            {
                Group = group
            },
        }.WriteTo(writeBuffer);

        await channel.Writer.WriteAsync(writeBuffer.WrittenMemory, token).ConfigureAwait(false);
        writeBuffer.Clear();
    }

    public Task RunAsync(CancellationToken cancellation = default)
    {
        //Ensure protocol at this stage too, in case the webSocket was connected after initial creation.
        if (webSocket.State == WebSocketState.Open && webSocket.SubProtocol != "protobuf.webpubsub.azure.v1")
            throw new InvalidOperationException("Subprotocol protobuf.webpubsub.azure.v1 is required.");

        if (cancellation == default)
            return ReadInputAsync(disposeCancellation.Token);

        var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellation, disposeCancellation.Token);
        return ReadInputAsync(combined.Token);
    }

    public async ValueTask SendAsync(string group, ReadOnlyMemory<byte> message, CancellationToken cancellation = default)
    {
        using var cts = GetCancellation(cancellation, out var token);
        using var sync = await writerGate.LockAsync(token).ConfigureAwait(false);

        new UpstreamMessage
        {
            SendToGroupMessage = new UpstreamMessage.Types.SendToGroupMessage
            {
                Group = group,
                Data = new MessageData
                {
                    BinaryData = UnsafeByteOperations.UnsafeWrap(message),
                }
            }
        }.WriteTo(writeBuffer);

        await channel.Writer.WriteAsync(writeBuffer.WrittenMemory, token).ConfigureAwait(false);
        writeBuffer.Clear();
    }

    public IDisposable Subscribe(IObserver<KeyValuePair<string, ReadOnlyMemory<byte>>> observer)
        => messages.Subscribe(observer);

    async Task ReadInputAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
            {
                var message = DownstreamMessage.Parser.ParseFrom(item.Span);

                if (message.MessageCase == DownstreamMessage.MessageOneofCase.DataMessage)
                    messages.OnNext(KeyValuePair.Create(message.DataMessage.Group ?? message.DataMessage.From, message.DataMessage.Data.BinaryData.Memory));
                else if (message.MessageCase == DownstreamMessage.MessageOneofCase.SystemMessage &&
                    message.SystemMessage.DisconnectedMessage != null)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Preserve the close status since it might be triggered by a received Close message containing the status and description.
            await CloseAsync(webSocket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, webSocket.CloseStatusDescription)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the websocket.
    /// </summary>
    public void Dispose()
    {
        if (!disposeCancellation.IsCancellationRequested)
            disposeCancellation.Cancel();

        webSocket.Dispose();
    }

    /// <summary>
    /// Gracefully closes the websocket and disposes it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!disposeCancellation.IsCancellationRequested)
            disposeCancellation.Cancel();

        await CloseAsync(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
        Dispose();
    }

    /// <summary>
    /// Combines the disposal cancellation token with the received token, if it's not <see cref="CancellationToken.None"/>. 
    /// This allows the disposal of the entire socketeer to abort anything that's currently ongoing, regardless of the 
    /// token passed to each individual method.
    /// </summary>
    IDisposable? GetCancellation(CancellationToken candidateToken, out CancellationToken effectiveToken)
    {
        var cts = candidateToken == CancellationToken.None ? default :
            CancellationTokenSource.CreateLinkedTokenSource(candidateToken, disposeCancellation.Token);

        effectiveToken = candidateToken == CancellationToken.None ? disposeCancellation.Token : cts?.Token ?? default;
        return cts;
    }

    async Task CloseAsync(WebSocketCloseStatus closeStatus, string? closeStatusDescription = default)
    {
        var state = webSocket.State;
        if (state == WebSocketState.Closed || state == WebSocketState.CloseSent || state == WebSocketState.Aborted)
            return;

        var closeTask = webSocket is ClientWebSocket ?
            // Disconnect from client vs server is different.
            webSocket.CloseAsync(closeStatus, closeStatusDescription, default) :
            webSocket.CloseOutputAsync(closeStatus, closeStatusDescription, default);

        // Don't wait indefinitely for the close to be acknowledged
        await Task.WhenAny(closeTask, Task.Delay(closeTimeout)).ConfigureAwait(false);
        channel.Writer.TryComplete();
    }
}
