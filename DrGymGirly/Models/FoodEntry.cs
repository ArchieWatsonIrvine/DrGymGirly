using SQLite;

namespace DrGymGirly.Models;

public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snacks
}

[Table("FoodEntries")]
public sealed class FoodEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string UserId { get; set; } = string.Empty;

    [Indexed]
    public DateTime DateLogged { get; set; }

    [Indexed]
    public MealType MealType { get; set; }

    public string Name { get; set; } = string.Empty;

    // Stored per-100g so editing QuantityGrams later recalculates calories and
    // macros instead of needing to re-look the food up.
    public double CaloriesPer100g { get; set; }
    public double ProteinPer100g { get; set; }
    public double CarbsPer100g { get; set; }
    public double FatPer100g { get; set; }
    public double QuantityGrams { get; set; }
}
