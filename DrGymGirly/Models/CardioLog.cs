using SQLite;

namespace DrGymGirly.Models;

// Logged independently of the day's main lifting routine, so cardio can be
// added to any day regardless of its assigned DayType.
[Table("CardioLogs")]
public sealed class CardioLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime DateLogged { get; set; }

    public string ActivityName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public double? DistanceKm { get; set; }
}
