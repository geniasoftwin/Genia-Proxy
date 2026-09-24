using System.Text;
using System.Text.Json;

namespace GeniaProxy.Services
{
    public sealed class StartupJournal
    {
        private static readonly UTF8Encoding Utf8NoBom = new(
            encoderShouldEmitUTF8Identifier: false
        );

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

        private readonly object sync = new();

        public string Path { get; }

        public StartupJournal(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Не указан путь startup journal.",
                    nameof(path)
                );
            }

            Path = System.IO.Path.GetFullPath(path);
        }

        public static StartupJournal CreateDefault()
        {
            return new StartupJournal(
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "data",
                    "diagnostics",
                    "startup-journal.jsonl"
                )
            );
        }

        public bool TryAppend(
            string eventName,
            Guid attemptId,
            IReadOnlyDictionary<string, object?>? details = null)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return false;
            }

            try
            {
                var entry = new Dictionary<string, object?>
                {
                    ["timestampUtc"] = DateTimeOffset.UtcNow,
                    ["event"] = eventName,
                    ["attemptId"] = attemptId,
                    ["processId"] = Environment.ProcessId,
                    ["isAdministrator"] = SafeIsAdministrator(),
                    ["details"] = details ??
                        new Dictionary<string, object?>()
                };

                string line = JsonSerializer.Serialize(
                    entry,
                    JsonOptions
                );

                lock (sync)
                {
                    string? directory = System.IO.Path.GetDirectoryName(Path);

                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using var stream = new FileStream(
                        Path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete
                    );

                    using var writer = new StreamWriter(
                        stream,
                        Utf8NoBom,
                        bufferSize: 1024,
                        leaveOpen: false
                    );

                    writer.WriteLine(line);
                    writer.Flush();
                }

                return true;
            }
            catch
            {
                // Startup diagnostics are observability only. They must never
                // prevent GeniaProxy from starting or recovering the network.
                return false;
            }
        }

        private static bool SafeIsAdministrator()
        {
            try
            {
                return WindowsElevationService.IsAdministrator;
            }
            catch
            {
                return false;
            }
        }
    }
}
