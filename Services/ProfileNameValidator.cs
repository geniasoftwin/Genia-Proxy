namespace GeniaProxy.Services
{
    public static class ProfileNameValidator
    {
        private static readonly HashSet<string>
            ReservedWindowsNames = new(
                StringComparer.OrdinalIgnoreCase
            )
            {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                "COM1",
                "COM2",
                "COM3",
                "COM4",
                "COM5",
                "COM6",
                "COM7",
                "COM8",
                "COM9",
                "LPT1",
                "LPT2",
                "LPT3",
                "LPT4",
                "LPT5",
                "LPT6",
                "LPT7",
                "LPT8",
                "LPT9"
            };

        public static string Normalize(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            string candidate = name.Trim();

            if (candidate.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^5].Trim();
            }

            return candidate;
        }

        public static string? GetValidationError(
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Введите имя профиля.";
            }

            if (name.Length > 100)
            {
                return "Имя профиля слишком длинное.";
            }

            if (name is "." or "..")
            {
                return "Это имя нельзя использовать.";
            }

            if (name.EndsWith(' ') ||
                name.EndsWith('.'))
            {
                return
                    "Имя не должно заканчиваться " +
                    "пробелом или точкой.";
            }

            if (name.IndexOfAny(
                    Path.GetInvalidFileNameChars())
                >= 0)
            {
                return
                    "Имя содержит недопустимые символы.";
            }

            string baseName =
                name.Split('.')[0];

            if (ReservedWindowsNames.Contains(baseName))
            {
                return
                    "Это имя зарезервировано Windows.";
            }

            return null;
        }
    }
}
