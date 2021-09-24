using System;
using System.Threading.Tasks;

namespace Devlooped.Net;

/// <summary>
/// Represents a joined group in an <see cref="IWebSocketeer"/> connection, 
/// which can be used to receive and send messages to that group.
/// </summary>
/// <remarks>
/// The group itself is an observable for easy consumption. When using the 
/// <see cref="IAsyncDisposable.DisposeAsync"/>, the client leaves the group 
/// cleanly too.
/// </remarks>
/// <example>
/// await using var group = socketeer.JoinAsync("inbox");
/// group.Subscribe(message => /* process message */);
/// await group.SendAsync(Encoding.UTF8.GetBytes("Hello world"));
/// </example>
public interface IWebSocketeerGroup : IObservable<ReadOnlyMemory<byte>>, IAsyncDisposable
{
    /// <summary>
    /// The name of the group.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Sends a message to the group.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> message);
}