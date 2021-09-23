using System.Net.WebSockets;
using System.Text;
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
        TaskScheduler.UnobservedTaskException += (s, e) => Assert.False(true, e.ToString());

        string? message = default;

        var pingerSocketeer = WebSocketeer.Create(await ConnectAsync());
        var pingerTask = Task.Run(() => pingerSocketeer.RunAsync());

        // Pongs come through the 'pong' group. 
        var pongs = await pingerSocketeer.JoinAsync("pong").ConfigureAwait(false);
        pongs.Subscribe(async x =>
        {
            message = Encoding.UTF8.GetString(x.Span);
            await pingerSocketeer.DisposeAsync();
            Output.WriteLine("pinger done");
        });

        var pongerSocketeer = WebSocketeer.Create(await ConnectAsync());
        var pongerTask = Task.Run(() => pongerSocketeer.RunAsync());

        // Pings come through the 'ping' group.
        var pings = await pongerSocketeer.JoinAsync("ping").ConfigureAwait(false);
        // Send pongs to the 'pong' responses group.
        pings.Subscribe(async x =>
        {
            await pongerSocketeer.SendAsync("pong", Encoding.UTF8.GetBytes("PONG!"));
            await pongerSocketeer.DisposeAsync();
            Output.WriteLine("ponger done");
        });

        await pingerSocketeer.SendAsync("ping", Encoding.UTF8.GetBytes("ping")).ConfigureAwait(false);

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
