using SQLite;

namespace DrGymGirly.Models;

// Matched to WorkoutExercise by name (same lightweight, non-FK convention
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

    // Set once this entry is corrected after the fact (via the Progression
    // Audit page), as opposed to how it was originally logged - lets the
    // day view strike it through so an edited value doesn't read as fresh.
    public bool WasEdited { get; set; }
}
