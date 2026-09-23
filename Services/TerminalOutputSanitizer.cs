using System.Text;

namespace GeniaProxy.Services
{
    /// <summary>
    /// Удаляет ANSI-последовательности и управляющие символы
    /// из консольного вывода перед показом в интерфейсе.
    /// </summary>
    public static class TerminalOutputSanitizer
    {
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var result = new StringBuilder(text.Length);

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];

                if (character == '\x1B')
                {
                    index = SkipEscapeSequence(text, index);
                    continue;
                }

                // Сохраняем переносы строк и табуляцию. BEL (\x07),
                // backspace и другие управляющие символы не передаём
                // в RichTextBox, чтобы они не вызывали системные звуки.
                if (character is '\r' or '\n' or '\t' ||
                    !char.IsControl(character))
                {
                    result.Append(character);
                }
            }

            return result.ToString();
        }

        private static int SkipEscapeSequence(
            string text,
            int escapeIndex)
        {
            int nextIndex = escapeIndex + 1;

            if (nextIndex >= text.Length)
            {
                return escapeIndex;
            }

            char introducer = text[nextIndex];

            // CSI: ESC [ ... final-byte (0x40-0x7E)
            if (introducer == '[')
            {
                for (int index = nextIndex + 1;
                     index < text.Length;
                     index++)
                {
                    char current = text[index];

                    if (current is >= '@' and <= '~')
                    {
                        return index;
                    }
                }

                return text.Length - 1;
            }

            // OSC/DCS/SOS/PM/APC: ESC ]/P/X/^/_ ... BEL или ESC \
            if (introducer is ']' or 'P' or 'X' or '^' or '_')
            {
                for (int index = nextIndex + 1;
                     index < text.Length;
                     index++)
                {
                    if (text[index] == '\x07')
                    {
                        return index;
                    }

                    if (text[index] == '\x1B' &&
                        index + 1 < text.Length &&
                        text[index + 1] == '\\')
                    {
                        return index + 1;
                    }
                }

                return text.Length - 1;
            }

            // Двухбайтовая escape-последовательность.
            return nextIndex;
        }
    }
}
