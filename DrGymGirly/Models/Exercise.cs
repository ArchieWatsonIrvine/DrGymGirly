using SQLite;

namespace DrGymGirly.Models;

[Table("Exercises")]
public sealed class Exercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // e.g., "Legs", "Push", "Cardio"
}