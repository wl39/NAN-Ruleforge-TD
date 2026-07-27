using System.Text;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Defines every non-localized glyph used by the Stage 01 runtime UI and
    /// provides one coverage check shared by editor installation and tests.
    /// Keep decorative UI glyphs here so the WebGL font subset cannot silently
    /// lose them when localization data is expanded.
    /// </summary>
    public static class StageOneUiFontCoverage
    {
        public const string RequiredRuntimeSymbols = "✓×→−·";

        public static string FindMissingCharacters(
            Font font,
            params string[] sources)
        {
            if (font == null)
            {
                return string.Empty;
            }

            var missing = new StringBuilder();
            AppendMissing(
                font,
                RequiredRuntimeSymbols,
                missing);

            if (sources == null)
            {
                return missing.ToString();
            }

            for (int i = 0; i < sources.Length; i++)
            {
                AppendMissing(font, sources[i], missing);
            }

            return missing.ToString();
        }

        private static void AppendMissing(
            Font font,
            string source,
            StringBuilder missing)
        {
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            for (int i = 0; i < source.Length; i++)
            {
                char character = source[i];
                if (char.IsControl(character) ||
                    char.IsWhiteSpace(character) ||
                    font.HasCharacter(character) ||
                    Contains(missing, character))
                {
                    continue;
                }

                missing.Append(character);
            }
        }

        private static bool Contains(
            StringBuilder builder,
            char character)
        {
            for (int i = 0; i < builder.Length; i++)
            {
                if (builder[i] == character)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
