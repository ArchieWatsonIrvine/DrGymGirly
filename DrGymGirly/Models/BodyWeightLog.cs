using SQLite;

namespace DrGymGirly.Models;

// One entry per calendar day - logging again the same day replaces that
// day's value rather than adding a second row.
[Table("BodyWeightLogs")]
public sealed class BodyWeightLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime DateLogged { get; set; }

    public double WeightKg { get; set; }
}
