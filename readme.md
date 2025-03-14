![Icon](https://raw.githubusercontent.com/devlooped/WebSocketeer/main/assets/img/icon.png) WebSocketeer
============

A thin, intuitive, idiomatic and high-performance API for 
Azure Web PubSub [protobuf subprotocol](https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-protobuf-webpubsub-subprotocol).

[![Version](https://img.shields.io/nuget/v/WebSocketeer.svg?color=royalblue)](https://www.nuget.org/packages/WebSocketeer)
[![Downloads](https://img.shields.io/nuget/dt/WebSocketeer.svg?color=green)](https://www.nuget.org/packages/WebSocketeer)
[![License](https://img.shields.io/github/license/devlooped/WebSocketeer.svg?color=blue)](https://github.com/devlooped/WebSocketeer/blob/main/license.txt)
[![Build](https://img.shields.io/github/actions/workflow/status/devlooped/WebSocketeer/build.yml)](https://github.com/devlooped/WebSocketeer/actions)

<!-- include https://github.com/devlooped/.github/raw/main/sponsorlink.md -->
*This project uses [SponsorLink](https://github.com/devlooped#sponsorlink) 
and may issue IDE-only warnings if no active sponsorship is detected.*

<!-- https://github.com/devlooped/.github/raw/main/sponsorlink.md -->

# What

Azure Web PubSub [protobuf subprotocol](https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-protobuf-webpubsub-subprotocol) 
is super awesome and general purpose and I can see endless applications 
for this new service from Azure. The message-based nature of its "API" is 
not very intuitive or idiomatic for a dotnet developer though, I think. 

I wanted to create a super thin layer on top that didn't incur unnecessary 
allocations or buffer handling or extra threads, since that would detract 
from the amazing work on performance that .NET 6 brings to the table. 
I use the [best practices](https://docs.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-5.0#send-binary-payloads) 
for sending binary payloads using low-level (and quite new!) protobuf 
APIs for avoiding unnecessary buffer creation/handling.

In order to also squeeze every bit of performance, this project uses the 
protobuf subprotocol exclusively, even though there is support in the service 
for [JSON](https://docs.microsoft.com/en-us/azure/azure-web-pubsub/reference-json-webpubsub-subprotocol) 
payloads.

The actual binary payloads you send/receive can of course be decoded into 
any format you need, including JSON if you just encode/decode it as UTF8 bytes.

<!-- #content -->
# Usage

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
await using IWebSocketeer socketeer = WebSocketeer.ConnectAsync(serviceUri);
```

> NOTE: the `IWebSocketeer` interface implements both `IAsyncDisposable`, 
> which allows the `await using` pattern above, but also the regular 
> `IDisposable` interface. The former will perform a graceful `WebSocket` 
> disconnect/close. The latter will simply dispose the underlying `WebSocket`.


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
the socketeer "message loop" first. This would typically be done on a separate thread, 
using `Task.Run`, for example:

```csharp
var started = Task.Run(() => socketeer.RunAsync());
```

The returned task from `RunAsync` will remain in progress until the socketeer is disposed, 
or the underlying `WebSocket` is closed (either by the client or the server), or when an 
optional cancellation token passed to it is cancelled.

You can also send messages to a group you haven't joined (provided the roles 
specified when opening the connection allow it) via the `IWebSocketeer.SendAsync` 
method too:

```csharp
await socketeer.SendAsync("YourGroup", Encoding.UTF8.GetBytes("Hello World"));
```

## Advanced Scenarios

### Accessing Joined Group

Sometimes, it's useful to perform group join up-front, but at some 
later time you might also need to get the previously joined group 
from the same `IWebSocketeer` instance. 

```csharp
IWebSocketeer socketeer = /* connect, join some groups, etc. */;

// If group hasn't been joined previously, no incoming messages would arrive in this case.
IWebSocketeerGroup group = socketeer.Joined("incoming");
group.Subscribe(x => /* process incoming */);
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


### Split Request/Response Groups

You may want to simulate request/response communication patterns over the 
socketeer. In cases like this, you would typically do the following:

- Server joined to a client-specific group, such as `SERVER_ID-CLIENT_ID` 
  (with a `[TO]-[FROM]` format, so, TO=server, FROM=client)
- Server replying to requests in that group by sending responses to 
  `CLIENT_ID-SERVER_ID` (TO=client, FROM=server);
- Client joined to the responses group `CLIENT_ID-SERVER_ID` and sending 
  requests as needed to `SERVER_ID-CLIENT_ID`.

Note that the client *must not* join the `SERVER_ID-CLIENT_ID` group because 
otherwise it would *also* receive its own messages that are intended for the 
server only. Likewise, the server cannot join the `CLIENT_ID-SERVER_ID` group 
either. This is why this pattern might be more common than it would otherwise
seem.

Server-side:

```csharp
IWebSocketeer socketeer = ...;
var serverId = socketeer.UserId;

// Perhaps from an initial exchange over a shared channel
var clientId = ...;

await using IWebSocketeerGroup clientChannel = socketeer.Split(
    await socketeer.JoinAsync($"{serverId}-{clientId}"), 
    $"{clientId}-{serverId}");

clientChannel.Subscribe(async x => 
{
    // do some processing on incoming requests.
    ...
    // send a response via the outgoing group
    await clientChannel.SendAsync(response);
});
```

Client-side:

```csharp
IWebSocketeer socketeer = ...;
var clientId = socketeer.UserId;

// Perhaps a known identifier, or looked up somehow
var serverId = ...;

await using IWebSocketeerGroup serverChannel = socketeer.Split(
    await socketeer.JoinAsync($"{clientId}-{serverId}""),
    $"{serverId}-{clientId}");

serverChannel.Subscribe(async x => /* process responses */);
await serverChannel.SendAsync(request);
```

<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/clarius.png "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/MFB-Technologies-Inc.png "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![Torutek](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/torutek-gh.png "Torutek")](https://github.com/torutek-gh)
[![DRIVE.NET, Inc.](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/drivenet.png "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/Keflon.png "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/tbolon.png "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/kfrancis.png "Kori Francis")](https://github.com/kfrancis)
[![Toni Wenzel](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/twenzel.png "Toni Wenzel")](https://github.com/twenzel)
[![Uno Platform](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/unoplatform.png "Uno Platform")](https://github.com/unoplatform)
[![Dan Siegel](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/dansiegel.png "Dan Siegel")](https://github.com/dansiegel)
[![Reuben Swartz](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/rbnswartz.png "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/jfoshee.png "Jacob Foshee")](https://github.com/jfoshee)
[![](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/Mrxx99.png "")](https://github.com/Mrxx99)
[![Eric Johnson](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/eajhnsn1.png "Eric Johnson")](https://github.com/eajhnsn1)
[![Ix Technologies B.V.](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/IxTechnologies.png "Ix Technologies B.V.")](https://github.com/IxTechnologies)
[![David JENNI](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/davidjenni.png "David JENNI")](https://github.com/davidjenni)
[![Jonathan ](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/Jonathan-Hickey.png "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Charley Wu](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/akunzai.png "Charley Wu")](https://github.com/akunzai)
[![Jakob Tikjøb Andersen](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/jakobt.png "Jakob Tikjøb Andersen")](https://github.com/jakobt)
[![Tino Hager](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/tinohager.png "Tino Hager")](https://github.com/tinohager)
[![Ken Bonny](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/KenBonny.png "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/SimonCropp.png "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/agileworks-eu.png "agileworks-eu")](https://github.com/agileworks-eu)
[![sorahex](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/sorahex.png "sorahex")](https://github.com/sorahex)
[![Zheyu Shen](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/arsdragonfly.png "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/vezel-dev.png "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/ChilliCream.png "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/4OTC.png "4OTC")](https://github.com/4OTC)
[![Vincent Limo](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/v-limo.png "Vincent Limo")](https://github.com/v-limo)
[![Jordan S. Jones](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/jordansjones.png "Jordan S. Jones")](https://github.com/jordansjones)
[![domischell](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/DominicSchell.png "domischell")](https://github.com/DominicSchell)
[![Joseph Kingry](https://raw.githubusercontent.com/devlooped/sponsors/main/.github/avatars/jkingry.png "Joseph Kingry")](https://github.com/jkingry)


<!-- sponsors.md -->

[![Sponsor this project](https://raw.githubusercontent.com/devlooped/sponsors/main/sponsor.png "Sponsor this project")](https://github.com/sponsors/devlooped)
&nbsp;

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
