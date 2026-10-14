using SQLite;

namespace DrGymGirly.Models;

// A user-entered food that Open Food Facts doesn't have (or has wrong), kept
// so it only ever needs typing in once. Barcode is null when the entry came
// from a plain name search miss rather than a scan.
[Table("CustomFoodItems")]
public sealed class CustomFoodItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string UserId { get; set; } = string.Empty;

    [Indexed]
    public string? Barcode { get; set; }

    public string Name { get; set; } = string.Empty;
    public double CaloriesPer100g { get; set; }
    public double ProteinPer100g { get; set; }
    public double CarbsPer100g { get; set; }
    public double FatPer100g { get; set; }
    public DateTime CreatedUtc { get; set; }
}
