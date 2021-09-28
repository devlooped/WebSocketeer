namespace Devlooped.Net;

public record Tests(ITestOutputHelper Output)
{
    [Fact]
    public async Task PingPong()
    {
        string? message = default;

        using var cancellation = new CancellationTokenSource();
        await using var pingerSocketeer = await WebSocketeer.ConnectAsync(await ConnectAsync());
        var pingerTask = Task.Run(() => pingerSocketeer.StartAsync(cancellation.Token));
        var pongEv = new ManualResetEventSlim();

        // Pongs come through the 'pong' group. 
        var pongs = await pingerSocketeer.JoinAsync("pong").ConfigureAwait(false);

        var setEv = new ManualResetEventSlim();
        _ = Task.Run(() =>
        {
            while (pingerSocketeer.ConnectionId == null || pingerSocketeer.UserId == null)
                Thread.Sleep(50);

            setEv.Set();
        }, cancellation.Token);

        Assert.True(setEv.Wait(500), "Should have set ConnectionId and UserId before timeout");

        pongs.Subscribe(async x =>
        {
            message = Encoding.UTF8.GetString(x.Span);
            await pingerSocketeer.DisposeAsync();
            Output.WriteLine("pinger done");
            pongEv.Set();
        });

        await using var pongerSocketeer = await WebSocketeer.ConnectAsync(await ConnectAsync());
        var pongerTask = Task.Run(() => pongerSocketeer.StartAsync(cancellation.Token));
        var pingEv = new ManualResetEventSlim();

        // Pings come through the 'ping' group.
        var pings = await pongerSocketeer.JoinAsync("ping").ConfigureAwait(false);

        setEv.Reset();
        _ = Task.Run(() =>
        {
            while (pongerSocketeer.ConnectionId == null || pongerSocketeer.UserId == null)
                Thread.Sleep(50);

            setEv.Set();
        }, cancellation.Token);

        Assert.True(setEv.Wait(500), "Should have set ConnectionId and UserId before timeout");

        // Send pongs to the 'pong' responses group.
        pings.Subscribe(async x =>
        {
            await pongerSocketeer.SendAsync("pong", Encoding.UTF8.GetBytes("PONG!"));
            await pongerSocketeer.DisposeAsync();
            Output.WriteLine("ponger done");
            pingEv.Set();
        });

        await pingerSocketeer.SendAsync("ping", Encoding.UTF8.GetBytes("ping")).ConfigureAwait(false);

        Assert.True(pingEv.Wait(1000), "Expected to receive ping message before timeout.");
        Assert.True(pongEv.Wait(1000), "Expected to receive pong message before timeout.");

        await pingerTask;
        await pongerTask;

        Assert.Equal("PONG!", message);
    }

    [Fact]
    public async Task DisposingSocketeerCompletesRunTask()
    {
        var socketeer = await WebSocketeer.ConnectAsync(await ConnectAsync());
        var runTask = Task.Run(async () => await socketeer.StartAsync());

        socketeer.Dispose();

        var timeoutTask = Task.Delay(100);
        if (await Task.WhenAny(runTask, timeoutTask) == timeoutTask)
            Assert.False(true, "Expected RunAsync to complete before timeout");
    }

    [Fact]
    public async Task WhenGroupJoined_ThenGetsMessagesToGroup()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.StartAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.StartAsync(cancellation.Token));

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup));
        var ev = new ManualResetEventSlim();

        group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await server.SendAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(1000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await server.SendAsync(nameof(WhenGroupJoined_ThenGetsMessagesToGroup), Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(1000), "Expected client to receive message before timeout.");
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
        _ = Task.Run(() => server.StartAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.StartAsync(cancellation.Token));

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup));
        var ev = new ManualResetEventSlim();

        var subs = group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await server.SendAsync(nameof(WhenGroupJoined_ThenCanGetSecondJoinedGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(1000), "Expected client to receive message before timeout.");
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

        Assert.True(ev.Wait(1000), "Expected second group client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);

        cancellation.Cancel();
    }


    [Fact]
    public async Task WhenGroupJoined_ThenGetsOwnMessagesToGroup()
    {
        var messages = new List<string>();

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.StartAsync());

        var group = await client.JoinAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup));
        var ev = new ManualResetEventSlim();

        group.Subscribe(x =>
        {
            messages.Add(Encoding.UTF8.GetString(x.Span));
            ev.Set();
        });

        await client.SendAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup), Encoding.UTF8.GetBytes("first"));

        Assert.True(ev.Wait(500), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await client.SendAsync(nameof(WhenGroupJoined_ThenGetsOwnMessagesToGroup), Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(500), "Expected client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);
    }

    [Fact]
    public async Task CanSubscribeToAllMessagesFromAnyGroup()
    {
        var messages = new List<string>();

        var cancellation = new CancellationTokenSource();
        await using var server = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => server.StartAsync(cancellation.Token));

        await using var client = await WebSocketeer.ConnectAsync(await ConnectAsync());
        _ = Task.Run(() => client.StartAsync(cancellation.Token));

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

        Assert.True(ev.Wait(1000), "Expected client to receive message before timeout.");
        Assert.Single(messages);
        Assert.Equal("first", messages[0]);

        ev.Reset();
        await server.SendAsync(nameof(CanSubscribeToAllMessagesFromAnyGroup) + "2", Encoding.UTF8.GetBytes("second"));

        Assert.True(ev.Wait(1000), "Expected client to receive message before timeout.");
        Assert.Equal(2, messages.Count);
        Assert.Equal("second", messages[1]);

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
