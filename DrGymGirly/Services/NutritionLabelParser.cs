using System.Globalization;
using System.Text.RegularExpressions;

namespace DrGymGirly.Services;

public sealed record ParsedNutritionLabel(double? CaloriesPer100g, double? ProteinPer100g, double? CarbsPer100g, double? FatPer100g);

// Best-effort extraction of the per-100g nutrition figures from OCR'd label
// text. Real labels vary enormously in layout (UK/EU "per 100g"/"per serving"
// columns, US Nutrition Facts, kJ-only energy, sub-lines like "of which
// sugars"/"of which saturates"), so this is a heuristic over line text, not a
// guarantee - callers must always let the user review/correct the result
// before saving, never treat it as authoritative.
public static class NutritionLabelParser
{
    public static ParsedNutritionLabel Parse(IEnumerable<string> lines)
    {
        double? calories = null, protein = null, carbs = null, fat = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var lower = line.ToLowerInvariant();

            if (calories is null && (lower.Contains("energy") || lower.Contains("calorie")))
            {
                calories = ExtractKcal(line);
            }
            else if (protein is null && lower.Contains("protein"))
            {
                protein = ExtractGrams(line);
            }
            // Excludes the "of which sugars" sub-line so the main
            // carbohydrate total isn't overwritten by a later, smaller number.
            else if (carbs is null && (lower.Contains("carbohydrate") || lower.Contains("carbs")) && !lower.Contains("sugar"))
            {
                carbs = ExtractGrams(line);
            }
            // Same idea for "of which saturates".
            else if (fat is null && lower.Contains("fat") && !lower.Contains("satur"))
            {
                fat = ExtractGrams(line);
            }
        }

        return new ParsedNutritionLabel(calories, protein, carbs, fat);
    }

    // Prefers an explicit kcal figure; falls back to converting a kJ figure
    // if that's all the label shows (1 kcal = 4.184 kJ).
    private static double? ExtractKcal(string line)
    {
        var kcalMatch = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*kcal", RegexOptions.IgnoreCase);
        if (kcalMatch.Success) return ParseInvariant(kcalMatch.Groups[1].Value);

        var kjMatch = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*kj", RegexOptions.IgnoreCase);
        if (kjMatch.Success) return Math.Round(ParseInvariant(kjMatch.Groups[1].Value) / 4.184, 1);

        return ExtractFirstNumber(line);
    }

    private static double? ExtractGrams(string line)
    {
        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*g\b", RegexOptions.IgnoreCase);
        return match.Success ? ParseInvariant(match.Groups[1].Value) : ExtractFirstNumber(line);
    }

    private static double? ExtractFirstNumber(string line)
    {
        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)");
        return match.Success ? ParseInvariant(match.Groups[1].Value) : null;
    }

    private static double ParseInvariant(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
