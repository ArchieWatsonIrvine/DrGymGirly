using SQLite;

namespace DrGymGirly.Models;

[Table("Meals")]
public sealed class Meal
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[Table("MealIngredients")]
public sealed class MealIngredient
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int MealId { get; set; }

    public string Name { get; set; } = string.Empty;

    // Real per-100g figures looked up from Open Food Facts when this
    // ingredient was added; the meal's totals are these scaled by QuantityGrams.
    public double CaloriesPer100g { get; set; }
    public double ProteinPer100g { get; set; }
    public double CarbsPer100g { get; set; }
    public double FatPer100g { get; set; }
    public double QuantityGrams { get; set; }
}
