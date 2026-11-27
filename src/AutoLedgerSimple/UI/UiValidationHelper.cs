using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.UI;

/// <summary>
/// Safe parsing and formatting helpers for the WinForms layer.
/// Every parse goes through a TryParse variant, so bad user input never throws.
/// </summary>
public static class UiValidationHelper
{
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy" };
    private static readonly Regex ValidIntPattern = new(@"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)$", RegexOptions.Compiled);
    private static readonly Regex ValidDecimalPattern = new(@"^[+-]?(?:(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?|\.\d+)$", RegexOptions.Compiled);

    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (!ValidIntPattern.IsMatch(trimmed))
            return false;

        return int.TryParse(
            trimmed,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Replace("₦", string.Empty).Trim();
        if (cleaned.Length == 0 || !ValidDecimalPattern.IsMatch(cleaned))
            return false;

        return decimal.TryParse(
            cleaned,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static bool TryParseDateOnly(string? text, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return DateOnly.TryParseExact(
            text.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    public static string FormatNaira(decimal amount)
        => "₦" + amount.ToString("N2", CultureInfo.InvariantCulture);

    public static string FormatCategory(ServiceCategory category)
    {
        string name = category.ToString();
        var builder = new StringBuilder(name.Length + 4);

        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                builder.Append(' ');

            builder.Append(name[i]);
        }

        return builder.ToString();
    }
}
