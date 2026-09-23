using GeniaProxy.Services;

namespace GeniaProxy
{
    internal static class Program
    {
        private const string ApplicationId = "GeniaProxy";
        private const string ElevatedRestartArgument =
            "--elevated-restart";
        private const string ConnectAfterElevationArgument =
            "--connect-after-elevation";
        private const string TunRecoveryOnlyArgument =
            "--recover-tun-only";

        [STAThread]
        private static void Main()
        {
            string[] arguments = Environment
                .GetCommandLineArgs()
                .Skip(1)
                .ToArray();

            bool elevatedRestart = arguments.Any(argument =>
                argument.Equals(
                    ElevatedRestartArgument,
                    StringComparison.OrdinalIgnoreCase
                ));

            bool connectAfterElevation = arguments.Any(argument =>
                argument.Equals(
                    ConnectAfterElevationArgument,
                    StringComparison.OrdinalIgnoreCase
                ));

            bool tunRecoveryOnly = arguments.Any(argument =>
                argument.Equals(
                    TunRecoveryOnlyArgument,
                    StringComparison.OrdinalIgnoreCase
                ));

            if (tunRecoveryOnly)
            {
                RunTunRecoveryOnly();
                return;
            }

            if (elevatedRestart)
            {
                SingleInstanceService.WaitForPrimaryInstanceExit(
                    ApplicationId,
                    TimeSpan.FromSeconds(15)
                );
            }

            using var singleInstance =
                new SingleInstanceService(ApplicationId);

            if (!singleInstance.IsPrimaryInstance)
            {
                singleInstance.SignalPrimaryInstance();
                return;
            }

            ApplicationConfiguration.Initialize();

            var application = new System.Windows.Application
            {
                ShutdownMode =
                    System.Windows.ShutdownMode.OnMainWindowClose
            };

            var mainWindow = new MainWindow(
                connectAfterElevation &&
                WindowsElevationService.IsAdministrator
            );

            singleInstance.ActivationRequested +=
                (_, _) =>
                    mainWindow.Dispatcher.BeginInvoke(
                        new Action(
                            mainWindow.ActivateFromSecondInstance
                        )
                    );

            singleInstance.StartListening();

            application.Run(mainWindow);
        }

        private static void RunTunRecoveryOnly()
        {
            if (!WindowsElevationService.IsAdministrator)
            {
                Environment.ExitCode = 5;
                return;
            }

            try
            {
                var tunNetworkService = new WindowsTunNetworkService();

                if (tunNetworkService.HasPendingBackup)
                {
                    tunNetworkService
                        .RestoreAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
        }
    }
}
