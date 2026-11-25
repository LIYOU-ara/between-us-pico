using System;
using System.Text.RegularExpressions;

// Pure C# validation shared by the language and visual response contracts.
public static class ConversationModelFields
{
    static readonly Regex Restricted = new Regex(
        @"(?<![A-Za-z])(?:NPD|PPD|BPD)(?![A-Za-z])|人格障碍|偏执型人格|自恋型人格",
        RegexOptions.IgnoreCase);

    // field is a caller-defined field code, never the returned model text.
    public static string Required(string field, string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return field + ":missing";
        return ValidatePresent(field, value, max);
    }

    // An absent uncertainty is valid; a supplied value still follows the same limits.
    public static string Optional(string field, string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return ValidatePresent(field, value, max);
    }

    // Only these coarse hypotheses can reach the display; unknown text is discarded.
    public static string EmotionHypothesis(string value)
    {
        switch (value)
        {
            case "平静":
            case "愉快":
            case "不悦":
            case "惊讶":
            case "紧张":
            case "难判断":
                return value;
            default:
                return "难判断";
        }
    }

    static string ValidatePresent(string field, string value, int max)
    {
        if (value.Length > max) return field + ":too_long";
        if (Restricted.IsMatch(value)) return field + ":restricted";
        return null;
    }
}
