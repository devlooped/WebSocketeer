# WebSocketeer

High-performance intuitive API for Azure Web PubSub protobuf subprotocol

## Usage

First acquire a proper client access URI for Azure Web PubSub using the 
official client API, such as:

```csharp
var serviceClient = new WebPubSubServiceClient([WEB_PUB_SUB_CONNECTION_STRING], [HUB_NAME]);
var serviceUri = serviceClient.GenerateClientAccessUri(
    userId: Guid.NewGuid().ToString("N"),
    roles:  new[]
    {
        "webpubsub.joinLeaveGroup",
        "webpubsub.sendToGroup"
    });
```

Next you can either create and connect a `WebSocket` to it yourself and 
pass it to `WebSocketeer.ConnectAsync(WebSocket, CancellationToken)`.

> NOTE: if you create the `WebSocket` and connect it yourself, please 
> make sure to add the `protobuf.webpubsub.azure.v1` subprotocol.

Alternatively, you can just pass in the `Uri` to the `ConnectAsync` overload:

```csharp
await using IWebSocketeer socketeer = WebSockeer.ConnectAsync(serviceUri);
```

> NOTE: the `IWebSocketeer` interface implements both `IAsyncDisposable`, 
> which allows the `await using` pattern above, but also the regular 
> `IDisposable` interface. The former will perform a graceful `WebSocket` 
> disconnect/close.


At this point, the `socketeer` variable contains a properly connected 
Web PubSub client, and you can inspect its `ConnectionId` and `UserId`, 
for example. 

Next step is perhaps to join some groups:

```csharp
IWebSocketeerGroup group = await socketeer.JoinAsync("MyGroup");
```

The `IWebSocketeerGroup` is an observable of `ReadOnlyMemory<byte>`, exposing 
the incoming messages to that group, and it also provides a 
`SendAsync(ReadOnlyMemory<byte> message)` method to post messages to the group.

To write all incoming messages to the group to the console, you could 
write:

```csharp
using var subscription = group.Subscribe(bytes => 
    Console.WriteLine(Encoding.UTF8.GetString(bytes.Span)));
```

In order to start processing incoming messages, though, you need to start 
the socketeer first. This would typically be done on a separate thread, using 
`Task.Run`, for example:

```csharp
var started = Task.Run(() => socketeer.StartAsync());
```

The `StartAsync` task will remain in progress until the socketeer is disposed, 
or the underlying `WebSocket` is closed (either by the client or the server).

You can also send messages to a group you haven't joined (provided the roles 
specified when opening the connection allow it) via the `IWebSocketeer.SendAsync` 
method too:

```csharp
await socketeer.SendAsync("YourGroup", Encoding.UTF8.GetBytes("Hello World"));
```