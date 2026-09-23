namespace GeniaProxy.Services
{
    public static class ApplicationBrandingService
    {
        public static void ApplyIcon(System.Windows.Forms.Form form)
        {
            ArgumentNullException.ThrowIfNull(form);

            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                return;
            }

            using System.Drawing.Icon? extracted =
                System.Drawing.Icon.ExtractAssociatedIcon(
                    executablePath
                );

            if (extracted is not null)
            {
                form.Icon = (System.Drawing.Icon)extracted.Clone();
            }
        }
    }
}
