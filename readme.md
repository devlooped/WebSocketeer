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
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![Ryan McCaffery](https://avatars.githubusercontent.com/u/16667079?u=c0daa64bb5c1b572130e05ae2b6f609ecc912d4d&v=4&s=39 "Ryan McCaffery")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![eska-gmbh](https://avatars.githubusercontent.com/devlooped-team?s=39 "eska-gmbh")](https://github.com/eska-gmbh)
[![Geodata AS](https://avatars.githubusercontent.com/u/5946299?v=4&s=39 "Geodata AS")](https://github.com/geodata-no)
[![Jiri Slachta](https://avatars.githubusercontent.com/u/6891947?u=802cfeb13b070d04c53269fc662b0d58963480dd&v=4&s=39 "Jiri Slachta")](https://github.com/jslachta)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
