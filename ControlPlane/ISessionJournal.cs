namespace GeniaProxy.ControlPlane;

public interface ISessionJournal : IAsyncDisposable
{
    ValueTask AppendAsync(JournalEvent entry, CancellationToken cancellationToken = default);
}
