namespace GeniaProxy.Services
{
    public enum ApplicationOperation
    {
        None,
        ImportProfile,
        CheckProfile,
        StartConnection,
        StopConnection,
        ExportProfiles,
        RestoreProfiles
    }

    public sealed class OperationCoordinator : IDisposable
    {
        private readonly object sync = new();
        private CancellationTokenSource? cancellation;
        private ApplicationOperation currentOperation;
        private bool disposed;

        public bool IsBusy
        {
            get
            {
                lock (sync)
                {
                    return currentOperation !=
                        ApplicationOperation.None;
                }
            }
        }

        public ApplicationOperation CurrentOperation
        {
            get
            {
                lock (sync)
                {
                    return currentOperation;
                }
            }
        }

        public CancellationToken Begin(
            ApplicationOperation operation)
        {
            if (operation == ApplicationOperation.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(operation)
                );
            }

            lock (sync)
            {
                ObjectDisposedException.ThrowIf(
                    disposed,
                    this
                );

                if (currentOperation !=
                    ApplicationOperation.None)
                {
                    throw new InvalidOperationException(
                        "Другая операция GeniaProxy уже выполняется."
                    );
                }

                cancellation = new CancellationTokenSource();
                currentOperation = operation;

                return cancellation.Token;
            }
        }

        public void End()
        {
            CancellationTokenSource? completed;

            lock (sync)
            {
                completed = cancellation;
                cancellation = null;
                currentOperation = ApplicationOperation.None;
            }

            completed?.Dispose();
        }

        public void Cancel()
        {
            CancellationTokenSource? active;

            lock (sync)
            {
                active = cancellation;
            }

            try
            {
                active?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Операция успела завершиться между чтением и отменой.
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? completed;

            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                completed = cancellation;
                cancellation = null;
                currentOperation = ApplicationOperation.None;
            }

            completed?.Cancel();
            completed?.Dispose();
        }

        public static string GetDisplayName(
            ApplicationOperation operation)
        {
            return operation switch
            {
                ApplicationOperation.None => "нет",
                ApplicationOperation.ImportProfile =>
                    "импорт профиля",
                ApplicationOperation.CheckProfile =>
                    "проверка профиля",
                ApplicationOperation.StartConnection =>
                    "запуск подключения",
                ApplicationOperation.StopConnection =>
                    "остановка подключения",
                ApplicationOperation.ExportProfiles =>
                    "экспорт профилей",
                ApplicationOperation.RestoreProfiles =>
                    "восстановление профилей",
                _ => "неизвестная операция"
            };
        }
    }
}
