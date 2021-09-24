using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.WebPubSub;
using DotNetConfig;
using Xunit;
using Xunit.Abstractions;

namespace Devlooped.Net;

public record Tests(ITestOutputHelper Output)
{
    [Fact]
    public async Task PingPong()
    {
        string? message = default;

        var cancellation = new CancellationTokenSource();
        await using var pingerSocketeer = WebSocketeer.Create(await ConnectAsync());
        var pingerTask = Task.Run(() => pingerSocketeer.RunAsync(cancellation.Token));
        var pongEv = new ManualResetEventSlim();

        // Pongs come through the 'pong' group. 
        var pongs = await pingerSocketeer.JoinAsync("pong").ConfigureAwait(false);
        pongs.Subscribe(async x =>
        {
            message = Encoding.UTF8.GetString(x.Span);
            await pingerSocketeer.DisposeAsync();
            Output.WriteLine("pinger done");
            pongEv.Set();
        });

        await using var pongerSocketeer = WebSocketeer.Create(await ConnectAsync());
        var pongerTask = Task.Run(() => pongerSocketeer.RunAsync(cancellation.Token));
        var pingEv = new ManualResetEventSlim();

        // Pings come through the 'ping' group.
        var pings = await pongerSocketeer.JoinAsync("ping").ConfigureAwait(false);
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
        var socketeer = WebSocketeer.Create(await ConnectAsync());
        var runTask = Task.Run(async () => await socketeer.RunAsync());

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
        await using var server = WebSocketeer.Create(await ConnectAsync());
        _ = Task.Run(() => server.RunAsync(cancellation.Token));

        await using var client = WebSocketeer.Create(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync(cancellation.Token));

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
    public async Task WhenGroupJoined_ThenGetsOwnMessagesToGroup()
    {
        var messages = new List<string>();

        await using var client = WebSocketeer.Create(await ConnectAsync());
        _ = Task.Run(() => client.RunAsync());

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
