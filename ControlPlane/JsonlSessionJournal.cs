using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeniaProxy.ControlPlane;

public sealed class JsonlSessionJournal : ISessionJournal
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;
    private bool disposed;

    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public JsonlSessionJournal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
    }

    public async ValueTask AppendAsync(JournalEvent entry, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string json = JsonSerializer.Serialize(entry, jsonOptions);

            // Diagnostics must remain readable/copyable while a session is active.
            // Keep no persistent file handle: append one event, flush, release immediately.
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
                leaveOpen: false);

            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
