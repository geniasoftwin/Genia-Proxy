namespace GeniaProxy.Services
{
    public static class TimingAbExperiment
    {
        public const int ModeBBarrierMilliseconds = 150;

        private static readonly string ModePath = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "timing-ab-mode.txt"
        );

        public static string Mode { get; } = LoadMode();

        public static int BarrierMilliseconds =>
            string.Equals(Mode, "B", StringComparison.OrdinalIgnoreCase)
                ? ModeBBarrierMilliseconds
                : 0;

        public static async Task ApplyBeforeDirectDnsBarrierAsync(
            CancellationToken cancellationToken)
        {
            int delayMs = BarrierMilliseconds;
            if (delayMs <= 0)
            {
                return;
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        private static string LoadMode()
        {
            try
            {
                if (!File.Exists(ModePath))
                {
                    return "A";
                }

                string value = File.ReadAllText(ModePath).Trim();
                return string.Equals(value, "B", StringComparison.OrdinalIgnoreCase)
                    ? "B"
                    : "A";
            }
            catch
            {
                return "A";
            }
        }
    }
}
