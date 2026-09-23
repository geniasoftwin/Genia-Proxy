using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public sealed class CoreManager : IDisposable
    {
        private Process? coreProcess;
        private ProcessStopSignal? coreStopSignal;
        private bool disposed;

        public string ExecutablePath { get; private set; }

        public ProxyCoreKind CoreKind { get; private set; }

        public string DisplayName =>
            ProfileFormatService.FormatCore(CoreKind);

        public bool Exists => File.Exists(ExecutablePath);

        public bool IsRunning
        {
            get
            {
                Process? process = coreProcess;

                try
                {
                    return process is not null &&
                           !process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public event Action<string>? OutputReceived;
        public event Action<int>? ProcessExited;

        public CoreManager()
        {
            ExecutablePath = string.Empty;
            SelectCore(ProxyCoreKind.SingBox);
        }

        public void SelectCore(ProxyCoreKind coreKind)
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                throw new InvalidOperationException(
                    "Нельзя сменить ядро во время подключения."
                );
            }

            CoreKind = coreKind;
            ExecutablePath = Path.Combine(
                AppContext.BaseDirectory,
                "engine",
                coreKind == ProxyCoreKind.Xray
                    ? "xray.exe"
                    : "sing-box.exe"
            );
        }

        public bool TryGetRunningProcess(
            out Process? process)
        {
            process = coreProcess;

            try
            {
                if (process is null || process.HasExited)
                {
                    process = null;
                    return false;
                }

                return true;
            }
            catch
            {
                process = null;
                return false;
            }
        }

        public async Task<CoreCommandResult> CheckConfigAsync(
            string configPath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureFilesExist(configPath);
            VerifyExecutableIntegrity();

            ProcessStartInfo startInfo =
                CreateStartInfo();

            if (CoreKind == ProxyCoreKind.Xray)
            {
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("-test");
                startInfo.ArgumentList.Add("-config");
                startInfo.ArgumentList.Add(configPath);
            }
            else
            {
                startInfo.ArgumentList.Add("check");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(configPath);
            }

            using var process = new Process
            {
                StartInfo = startInfo
            };

            cancellationToken.ThrowIfCancellationRequested();

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Не удалось запустить проверку конфигурации."
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

            using var timeoutCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken
                    );

            timeoutCancellation.CancelAfter(
                TimeSpan.FromSeconds(15)
            );

            try
            {
                await process.WaitForExitAsync(
                    timeoutCancellation.Token
                );
            }
            catch (OperationCanceledException)
            {
                TryKill(process);

                await Task.WhenAny(
                    process.WaitForExitAsync(
                        CancellationToken.None
                    ),
                    Task.Delay(
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None
                    )
                );

                cancellationToken
                    .ThrowIfCancellationRequested();

                throw new TimeoutException(
                    $"Проверка конфигурации {DisplayName} " +
                    "не завершилась за 15 секунд."
                );
            }

            string output = await outputTask;
            string error = await errorTask;

            return new CoreCommandResult(
                process.ExitCode,
                TerminalOutputSanitizer.Sanitize(output),
                TerminalOutputSanitizer.Sanitize(error)
            );
        }

        public void Start(string configPath)
        {
            ThrowIfDisposed();
            EnsureFilesExist(configPath);
            VerifyExecutableIntegrity();

            if (IsRunning)
            {
                throw new InvalidOperationException(
                    $"Ядро {DisplayName} уже запущено."
                );
            }

            DisposeOldProcess();

            ProcessStartInfo startInfo =
                CreateStartInfo();

            startInfo.ArgumentList.Add("run");

            if (CoreKind == ProxyCoreKind.Xray)
            {
                startInfo.ArgumentList.Add("-config");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
            }

            startInfo.ArgumentList.Add(configPath);

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            var stopSignal = new ProcessStopSignal();

            process.OutputDataReceived += (_, eventArgs) =>
                ForwardOutput(eventArgs.Data);

            process.ErrorDataReceived += (_, eventArgs) =>
                ForwardOutput(eventArgs.Data);

            process.Exited += (_, _) =>
            {
                // При ручной остановке сообщение уже выводит форма.
                if (stopSignal.IsRequested)
                {
                    return;
                }

                int exitCode;

                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }

                ProcessExited?.Invoke(exitCode);
            };

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        $"Не удалось запустить ядро {DisplayName}."
                    );
                }

                coreProcess = process;
                coreStopSignal = stopSignal;

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch
            {
                stopSignal.Request();

                if (ReferenceEquals(coreProcess, process))
                {
                    coreProcess = null;
                    coreStopSignal = null;
                }

                TryKill(process);
                process.Dispose();

                throw;
            }
        }

        public async Task StopAsync()
        {
            if (disposed)
            {
                return;
            }

            Process? process = coreProcess;

            if (process is null)
            {
                return;
            }

            coreStopSignal?.Request();
            bool processExited = false;

            try
            {
                if (!SafeHasExited(process))
                {
                    try
                    {
                        process.Kill(
                            entireProcessTree: true
                        );
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось завершить процесс {DisplayName}.",
                            ex
                        );
                    }

                    using var stopTimeout =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(5)
                        );

                    try
                    {
                        await process.WaitForExitAsync(
                            stopTimeout.Token
                        );
                    }
                    catch (OperationCanceledException ex)
                    {
                        throw new TimeoutException(
                            $"Процесс {DisplayName} не завершился " +
                            "за 5 секунд.",
                            ex
                        );
                    }
                }

                processExited = true;
            }
            finally
            {
                // При ошибке остановки оставляем ссылку на живой
                // процесс, чтобы пользователь мог повторить попытку.
                if (processExited || SafeHasExited(process))
                {
                    if (ReferenceEquals(coreProcess, process))
                    {
                        coreProcess = null;
                        coreStopSignal = null;
                    }

                    process.Dispose();
                }
            }
        }

        private ProcessStartInfo CreateStartInfo()
        {
            string engineDirectory =
                Path.GetDirectoryName(ExecutablePath)
                ?? AppContext.BaseDirectory;

            return new ProcessStartInfo
            {
                FileName = ExecutablePath,
                WorkingDirectory = engineDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
        }

        private void ForwardOutput(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string cleaned =
                TerminalOutputSanitizer.Sanitize(text);

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                OutputReceived?.Invoke(cleaned);
            }
        }


        private void VerifyExecutableIntegrity()
        {
            string engineDirectory =
                Path.GetDirectoryName(ExecutablePath)
                ?? AppContext.BaseDirectory;

            VerifyFileIntegrity(
                ExecutablePath,
                Path.Combine(
                    engineDirectory,
                    CoreKind == ProxyCoreKind.Xray
                        ? "xray.sha256"
                        : "sing-box.sha256"
                ),
                DisplayName
            );

            if (CoreKind == ProxyCoreKind.Xray)
            {
                VerifyFileIntegrity(
                    Path.Combine(engineDirectory, "wintun.dll"),
                    Path.Combine(engineDirectory, "wintun.sha256"),
                    "Wintun"
                );
            }
        }

        private static void VerifyFileIntegrity(
            string filePath,
            string checksumPath,
            string displayName)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    $"Файл {Path.GetFileName(filePath)} не найден.",
                    filePath
                );
            }

            if (!File.Exists(checksumPath))
            {
                throw new FileNotFoundException(
                    $"Файл контрольной суммы {displayName} " +
                    "не найден.",
                    checksumPath
                );
            }

            string checksumText = File.ReadAllText(
                checksumPath,
                Encoding.UTF8
            );

            string expectedHash = checksumText.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries
            ).FirstOrDefault() ?? string.Empty;

            if (expectedHash.Length != 64 ||
                expectedHash.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException(
                    $"Файл контрольной суммы {displayName} имеет неправильный формат."
                );
            }

            using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                FileOptions.SequentialScan
            );

            string actualHash = Convert.ToHexString(
                SHA256.HashData(stream)
            );

            if (!string.Equals(
                    actualHash,
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Контрольная сумма {Path.GetFileName(filePath)} " +
                    "не совпадает. Запуск заблокирован."
                );
            }
        }

        private void EnsureFilesExist(
            string configPath)
        {
            if (!Exists)
            {
                throw new FileNotFoundException(
                    $"Файл {Path.GetFileName(ExecutablePath)} не найден.",
                    ExecutablePath
                );
            }

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException(
                    "Файл профиля не найден.",
                    configPath
                );
            }
        }

        private void DisposeOldProcess()
        {
            if (coreProcess is null)
            {
                return;
            }

            try
            {
                coreProcess.Dispose();
            }
            finally
            {
                coreProcess = null;
                coreStopSignal = null;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            coreStopSignal?.Request();

            try
            {
                if (coreProcess is not null &&
                    !SafeHasExited(coreProcess))
                {
                    TryKill(coreProcess);

                    coreProcess.WaitForExit(
                        milliseconds: 2000
                    );
                }
            }
            catch
            {
                // Программа уже закрывается,
                // поэтому ошибку можно игнорировать.
            }
            finally
            {
                coreProcess?.Dispose();
                coreProcess = null;
                coreStopSignal = null;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                disposed,
                this
            );
        }

        private static bool SafeHasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!SafeHasExited(process))
                {
                    process.Kill(
                        entireProcessTree: true
                    );
                }
            }
            catch
            {
                // Вызывающий код сохранит исходную ошибку
                // или продолжит завершение приложения.
            }
        }

        private sealed class ProcessStopSignal
        {
            private volatile bool requested;

            public bool IsRequested => requested;

            public void Request()
            {
                requested = true;
            }
        }
    }

    public sealed record CoreCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
