using System.IO.Pipes;
using System.Text;
using System.Security.Principal;

namespace GeniaProxy.Services
{
    public sealed class SingleInstanceService : IDisposable
    {
        private const string ActivationRequest = "ACTIVATE";
        private const string ActivationAck = "ACK";

        private readonly Mutex instanceMutex;
        private readonly string activationPipeName;

        private CancellationTokenSource? cancellation;
        private Task? listenerTask;
        private bool ownsMutex;
        private bool disposed;

        public bool IsPrimaryInstance { get; private set; }

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
            string mutexName = $@"Local\{safeId}.SingleInstance";

            string userScope = NormalizeApplicationId(
                WindowsIdentity.GetCurrent().User?.Value ?? "CurrentUser"
            );

            activationPipeName =
                $"{safeId}.{userScope}.Activation";

            instanceMutex = new Mutex(
                initiallyOwned: true,
                name: mutexName,
                createdNew: out bool createdNew
            );

            IsPrimaryInstance = createdNew;
            ownsMutex = createdNew;
        }

        public static bool WaitForPrimaryInstanceExit(
            string applicationId,
            TimeSpan timeout)
        {
            string safeId = NormalizeApplicationId(applicationId);
            string mutexName = $@"Local\{safeId}.SingleInstance";

            try
            {
                using Mutex existingMutex = Mutex.OpenExisting(mutexName);

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
                    // Mutex may already have been released during shutdown.
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

        public bool TryActivatePrimary(
            TimeSpan timeout,
            out string? errorMessage)
        {
            ThrowIfDisposed();
            errorMessage = null;

            if (IsPrimaryInstance)
            {
                return true;
            }

            int timeoutMilliseconds = NormalizeTimeout(timeout);

            try
            {
                using var client = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: activationPipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous
                );

                client.Connect(timeoutMilliseconds);

                using var reader = new StreamReader(
                    client,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true
                );

                using var writer = new StreamWriter(
                    client,
                    new UTF8Encoding(false),
                    bufferSize: 1024,
                    leaveOpen: true
                )
                {
                    AutoFlush = true
                };

                writer.WriteLine(ActivationRequest);

                Task<string?> responseTask = reader.ReadLineAsync();
                string? response = responseTask
                    .WaitAsync(timeout)
                    .GetAwaiter()
                    .GetResult();

                if (!string.Equals(
                        response,
                        ActivationAck,
                        StringComparison.Ordinal))
                {
                    errorMessage =
                        "Primary instance returned an invalid activation response.";
                    return false;
                }

                return true;
            }
            catch (TimeoutException)
            {
                errorMessage =
                    "Primary instance activation pipe timed out.";
                return false;
            }
            catch (IOException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryBecomePrimaryAfterFailedActivation(
            TimeSpan timeout)
        {
            ThrowIfDisposed();

            if (IsPrimaryInstance)
            {
                return true;
            }

            bool acquired;

            try
            {
                acquired = instanceMutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return false;
            }

            IsPrimaryInstance = true;
            ownsMutex = true;
            return true;
        }

        public void StartListening()
        {
            ThrowIfDisposed();

            if (!IsPrimaryInstance || listenerTask is not null)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;

            listenerTask = Task.Run(
                () => ListenForActivationAsync(token),
                token
            );
        }

        private async Task ListenForActivationAsync(
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        activationPipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                    );

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(
                        server,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 1024,
                        leaveOpen: true
                    );

                    using var writer = new StreamWriter(
                        server,
                        new UTF8Encoding(false),
                        bufferSize: 1024,
                        leaveOpen: true
                    )
                    {
                        AutoFlush = true
                    };

                    string? request = await reader.ReadLineAsync(token);

                    if (!string.Equals(
                            request,
                            ActivationRequest,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        // The ACK is emitted only after the activation handler
                        // returns. Program.cs uses Dispatcher.Invoke, so a hung
                        // UI will not produce a false-positive ACK.
                        ActivationRequested?.Invoke(this, EventArgs.Empty);
                        await writer.WriteLineAsync(ActivationAck);
                    }
                    catch
                    {
                        // A failed activation is intentionally left without ACK;
                        // the secondary instance can then attempt safe takeover.
                    }
                }
                catch (OperationCanceledException) when (
                    token.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException) when (!token.IsCancellationRequested)
                {
                    await Task.Delay(100, token);
                }
                catch (UnauthorizedAccessException) when (
                    !token.IsCancellationRequested)
                {
                    await Task.Delay(250, token);
                }
            }
        }

        private static int NormalizeTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return 1;
            }

            double milliseconds = timeout.TotalMilliseconds;

            if (milliseconds >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(1, (int)Math.Ceiling(milliseconds));
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

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation?.Cancel();

            try
            {
                listenerTask?.Wait(TimeSpan.FromMilliseconds(750));
            }
            catch
            {
                // Application is shutting down; diagnostics must not block exit.
            }

            cancellation?.Dispose();

            if (ownsMutex)
            {
                try
                {
                    instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex is no longer owned by this thread.
                }
            }

            instanceMutex.Dispose();
        }
    }
}
