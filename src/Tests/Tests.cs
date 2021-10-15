using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace Devlooped.Net;

public record Tests(ITestOutputHelper Output)
{
    [Fact]
    public async Task PingPong()
    {
        const string expectedMessage = "sending expected pong!";
        string? actualMessage = default;

        using var cancellation = new CancellationTokenSource();
        await using var pingerSocketeer = await WebSocketeer.ConnectAsync(await ConnectAsync().ConfigureAwait(false), "pinger").ConfigureAwait(false);

        Assert.NotNull(pingerSocketeer.ConnectionId);
        Assert.NotNull(pingerSocketeer.UserId);

        // Pongs come through the 'pong' group. 
        var pongs = await pingerSocketeer.JoinAsync("pong").ConfigureAwait(false);
        var pongEv = new ManualResetEventSlim();

        pongs.Subscribe(x =>
        {
            actualMessage = Encoding.UTF8.GetString(x.Span);
            cancellation.Cancel();
            Output.WriteLine("pinger done");
            pongEv.Set();
        });

        await using var pongerSocketeer = await WebSocketeer.ConnectAsync(await ConnectAsync().ConfigureAwait(false), "ponger").ConfigureAwait(false);

        var pingerTask = Task.Run(() => pingerSocketeer.RunAsync(cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);
        var pongerTask = Task.Run(() => pongerSocketeer.RunAsync(cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);

        // Pings come through the 'ping' group.
        var pings = await pongerSocketeer.JoinAsync("ping").ConfigureAwait(false);
        var pingEv = new ManualResetEventSlim();

        // Send pongs to the 'pong' responses group.
        pings.Subscribe(async x =>
        {
            await pongerSocketeer.SendAsync("pong", Encoding.UTF8.GetBytes(expectedMessage)).ConfigureAwait(false);
            pingEv.Set();
            await pongerSocketeer.DisposeAsync().ConfigureAwait(false);
            Output.WriteLine("ponger done");
        });

        await pingerSocketeer.SendAsync("ping", Encoding.UTF8.GetBytes("ping")).ConfigureAwait(false);

        Assert.True(pingEv.Wait(Debugger.IsAttached ? int.MaxValue : 2000), "Expected to receive ping message before timeout.");
        Assert.True(pongEv.Wait(Debugger.IsAttached ? int.MaxValue : 2000), "Expected to receive pong message before timeout.");

        Assert.Equal(expectedMessage, actualMessage);
    }

    [Fact]
    public async Task DisposingSocketeerCompletesRunTask()
    {
        var socketeer = await WebSocketeer.ConnectAsync(await ConnectAsync());
        var runTask = Task.Run(async () => await socketeer.RunAsync());

        socketeer.Dispose();

        var timeoutTask = Task.Delay(500);
        if (await Task.WhenAny(runTask, timeoutTask) == timeoutTask)
            Assert.False(true, "Expected RunAsync to complete before timeout");
    }

    [Fact]
    public async Task WhenGroupJoined_ThenGetsMessagesToGroup()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.RunAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync(cancellation.Token));

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup));
        var ev = new ManualResetEventSlim();

        group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await server.SendAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await server.SendAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup), Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);

        cancellation.Cancel();
    }

    [Fact]
    public async Task WhenGroupJoined_ThenCanGetSecondJoinedGroup()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.RunAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync(cancellation.Token));

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup));
        var ev = new ManualResetEventSlim();

        var subs = group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await server.SendAsync(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();

        // Stop listening from the JoinAsync group.
        subs.Dispose();

        client.Joined(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup))
            .Subscribe(x =>
            {
                messages.Add(Encoding.UTF8.GetString(x.Span));
                ev.Set();
            });

        await server.SendAsync(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup), Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(2000), "Expected second group client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);

        cancellation.Cancel();
    }

    [Fact]
    public async Task WhenGroupJoined_ThenGetsOwnMessagesToGroup()
    {
        var messages = new List<string>();

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync());

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup));
        var ev = new ManualResetEventSlim();

        group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await client.SendAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await client.SendAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup), Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);
    }

    [Fact]
    public async Task CanSubscribeToAllMessagesFromAnyGroup()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.RunAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync(cancellation.Token));

        var ev = new ManualResetEventSlim();

        // NOTE: the client still needs to join the groups in order for messages to be received
        await client.JoinAsync(nameof(CanSubscribeToAllMessagesFromAnyGroup));
        await client.JoinAsync(nameof(CanSubscribeToAllMessagesFromAnyGroup) + "2");

        client.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Value.Span));
            ev.Set();
        });

        await server.SendAsync(nameof(CanSubscribeToAllMessagesFromAnyGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await server.SendAsync(nameof(CanSubscribeToAllMessagesFromAnyGroup) + "2", Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);

        cancellation.Cancel();
    }

    [Fact]
    public async Task CanSimulateRequestResponseViaGroups()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.RunAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync(cancellation.Token));

        // Group ID format is To-From. 
        // So for the server, incoming = Server-Client, outgoing = Client-Server
        var serverChannel = server.Split(await server.JoinAsync(server.UserId + "-" + client.UserId), client.UserId + "-" + server.UserId);
        var clientChannel = client.Split(await client.JoinAsync(client.UserId + "-" + server.UserId), server.UserId + "-" + client.UserId);

        // Server listens on the incoming group and sends replies via the outgoing one.
        serverChannel.ObserveOn(TaskPoolScheduler.Default).Subscribe(async x => await serverChannel.SendAsync(x));

        var ev = new ManualResetEventSlim();

        clientChannel.ObserveOn(TaskPoolScheduler.Default).Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await clientChannel.SendAsync(Encoding.UTF8.GetBytes("ping"));

        Assert.True(ev.Wait(2000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("ping", messages[0]);
        cancellation.Cancel();
    }

    static readonly Config Configuration = Config.Build();

    static Uri GetServiceUri(params string[] roles)
    {
        if (roles.Length == 0)
        {
            roles = new[]
            {
                "webpubsub.joinLeaveGroup",
                "webpubsub.sendToGroup"
            };
        }

        var serviceClient = new WebPubSubServiceClient(Configuration.GetString("Azure", "WebPubSub") ??
            Environment.GetEnvironmentVariable("AZURE_WEBPUBSUB"), "test");

        return serviceClient.GenerateClientAccessUri(
            userId: Guid.NewGuid().ToString("N"),
            roles: roles);
    }

    static async Task<WebSocket> ConnectAsync()
    {
        var client = new ClientWebSocket();
        client.Options.AddSubProtocol("protobuf.webpubsub.azure.v1");
        await client.ConnectAsync(GetServiceUri(), default);
        return client;
    }
}
