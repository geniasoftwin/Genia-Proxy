using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace GeniaProxy.Services
{
    public enum ElevationRestartResult
    {
        Started,
        Cancelled,
        Failed
    }

    public enum TunRecoveryElevationResult
    {
        Recovered,
        Cancelled,
        Failed
    }

    public static class WindowsElevationService
    {
        private const int ErrorCancelled = 1223;

        public static bool IsAdministrator
        {
            get
            {
                using WindowsIdentity identity =
                    WindowsIdentity.GetCurrent();

                var principal = new WindowsPrincipal(identity);

                return principal.IsInRole(
                    WindowsBuiltInRole.Administrator
                );
            }
        }

        public static ElevationRestartResult RestartAsAdministrator(
            bool connectAfterElevation,
            out string? errorMessage)
        {
            errorMessage = null;

            if (IsAdministrator)
            {
                return ElevationRestartResult.Started;
            }

            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                errorMessage =
                    "Не удалось определить путь к GeniaProxy.exe.";
                return ElevationRestartResult.Failed;
            }

            string arguments = connectAfterElevation
                ? "--elevated-restart --connect-after-elevation"
                : "--elevated-restart";

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process? process = Process.Start(startInfo);

                if (process is null)
                {
                    errorMessage =
                        "Windows не создала elevated-процесс GeniaProxy.";
                    return ElevationRestartResult.Failed;
                }

                process.Dispose();
                return ElevationRestartResult.Started;
            }
            catch (Win32Exception ex) when (
                ex.NativeErrorCode == ErrorCancelled)
            {
                return ElevationRestartResult.Cancelled;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return ElevationRestartResult.Failed;
            }
        }
        public static async Task<(TunRecoveryElevationResult Result, string? ErrorMessage)>
            RecoverTunNetworkAsAdministratorAsync(
                CancellationToken cancellationToken = default)
        {
            if (IsAdministrator)
            {
                return (TunRecoveryElevationResult.Recovered, null);
            }

            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                return (
                    TunRecoveryElevationResult.Failed,
                    "Не удалось определить путь к GeniaProxy.exe."
                );
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--recover-tun-only",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                using Process? process = Process.Start(startInfo);

                if (process is null)
                {
                    return (
                        TunRecoveryElevationResult.Failed,
                        "Windows не создала elevated recovery-процесс GeniaProxy."
                    );
                }

                await process.WaitForExitAsync(cancellationToken);

                return process.ExitCode == 0
                    ? (TunRecoveryElevationResult.Recovered, null)
                    : (
                        TunRecoveryElevationResult.Failed,
                        $"Elevated TUN recovery завершился с кодом {process.ExitCode}."
                    );
            }
            catch (Win32Exception ex) when (
                ex.NativeErrorCode == ErrorCancelled)
            {
                return (TunRecoveryElevationResult.Cancelled, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (TunRecoveryElevationResult.Failed, ex.Message);
            }
        }

    }
}
