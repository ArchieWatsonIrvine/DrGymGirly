using SQLite;

namespace DrGymGirly.Models;

// Marks that you did the prescribed reps for an exercise on a given day -
// independent of ExerciseLog, which records individual sets. You can "Log"
// zero, one, or several sets for an exercise and still mark it Complete (or
// not) separately; this is a plain done/not-done toggle, not a derived value.
[Table("ExerciseCompletions")]
public sealed class ExerciseCompletion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string ExerciseName { get; set; } = string.Empty;

    [Indexed]
    public DateTime DateCompleted { get; set; }
}
