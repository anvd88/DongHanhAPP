using System.Globalization;
using System.Text;

namespace KetoanMini;

public static class TextUtil
{
    private static readonly Dictionary<char, byte> Windows1252SpecialBytes = new()
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['ƒ'] = 0x83,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['†'] = 0x86,
        ['‡'] = 0x87,
        ['ˆ'] = 0x88,
        ['‰'] = 0x89,
        ['Š'] = 0x8A,
        ['‹'] = 0x8B,
        ['Œ'] = 0x8C,
        ['Ž'] = 0x8E,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
        ['˜'] = 0x98,
        ['™'] = 0x99,
        ['š'] = 0x9A,
        ['›'] = 0x9B,
        ['œ'] = 0x9C,
        ['ž'] = 0x9E,
        ['Ÿ'] = 0x9F
    };

    public static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = RepairMojibake(value);
        var normalized = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string RepairMojibake(string value)
    {
        if (string.IsNullOrEmpty(value) || !LooksLikeMojibake(value))
        {
            return value;
        }

        try
        {
            var bytes = new List<byte>(value.Length);
            foreach (var ch in value)
            {
                if (ch <= 0xFF)
                {
                    bytes.Add((byte)ch);
                }
                else if (Windows1252SpecialBytes.TryGetValue(ch, out var mapped))
                {
                    bytes.Add(mapped);
                }
                else
                {
                    return value;
                }
            }

            var repaired = Encoding.UTF8.GetString(bytes.ToArray());
            return LooksLikeMojibake(repaired) ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private static bool LooksLikeMojibake(string value)
    {
        if (value.Contains('\u00C3', StringComparison.Ordinal) ||
            value.Contains('\u00C4', StringComparison.Ordinal) ||
            value.Contains("\u00E1\u00BA", StringComparison.Ordinal) ||
            value.Contains("\u00E1\u00BB", StringComparison.Ordinal) ||
            value.Contains("\u00C6\u00B0", StringComparison.Ordinal))
        {
            return true;
        }
        return value.Contains('Ã', StringComparison.Ordinal) ||
            value.Contains('Ä', StringComparison.Ordinal) ||
            value.Contains('Â', StringComparison.Ordinal) ||
            value.Contains('Æ', StringComparison.Ordinal) ||
            value.Contains("áº", StringComparison.Ordinal) ||
            value.Contains("á»", StringComparison.Ordinal) ||
            value.Contains("â‚", StringComparison.Ordinal);
    }

    public static string FormatMoney(decimal value)
    {
        return value.ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN"));
    }

    public static decimal ParseMoney(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var raw = value.Trim().ToLowerInvariant();
        var multiplier = 1m;
        if (raw.EndsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^2].Trim();
            multiplier = 1_000_000m;
        }

        raw = raw.Replace(" ", "");
        if (raw.Contains(',') && raw.Contains('.'))
        {
            raw = raw.Replace(".", "").Replace(",", ".");
        }
        else if (raw.Contains(','))
        {
            raw = raw.Replace(",", ".");
        }
        else if (raw.Count(ch => ch == '.') > 1)
        {
            raw = raw.Replace(".", "");
        }
        else if (raw.Contains('.') && raw.Split('.')[^1].Length == 3)
        {
            raw = raw.Replace(".", "");
        }

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed * multiplier;
        }

        return 0;
    }

    public static DateOnly ParseDate(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateOnly.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date))
        {
            return date;
        }

        return DateOnly.FromDateTime(DateTime.Today);
    }

    public static string Initials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][0].ToString().ToUpper()
            : string.Concat(parts.Select(p => char.ToUpper(p[0])));
    }
}
