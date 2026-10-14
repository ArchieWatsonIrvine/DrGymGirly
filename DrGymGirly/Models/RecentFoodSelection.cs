using SQLite;

namespace DrGymGirly.Models;

[Table("RecentFoodSelections")]
public sealed class RecentFoodSelection
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string UserId { get; set; } = string.Empty;

    // Open Food Facts barcode, used to dedupe repeat selections of the same food.
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public double? CaloriesPer100g { get; set; }
    public string? Quantity { get; set; }
    public DateTime LastUsedUtc { get; set; }
}
