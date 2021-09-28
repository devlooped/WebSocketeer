namespace Devlooped.Net;

/// <summary>
/// Provides the <see cref="Joined"/> extension method that allows accessing 
/// a previously joined group to receive and send messages.
/// </summary>
public static class WebSocketeerExtensions
{
    /// <summary>
    /// Gets a reference to a previously joined group.
    /// </summary>
    /// <param name="socketeer">The <see cref="IWebSocketeer"/> that contains the joined group.</param>
    /// <param name="group">The group to retrieve.</param>
    /// <remarks>
    /// The <see cref="IWebSocketeer"/> must have previously joined the group in order for 
    /// the resulting <see cref="IWebSocketeerGroup"/> to actually receive incoming messages 
    /// for that group.
    /// </remarks>
    public static IWebSocketeerGroup Joined(this IWebSocketeer socketeer, string group)
        => new FilteredGroup(group, socketeer);

    class FilteredGroup : IWebSocketeerGroup
    {
        readonly CompositeDisposable disposables = new();
        readonly IWebSocketeer socketeer;
        readonly IObservable<ReadOnlyMemory<byte>> group;

        public FilteredGroup(string name, IWebSocketeer socketeer)
        {
            Name = name;
            this.socketeer = socketeer;
            group = socketeer.Where(x => x.Key == Name).Select(x => x.Value);
        }

        public string Name { get; }

        public ValueTask DisposeAsync()
        {
            disposables.Dispose();
            return new ValueTask();
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellation = default)
            => socketeer.SendAsync(Name, message, cancellation);

        public IDisposable Subscribe(IObserver<ReadOnlyMemory<byte>> observer)
        {
            var subscription = group.Subscribe(observer);
            disposables.Add(subscription);
            return subscription;
        }
    }
}

