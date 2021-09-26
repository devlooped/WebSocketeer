# WebSocketeer

High-performance intuitive API for Azure Web PubSub protobuf subprotocol

# Why

Azure Web PubSub [protobuf subprotocol](https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-protobuf-webpubsub-subprotocol) 
is super awesome and general purpose and I can see endless applications 
for this new service from Azure. The message-based nature of its "API" is 
not very intuitive or idiomatic for a dotnet developer though, I think. 

I wanted to create a super thin layer on top that didn't incur unnecessary 
allocations or buffer handling or extra threads, since that would detract 
from the amazing work on performance that .NET 5 (and 6!) are bringing to 
the table. I use the [best practices](https://docs.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-5.0#send-binary-payloads) 
for sending binary payloads using low-level (and quite new!) protobuf 
APIs for avoiding unnecessary buffer creation/handling.

In order to also squeeze every bit of performance, this project uses the 
protobuf subprotocol exclusively, even though there is support in the service 
for [JSON](https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-json-webpubsub-subprotocol) 
payloads.

The actual binary payloads you send/receive can of course be decoded into 
any format you need, including JSON if you just encode/decode it as UTF8 bytes.


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

Next simply connect the `WebSocketeer`:

```csharp
await using IWebSocketeer socketeer = WebSockeer.ConnectAsync(serviceUri);
```

> Note: the `IWebSocketeer` interface implements both `IAsyncDisposable`, 
> which allows the `await using` pattern above, but also the regular 
> `IDisposable` interface. The former will perform a graceful `WebSocket` 
> disconnect/close.


At this point, the `socketeer` variable contains a properly connected 
Web PubSub client, and you can inspect its `ConnectionId` and `UserId`
properties, for example. 

Next step is perhaps to join some groups:

```csharp
IWebSocketeerGroup group = await socketeer.JoinAsync("MyGroup");
```

The `IWebSocketeerGroup` is an observable of `ReadOnlyMemory<byte>`, exposing 
the incoming messages to that group, and it also provides a 
`SendAsync(ReadOnlyMemory<byte> message)` method to post messages to the group.

To write all incoming messages for the group to the console, you could 
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

### Handling the WebSocket

You can alternatively handle the `WebSocket` yourself. Instead of passing the 
service `Uri` to `ConnectAsync`, you can create and connect a `WebSocket` manually 
and pass it to the `WebSocketeer.ConnectAsync(WebSocket, CancellationToken)` overload.

In this case, it's important to remember to add the `protobuf.webpubsub.azure.v1` 
required subprotocol:

```csharp
using Devlooped.Net;

var client = new ClientWebSocket();
client.Options.AddSubProtocol("protobuf.webpubsub.azure.v1");

await client.ConnectAsync(serverUri, CancellationToken.None);

await using var socketeer = WebSocketeer.ConnectAsync(client);
```

