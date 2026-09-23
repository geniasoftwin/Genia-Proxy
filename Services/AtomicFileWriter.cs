using System.Text;

namespace GeniaProxy.Services
{
    public static class AtomicFileWriter
    {
        public static void WriteAllText(
            string path,
            string contents,
            Encoding encoding)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(contents);
            ArgumentNullException.ThrowIfNull(encoding);

            string temporaryPath =
                CreateTemporaryPath(path);

            try
            {
                EnsureDirectoryExists(path);

                File.WriteAllText(
                    temporaryPath,
                    contents,
                    encoding
                );

                File.Move(
                    temporaryPath,
                    path,
                    overwrite: true
                );
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        public static async Task WriteAllTextAsync(
            string path,
            string contents,
            Encoding encoding,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(contents);
            ArgumentNullException.ThrowIfNull(encoding);

            string temporaryPath =
                CreateTemporaryPath(path);

            try
            {
                EnsureDirectoryExists(path);

                await File.WriteAllTextAsync(
                    temporaryPath,
                    contents,
                    encoding,
                    cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();

                File.Move(
                    temporaryPath,
                    path,
                    overwrite: true
                );
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static string CreateTemporaryPath(
            string path)
        {
            string fullPath = Path.GetFullPath(path);

            string directory =
                Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(
                    "Не удалось определить папку файла."
                );

            string fileName =
                Path.GetFileName(fullPath);

            return Path.Combine(
                directory,
                $".{fileName}.{Guid.NewGuid():N}.tmp"
            );
        }

        private static void EnsureDirectoryExists(
            string path)
        {
            string? directory =
                Path.GetDirectoryName(
                    Path.GetFullPath(path)
                );

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Основная операция уже завершилась или выбросила
                // более полезное исключение.
            }
        }
    }
}
