using SQLite;

namespace DrGymGirly.Models;

// Matched to ScheduledExercise by name (same lightweight, non-FK convention
// the rest of this app already uses for exercise names).
[Table("ExerciseLogs")]
public sealed class ExerciseLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string ExerciseName { get; set; } = string.Empty;

    [Indexed]
    public DateTime DateLogged { get; set; }

    public int RepsCompleted { get; set; }
    public double WeightCompletedKg { get; set; }
}
