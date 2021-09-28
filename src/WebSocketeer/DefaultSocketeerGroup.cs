namespace Devlooped.Net;

class DefaultSocketeerGroup : IWebSocketeerGroup
{
    readonly DefaultSocketeer socketeer;
    readonly IDisposable subscription;
    readonly Subject<ReadOnlyMemory<byte>> messages = new();

    public DefaultSocketeerGroup(DefaultSocketeer socketeer, string name)
    {
        this.socketeer = socketeer;
        Name = name;
        subscription = socketeer.Messages.Where(x => x.Key == name)
            .Subscribe(x => messages.OnNext(x.Value));
    }

    /// <summary>
    /// Name of the group this channel belongs to.
    /// </summary>
    public string Name { get; }

    public IDisposable Subscribe(IObserver<ReadOnlyMemory<byte>> observer)
        => messages.Subscribe(observer);

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellation = default)
        => socketeer.SendAsync(Name, message, cancellation);

    public async ValueTask DisposeAsync()
    {
        await socketeer.LeaveAsync(Name);
        subscription.Dispose();
        messages.Dispose();
    }
}
