using System.Text;

namespace Qwader.Application.Common;

/// <summary>
/// Normalizes Arabic text for searching. The same rules run in the web client (lib/arabic.ts),
/// and the result is stored in indexed NormalizedName columns.
/// </summary>
public static class ArabicText
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        var lastWasSpace = false;

        foreach (var raw in input.Trim())
        {
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                _ => char.ToLowerInvariant(raw),
            };

            // Tashkeel (harakat), superscript alef and tatweel carry no search meaning.
            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ') continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            sb.Append(c);
        }

        return sb.ToString();
    }
}
