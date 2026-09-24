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

        private static readonly TimeSpan ElevationHandoffTimeout =
            TimeSpan.FromSeconds(30);

        private static readonly TimeSpan ActivationAckTimeout =
            TimeSpan.FromSeconds(2);

        private static readonly TimeSpan TakeoverTimeout =
            TimeSpan.FromSeconds(6);

        [STAThread]
        private static void Main()
        {
            Guid startupAttemptId = Guid.NewGuid();
            StartupJournal startupJournal = StartupJournal.CreateDefault();

            string[] arguments = Environment
                .GetCommandLineArgs()
                .Skip(1)
                .ToArray();

            bool elevatedRestart = HasArgument(
                arguments,
                ElevatedRestartArgument
            );

            bool connectAfterElevation = HasArgument(
                arguments,
                ConnectAfterElevationArgument
            );

            bool tunRecoveryOnly = HasArgument(
                arguments,
                TunRecoveryOnlyArgument
            );

            startupJournal.TryAppend(
                "startup.enter",
                startupAttemptId,
                new Dictionary<string, object?>
                {
                    ["elevatedRestart"] = elevatedRestart,
                    ["connectAfterElevation"] = connectAfterElevation,
                    ["tunRecoveryOnly"] = tunRecoveryOnly
                }
            );

            RegisterEarlyExceptionDiagnostics(
                startupJournal,
                startupAttemptId
            );

            try
            {
                ProtocolLabFeatureCatalog.ThrowIfAlpha1BoundaryViolated();

                startupJournal.TryAppend(
                    "startup.protocolLabBoundary",
                    startupAttemptId,
                    new Dictionary<string, object?>
                    {
                        ["safe"] = true,
                        ["experimentalCapabilities"] =
                            ProtocolLabFeatureCatalog.All.Count
                    }
                );

                if (tunRecoveryOnly)
                {
                    RunTunRecoveryOnly(
                        startupJournal,
                        startupAttemptId
                    );
                    return;
                }

                if (elevatedRestart)
                {
                    startupJournal.TryAppend(
                        "startup.elevationHandoffWait",
                        startupAttemptId,
                        new Dictionary<string, object?>
                        {
                            ["timeoutSeconds"] =
                                ElevationHandoffTimeout.TotalSeconds
                        }
                    );

                    bool previousExited =
                        SingleInstanceService.WaitForPrimaryInstanceExit(
                            ApplicationId,
                            ElevationHandoffTimeout
                        );

                    startupJournal.TryAppend(
                        "startup.elevationHandoffResult",
                        startupAttemptId,
                        new Dictionary<string, object?>
                        {
                            ["previousPrimaryExited"] = previousExited
                        }
                    );

                    if (!previousExited)
                    {
                        ShowStartupProblem(
                            "Предыдущий экземпляр GeniaProxy не завершился " +
                            "после UAC-перезапуска.\n\n" +
                            "Новый ADMIN-экземпляр не будет запускаться поверх " +
                            "зависшего процесса. Завершите старый GeniaProxy в " +
                            "Диспетчере задач и запустите программу снова."
                        );
                        return;
                    }
                }

                using var singleInstance =
                    new SingleInstanceService(ApplicationId);

                if (!singleInstance.IsPrimaryInstance)
                {
                    startupJournal.TryAppend(
                        "startup.secondaryDetected",
                        startupAttemptId
                    );

                    bool activated = singleInstance.TryActivatePrimary(
                        ActivationAckTimeout,
                        out string? activationError
                    );

                    startupJournal.TryAppend(
                        activated
                            ? "startup.secondaryActivationAcknowledged"
                            : "startup.secondaryActivationFailed",
                        startupAttemptId,
                        new Dictionary<string, object?>
                        {
                            ["error"] = activationError
                        }
                    );

                    if (activated)
                    {
                        return;
                    }

                    bool takeover =
                        singleInstance.TryBecomePrimaryAfterFailedActivation(
                            TakeoverTimeout
                        );

                    startupJournal.TryAppend(
                        takeover
                            ? "startup.safeTakeoverSucceeded"
                            : "startup.safeTakeoverTimedOut",
                        startupAttemptId,
                        new Dictionary<string, object?>
                        {
                            ["timeoutSeconds"] =
                                TakeoverTimeout.TotalSeconds
                        }
                    );

                    if (!takeover)
                    {
                        ShowStartupProblem(
                            "Другой процесс GeniaProxy существует, но не отвечает " +
                            "на запрос показа окна.\n\n" +
                            "GeniaProxy не будет автоматически завершать его, " +
                            "потому что во время TUN cleanup это могло бы оставить " +
                            "маршруты/DNS в промежуточном состоянии.\n\n" +
                            "Если окно не появилось, завершите зависший GeniaProxy " +
                            "в Диспетчере задач и запустите программу снова.\n\n" +
                            "Диагностика: data\\diagnostics\\startup-journal.jsonl"
                        );
                        return;
                    }
                }

                startupJournal.TryAppend(
                    "startup.primaryAcquired",
                    startupAttemptId
                );

                RunPreUiTunRecovery(
                    startupJournal,
                    startupAttemptId
                );

                ApplicationConfiguration.Initialize();

                var application = new System.Windows.Application
                {
                    ShutdownMode =
                        System.Windows.ShutdownMode.OnMainWindowClose
                };

                application.DispatcherUnhandledException +=
                    (_, eventArgs) =>
                        startupJournal.TryAppend(
                            "startup.dispatcherUnhandledException",
                            startupAttemptId,
                            ExceptionDetails(eventArgs.Exception)
                        );

                var mainWindow = new MainWindow(
                    connectAfterElevation &&
                    WindowsElevationService.IsAdministrator
                );

                singleInstance.ActivationRequested +=
                    (_, _) =>
                        mainWindow.Dispatcher.Invoke(
                            new Action(
                                mainWindow.ActivateFromSecondInstance
                            )
                        );

                singleInstance.StartListening();

                startupJournal.TryAppend(
                    "startup.uiRun",
                    startupAttemptId
                );

                application.Run(mainWindow);

                startupJournal.TryAppend(
                    "startup.cleanExit",
                    startupAttemptId
                );
            }
            catch (Exception ex)
            {
                startupJournal.TryAppend(
                    "startup.fatal",
                    startupAttemptId,
                    ExceptionDetails(ex)
                );

                ShowStartupProblem(
                    "GeniaProxy не удалось завершить запуск.\n\n" +
                    ex.Message +
                    "\n\nДиагностика: data\\diagnostics\\startup-journal.jsonl"
                );
            }
        }

        private static void RunPreUiTunRecovery(
            StartupJournal journal,
            Guid attemptId)
        {
            var tunNetworkService = new WindowsTunNetworkService();

            if (!tunNetworkService.HasPendingBackup)
            {
                journal.TryAppend(
                    "startup.tunRecoveryNotNeeded",
                    attemptId
                );
                return;
            }

            journal.TryAppend(
                "startup.tunRecoveryPending",
                attemptId
            );

            if (!WindowsElevationService.IsAdministrator)
            {
                journal.TryAppend(
                    "startup.tunRecoveryNeedsElevation",
                    attemptId
                );
                return;
            }

            try
            {
                journal.TryAppend(
                    "startup.tunRecoveryBegin",
                    attemptId
                );

                TunRestoreOutcome outcome = tunNetworkService
                    .RestoreAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                journal.TryAppend(
                    "startup.tunRecoveryComplete",
                    attemptId,
                    new Dictionary<string, object?>
                    {
                        ["outcome"] = outcome.ToString(),
                        ["snapshotRemaining"] =
                            tunNetworkService.HasPendingBackup
                    }
                );
            }
            catch (Exception ex)
            {
                journal.TryAppend(
                    "startup.tunRecoveryFailed",
                    attemptId,
                    ExceptionDetails(ex)
                );

                // The existing ConnectionSession initialization/recovery UI
                // remains the second recovery line. Do not block startup here.
            }
        }

        private static void RunTunRecoveryOnly(
            StartupJournal journal,
            Guid attemptId)
        {
            if (!WindowsElevationService.IsAdministrator)
            {
                journal.TryAppend(
                    "startup.recoveryHelperRejected",
                    attemptId,
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "not-administrator"
                    }
                );
                Environment.ExitCode = 5;
                return;
            }

            try
            {
                var tunNetworkService = new WindowsTunNetworkService();

                if (tunNetworkService.HasPendingBackup)
                {
                    journal.TryAppend(
                        "startup.recoveryHelperBegin",
                        attemptId
                    );

                    TunRestoreOutcome outcome = tunNetworkService
                        .RestoreAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    journal.TryAppend(
                        "startup.recoveryHelperComplete",
                        attemptId,
                        new Dictionary<string, object?>
                        {
                            ["outcome"] = outcome.ToString(),
                            ["snapshotRemaining"] =
                                tunNetworkService.HasPendingBackup
                        }
                    );
                }
                else
                {
                    journal.TryAppend(
                        "startup.recoveryHelperNothingToDo",
                        attemptId
                    );
                }

                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                journal.TryAppend(
                    "startup.recoveryHelperFailed",
                    attemptId,
                    ExceptionDetails(ex)
                );
                Environment.ExitCode = 1;
            }
        }

        private static void RegisterEarlyExceptionDiagnostics(
            StartupJournal journal,
            Guid attemptId)
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (_, eventArgs) =>
                {
                    if (eventArgs.ExceptionObject is Exception ex)
                    {
                        journal.TryAppend(
                            "startup.unhandledException",
                            attemptId,
                            ExceptionDetails(ex)
                        );
                    }
                    else
                    {
                        journal.TryAppend(
                            "startup.unhandledException",
                            attemptId,
                            new Dictionary<string, object?>
                            {
                                ["exceptionType"] = "unknown"
                            }
                        );
                    }
                };

            TaskScheduler.UnobservedTaskException +=
                (_, eventArgs) =>
                    journal.TryAppend(
                        "startup.unobservedTaskException",
                        attemptId,
                        ExceptionDetails(eventArgs.Exception)
                    );
        }

        private static IReadOnlyDictionary<string, object?> ExceptionDetails(
            Exception exception)
        {
            return new Dictionary<string, object?>
            {
                ["exceptionType"] = exception.GetType().FullName,
                ["message"] = exception.Message
            };
        }

        private static bool HasArgument(
            IEnumerable<string> arguments,
            string expected)
        {
            return arguments.Any(argument =>
                argument.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase
                ));
        }

        private static void ShowStartupProblem(string message)
        {
            try
            {
                System.Windows.MessageBox.Show(
                    message,
                    "GeniaProxy — запуск",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
            }
            catch
            {
                // If the desktop is unavailable, startup-journal remains the
                // authoritative diagnostic channel.
            }
        }
    }
}
