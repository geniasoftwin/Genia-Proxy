using System.Threading;

namespace GeniaProxy.Services
{
    public sealed class SingleInstanceService : IDisposable
    {
        private readonly Mutex instanceMutex;
        private readonly string activationEventName;

        private EventWaitHandle? activationEvent;
        private CancellationTokenSource? cancellation;
        private Task? listenerTask;

        private bool disposed;

        public bool IsPrimaryInstance { get; }

        public event EventHandler? ActivationRequested;

        public SingleInstanceService(string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                throw new ArgumentException(
                    "Не указан идентификатор приложения.",
                    nameof(applicationId)
                );
            }

            string safeId = NormalizeApplicationId(applicationId);

            string mutexName =
                $@"Local\{safeId}.SingleInstance";

            activationEventName =
                $@"Local\{safeId}.Activate";

            instanceMutex = new Mutex(
                initiallyOwned: true,
                name: mutexName,
                createdNew: out bool createdNew
            );

            IsPrimaryInstance = createdNew;

            if (IsPrimaryInstance)
            {
                activationEvent = new EventWaitHandle(
                    initialState: false,
                    mode: EventResetMode.AutoReset,
                    name: activationEventName
                );
            }
        }


        public static bool WaitForPrimaryInstanceExit(
            string applicationId,
            TimeSpan timeout)
        {
            string safeId = NormalizeApplicationId(applicationId);
            string mutexName =
                $@"Local\{safeId}.SingleInstance";

            try
            {
                using Mutex existingMutex =
                    Mutex.OpenExisting(mutexName);

                bool acquired;

                try
                {
                    acquired = existingMutex.WaitOne(timeout);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    return false;
                }

                try
                {
                    existingMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex мог быть освобождён при завершении владельца.
                }

                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string NormalizeApplicationId(string applicationId)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                throw new ArgumentException(
                    "Не указан идентификатор приложения.",
                    nameof(applicationId)
                );
            }

            return new string(
                applicationId
                    .Where(character =>
                        char.IsLetterOrDigit(character) ||
                        character is '.' or '-' or '_')
                    .ToArray()
            );
        }

        public void StartListening()
        {
            ThrowIfDisposed();

            if (!IsPrimaryInstance ||
                activationEvent is null ||
                listenerTask is not null)
            {
                return;
            }

            cancellation =
                new CancellationTokenSource();

            CancellationToken token =
                cancellation.Token;

            listenerTask = Task.Run(
                () => ListenForActivation(token),
                token
            );
        }

        public void SignalPrimaryInstance()
        {
            ThrowIfDisposed();

            if (IsPrimaryInstance)
            {
                return;
            }

            // Первый экземпляр может ещё запускаться,
            // поэтому пробуем открыть событие несколько раз.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using EventWaitHandle existingEvent =
                        EventWaitHandle.OpenExisting(
                            activationEventName
                        );

                    existingEvent.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void ListenForActivation(
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                activationEvent?.WaitOne();

                if (token.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    ActivationRequested?.Invoke(
                        this,
                        EventArgs.Empty
                    );
                }
                catch
                {
                    // Ошибка обработчика не должна
                    // завершать поток ожидания.
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                disposed,
                this
            );
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            cancellation?.Cancel();

            if (IsPrimaryInstance)
            {
                activationEvent?.Set();
            }

            try
            {
                listenerTask?.Wait(
                    TimeSpan.FromMilliseconds(500)
                );
            }
            catch
            {
                // Приложение уже завершается.
            }

            activationEvent?.Dispose();
            cancellation?.Dispose();

            if (IsPrimaryInstance)
            {
                try
                {
                    instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex уже не принадлежит потоку.
                }
            }

            instanceMutex.Dispose();
        }
    }
}