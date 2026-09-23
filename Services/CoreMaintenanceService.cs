using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public sealed class CoreMaintenanceService
    {
        private static readonly TimeSpan CommandTimeout =
            TimeSpan.FromSeconds(8);

        private const long MaxCoreSizeBytes =
            256L * 1024 * 1024;

        public string ExecutablePath { get; }
        public string ChecksumPath { get; }
        public string BackupPath { get; }

        public ProxyCoreKind CoreKind { get; }

        public string DisplayName =>
            ProfileFormatService.FormatCore(CoreKind);

        public bool HasBackup => File.Exists(BackupPath);

        public CoreMaintenanceService(
            ProxyCoreKind coreKind = ProxyCoreKind.SingBox)
        {
            CoreKind = coreKind;
            string engineDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "engine"
            );

            string baseName = coreKind == ProxyCoreKind.Xray
                ? "xray"
                : "sing-box";

            ExecutablePath = Path.Combine(
                engineDirectory,
                baseName + ".exe"
            );

            ChecksumPath = Path.Combine(
                engineDirectory,
                baseName + ".sha256"
            );

            BackupPath = Path.Combine(
                engineDirectory,
                baseName + ".backup.exe"
            );
        }

        public async Task<CoreInformation>
            GetInformationAsync(
                CancellationToken cancellationToken = default)
        {
            if (!File.Exists(ExecutablePath))
            {
                return new CoreInformation(
                    Exists: false,
                    Version: "Файл ядра не найден",
                    Sha256: string.Empty,
                    ExpectedSha256: ReadExpectedHash(),
                    HashMatches: false,
                    BackupAvailable: HasBackup,
                    SizeBytes: 0,
                    LastWriteTime: null
                );
            }

            string hash = await ComputeSha256Async(
                ExecutablePath,
                cancellationToken
            );

            string expectedHash = ReadExpectedHash();

            bool hashMatches =
                !string.IsNullOrWhiteSpace(expectedHash) &&
                string.Equals(
                    hash,
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase
                );

            string version;

            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                // Без доверенной контрольной суммы не запускаем
                // исполняемый файл даже для чтения версии.
                version =
                    "Не проверялась: контрольная сумма отсутствует";
            }
            else if (!hashMatches)
            {
                // Не запускаем файл с несовпадающей контрольной
                // суммой даже для определения версии.
                version =
                    "Не проверялась: контрольная сумма не совпадает";
            }
            else
            {
                try
                {
                    version = await GetVersionAsync(
                        ExecutablePath,
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    version = "Не удалось определить: " +
                        ex.Message;
                }
            }

            var file = new FileInfo(ExecutablePath);

            return new CoreInformation(
                Exists: true,
                Version: version,
                Sha256: hash,
                ExpectedSha256: expectedHash,
                HashMatches: hashMatches,
                BackupAvailable: HasBackup,
                SizeBytes: file.Length,
                LastWriteTime: file.LastWriteTime
            );
        }

        public async Task<CoreInformation> UpdateAsync(
            string candidatePath,
            CancellationToken cancellationToken = default)
        {
            EnsureMaintenanceRunsUnelevated("обновление");

            ArgumentException.ThrowIfNullOrWhiteSpace(
                candidatePath
            );

            string candidateFullPath =
                Path.GetFullPath(candidatePath);

            if (!File.Exists(candidateFullPath))
            {
                throw new FileNotFoundException(
                    "Выбранный файл ядра не найден.",
                    candidateFullPath
                );
            }

            long candidateSize =
                new FileInfo(candidateFullPath).Length;

            if (candidateSize is <= 0 or > MaxCoreSizeBytes)
            {
                throw new InvalidDataException(
                    "Размер выбранного ядра недопустим " +
                    "(максимум 256 МБ)."
                );
            }

            if (string.Equals(
                    candidateFullPath,
                    Path.GetFullPath(ExecutablePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Выберите новый {Path.GetFileName(ExecutablePath)}, " +
                    "расположенный в другой папке."
                );
            }

            string directory =
                Path.GetDirectoryName(ExecutablePath)
                ?? throw new InvalidOperationException(
                    "Не найдена папка ядра."
                );

            Directory.CreateDirectory(directory);

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileNameWithoutExtension(ExecutablePath)}.{Guid.NewGuid():N}.tmp"
            );

            bool backupCreated = false;

            try
            {
                // Проверяем именно ту копию, которая будет установлена.
                // Это закрывает окно подмены исходного файла между
                // проверкой версии и копированием.
                File.Copy(
                    candidateFullPath,
                    temporaryPath,
                    overwrite: true
                );

                long copiedSize =
                    new FileInfo(temporaryPath).Length;

                if (copiedSize is <= 0 or > MaxCoreSizeBytes)
                {
                    throw new InvalidDataException(
                        "Размер скопированного ядра недопустим " +
                        "(максимум 256 МБ)."
                    );
                }

                _ = await GetVersionAsync(
                    temporaryPath,
                    cancellationToken
                );

                string newHash =
                    await ComputeSha256Async(
                        temporaryPath,
                        cancellationToken
                    );

                if (File.Exists(ExecutablePath))
                {
                    File.Copy(
                        ExecutablePath,
                        BackupPath,
                        overwrite: true
                    );

                    backupCreated = true;
                }

                File.Move(
                    temporaryPath,
                    ExecutablePath,
                    overwrite: true
                );

                WriteChecksum(newHash);
            }
            catch
            {
                if (backupCreated &&
                    File.Exists(BackupPath))
                {
                    File.Copy(
                        BackupPath,
                        ExecutablePath,
                        overwrite: true
                    );
                }

                throw;
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            return await GetInformationAsync(
                cancellationToken
            );
        }

        public async Task<CoreInformation>
            RollbackAsync(
                CancellationToken cancellationToken = default)
        {
            EnsureMaintenanceRunsUnelevated("откат");

            if (!File.Exists(BackupPath))
            {
                throw new FileNotFoundException(
                    "Резервная копия ядра отсутствует.",
                    BackupPath
                );
            }

            string directory =
                Path.GetDirectoryName(ExecutablePath)
                ?? throw new InvalidOperationException(
                    "Не найдена папка ядра."
                );

            string replacementPath = Path.Combine(
                directory,
                $".{Path.GetFileNameWithoutExtension(ExecutablePath)}.rollback.{Guid.NewGuid():N}.tmp"
            );

            string previousPath = Path.Combine(
                directory,
                $".{Path.GetFileNameWithoutExtension(ExecutablePath)}.previous.{Guid.NewGuid():N}.tmp"
            );

            string? previousChecksum = File.Exists(ChecksumPath)
                ? File.ReadAllText(ChecksumPath, Encoding.UTF8)
                : null;

            bool executableExisted = File.Exists(ExecutablePath);

            try
            {
                // Все отменяемые и потенциально долгие операции
                // выполняются до изменения рабочего ядра.
                File.Copy(
                    BackupPath,
                    replacementPath,
                    overwrite: true
                );

                _ = await GetVersionAsync(
                    replacementPath,
                    cancellationToken
                );

                string hash = await ComputeSha256Async(
                    replacementPath,
                    cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(ExecutablePath))
                {
                    File.Copy(
                        ExecutablePath,
                        previousPath,
                        overwrite: true
                    );
                }

                // Оставляем проверенную копию до завершения всей
                // транзакции: она нужна для восстановления backup
                // при ошибке на одном из следующих шагов.
                File.Copy(
                    replacementPath,
                    ExecutablePath,
                    overwrite: true
                );

                WriteChecksum(hash);

                if (File.Exists(previousPath))
                {
                    File.Move(
                        previousPath,
                        BackupPath,
                        overwrite: true
                    );
                }
            }
            catch
            {
                TryRestoreRollbackState(
                    previousPath,
                    replacementPath,
                    previousChecksum,
                    executableExisted
                );

                throw;
            }
            finally
            {
                TryDelete(replacementPath);
                TryDelete(previousPath);
            }

            return await GetInformationAsync(
                cancellationToken
            );
        }

        private static void EnsureMaintenanceRunsUnelevated(
            string operationName)
        {
            if (!WindowsElevationService.IsAdministrator)
            {
                return;
            }

            throw new UnauthorizedAccessException(
                $"Безопасность: операция «{operationName} ядра» " +
                "заблокирована при запуске GeniaProxy от администратора. " +
                "Закройте " +
                "elevated-копию, запустите GeniaProxy в режиме USER и " +
                "повторите операцию."
            );
        }

        private async Task<string> GetVersionAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory =
                    Path.GetDirectoryName(executablePath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("version");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Не удалось запустить проверку версии ядра."
                );
            }

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync(
                    CancellationToken.None
                );

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync(
                    CancellationToken.None
                );

            using var timeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken
                    );

            timeout.CancelAfter(CommandTimeout);

            try
            {
                await process.WaitForExitAsync(
                    timeout.Token
                );
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                cancellationToken
                    .ThrowIfCancellationRequested();

                throw new TimeoutException(
                    "Проверка версии ядра превысила " +
                    "допустимое время."
                );
            }

            string output = TerminalOutputSanitizer
                .Sanitize(await outputTask)
                .Trim();

            string error = TerminalOutputSanitizer
                .Sanitize(await errorTask)
                .Trim();

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"Файл не прошёл проверку {DisplayName}: " +
                    (string.IsNullOrWhiteSpace(error)
                        ? $"код {process.ExitCode.ToString(
                            CultureInfo.InvariantCulture)}"
                        : error)
                );
            }

            string combined = string.IsNullOrWhiteSpace(output)
                ? error
                : output;

            string? versionLine = combined
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
                .FirstOrDefault(line => CoreKind == ProxyCoreKind.Xray
                    ? line.StartsWith(
                        "Xray ",
                        StringComparison.OrdinalIgnoreCase)
                    : line.Contains(
                        "sing-box version",
                        StringComparison.OrdinalIgnoreCase));

            if (versionLine is null)
            {
                throw new InvalidDataException(
                    "Файл не идентифицирует себя как " +
                    $"ядро {DisplayName}."
                );
            }

            return versionLine;
        }

        private static async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan
            );

            using SHA256 sha256 = SHA256.Create();

            byte[] hash = await sha256.ComputeHashAsync(
                stream,
                cancellationToken
            );

            return Convert.ToHexString(hash);
        }

        private string ReadExpectedHash()
        {
            if (!File.Exists(ChecksumPath))
            {
                return string.Empty;
            }

            string text = File.ReadAllText(
                ChecksumPath,
                Encoding.UTF8
            );

            string candidate = text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries
            ).FirstOrDefault() ?? string.Empty;

            if (candidate.Length != 64 ||
                candidate.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                return string.Empty;
            }

            return candidate.ToUpperInvariant();
        }

        private void WriteChecksum(string hash)
        {
            AtomicFileWriter.WriteAllText(
                ChecksumPath,
                hash + "  " + Path.GetFileName(ExecutablePath) +
                Environment.NewLine,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                )
            );
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(
                        entireProcessTree: true
                    );
                }
            }
            catch
            {
                // Сохраняем исходную ошибку проверки.
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
                // Временный файл будет очищен при следующем
                // обслуживании папки приложения.
            }
        }

        private void TryRestoreRollbackState(
            string previousPath,
            string replacementPath,
            string? previousChecksum,
            bool executableExisted)
        {
            try
            {
                if (File.Exists(previousPath))
                {
                    File.Copy(
                        previousPath,
                        ExecutablePath,
                        overwrite: true
                    );
                }
                else if (executableExisted &&
                         File.Exists(BackupPath))
                {
                    File.Copy(
                        BackupPath,
                        ExecutablePath,
                        overwrite: true
                    );
                }
                else
                {
                    TryDelete(ExecutablePath);
                }
            }
            catch
            {
                // Исходное исключение транзакции полезнее.
            }

            try
            {
                if (File.Exists(replacementPath))
                {
                    File.Copy(
                        replacementPath,
                        BackupPath,
                        overwrite: true
                    );
                }
            }
            catch
            {
                // Исходное исключение транзакции полезнее.
            }

            try
            {
                if (previousChecksum is null)
                {
                    TryDelete(ChecksumPath);
                }
                else
                {
                    AtomicFileWriter.WriteAllText(
                        ChecksumPath,
                        previousChecksum,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false
                        )
                    );
                }
            }
            catch
            {
                // Исходное исключение транзакции полезнее.
            }
        }
    }

    public sealed record CoreInformation(
        bool Exists,
        string Version,
        string Sha256,
        string ExpectedSha256,
        bool HashMatches,
        bool BackupAvailable,
        long SizeBytes,
        DateTime? LastWriteTime
    );
}
