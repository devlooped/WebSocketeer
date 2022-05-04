using System.Reactive.Disposables;
using System.Reactive.Linq;

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
        => new JoinedGroup(group, socketeer);

    /// <summary>
    /// Gets a virtual group that receives messages from one group but sends messages 
    /// to a different group (i.e. via distinct request/response groups).
    /// </summary>
    /// <param name="socketeer">The <see cref="IWebSocketeer"/> that contains the joined <paramref name="incomingJoinedGroup"/>.</param>
    /// <param name="incomingJoinedGroup">The group to receive incoming messages through. Must have been previously joined 
    /// via <see cref="IWebSocketeer.JoinAsync"/>.</param>
    /// <param name="outgoingGroup">Name of the group to use for sending messages via <see cref="IWebSocketeerGroup.SendAsync"/>.</param>
    /// <remarks>
    /// The <see cref="IWebSocketeer"/> must have previously joined the <paramref name="incomingJoinedGroup"/> group in order for 
    /// the resulting <see cref="IWebSocketeerGroup"/> to actually receive incoming messages 
    /// for that group.
    /// </remarks>
    public static IWebSocketeerGroup Split(this IWebSocketeer socketeer, string incomingJoinedGroup, string outgoingGroup)
        => new SplitGroup(socketeer, socketeer.Joined(incomingJoinedGroup), outgoingGroup);

    /// <summary>
    /// Gets a virtual group that receives messages from one group but sends messages 
    /// to a different group (i.e. via distinct request/response groups).
    /// </summary>
    /// <param name="socketeer">The <see cref="IWebSocketeer"/> for the underlying communications.</param>
    /// <param name="incomingGroup">The group to receive incoming messages through.</param>
    /// <param name="outgoingGroup">Name of the group to use for sending messages via <see cref="IWebSocketeerGroup.SendAsync"/>.</param>
    public static IWebSocketeerGroup Split(this IWebSocketeer socketeer, IWebSocketeerGroup incomingGroup, string outgoingGroup)
        => new SplitGroup(socketeer, incomingGroup, outgoingGroup);

    class SplitGroup : IWebSocketeerGroup
    {
        readonly IWebSocketeer socketeer;
        readonly string outgoing;
        readonly IWebSocketeerGroup incoming;

        public SplitGroup(IWebSocketeer socketeer, IWebSocketeerGroup incoming, string outgoing)
        {
            this.socketeer = socketeer;
            this.incoming = incoming;
            this.outgoing = outgoing;
        }

        public string Name => incoming.Name + "<->" + outgoing;

        public ValueTask DisposeAsync() => incoming.DisposeAsync();

        public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellation = default)
            => socketeer.SendAsync(outgoing, message, cancellation);

        public IDisposable Subscribe(IObserver<ReadOnlyMemory<byte>> observer)
            => incoming.Subscribe(observer);
    }

    class JoinedGroup : IWebSocketeerGroup
    {
        readonly CompositeDisposable disposables = new();
        readonly IWebSocketeer socketeer;
        readonly IObservable<ReadOnlyMemory<byte>> group;

        public JoinedGroup(string name, IWebSocketeer socketeer)
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

