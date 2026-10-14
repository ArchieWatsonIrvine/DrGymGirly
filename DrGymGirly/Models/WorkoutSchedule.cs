using SQLite;

namespace DrGymGirly.Models;

// Appended, not reordered: the numeric enum value is what's actually stored
// in WeeklyScheduleDay, and EditSchedulePage's day-type Picker maps
// SelectedIndex straight onto this enum - inserting a value in the middle
// would silently reassign everyone's already-saved schedule.
public enum DayType
{
    Rest,
    Pull,
    Push,
    Legs,
    Upper,
    Lower
}

// One row per day of week (0=Sunday..6=Saturday, matching DateTime.DayOfWeek)
// saying what type of training day it is.
[Table("WeeklyScheduleDays")]
public sealed class WeeklyScheduleDay
{
    [PrimaryKey]
    public int DayOfWeek { get; set; }

    public DayType DayType { get; set; }
}

// The fixed exercise list for a specific weekday (e.g. Tuesday's routine can
// differ from Thursday's even if both are "Upper" days) - replaces the
// earlier DayTypeExercises table, which keyed by DayType and forced every
// day of the same type to share one list. See WorkoutDatabase's startup
// migration for how existing DayType-keyed routines carry over.
[Table("ScheduledExercises")]
public sealed class ScheduledExercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // 0=Sunday..6=Saturday, matching DateTime.DayOfWeek - same convention as
    // WeeklyScheduleDay.DayOfWeek.
    [Indexed]
    public int DayOfWeek { get; set; }

    public int DisplayOrder { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public int TargetSets { get; set; }
    public int TargetReps { get; set; }

    // Planned/target weight, as opposed to ExerciseLog.WeightCompletedKg which
    // records what was actually lifted on a given day. 0 means bodyweight /
    // not tracked (e.g. Plank, Pull-Up).
    public double TargetWeightKg { get; set; }
}

// Maps each split day type to the Exercise.Category values relevant to it, so
// the "Add Exercise" picker on EditDayTypeExercisesPage can default to showing
// only exercises that make sense for that day's type (no bicep curls on leg
// day) while still allowing the full library via an explicit "Show All"
// escape hatch - see EditDayTypeExercisesPage.xaml.cs.
public static class DayTypeCategories
{
    // Abs/Cardio aren't tied to a push/pull/legs split, so they're always
    // offered regardless of day type.
    private static readonly string[] Universal = ["Abs", "Cardio"];

    // Exercise.Category doesn't distinguish biceps from triceps, so "Arms"
    // is offered on both Push and Pull rather than assigned to just one.
    private static readonly Dictionary<DayType, string[]> PrimaryByDayType = new()
    {
        [DayType.Push] = ["Chest", "Shoulders", "Arms"],
        [DayType.Pull] = ["Back", "Arms"],
        [DayType.Legs] = ["Legs", "Calves"],
        [DayType.Upper] = ["Chest", "Back", "Shoulders", "Arms"],
        [DayType.Lower] = ["Legs", "Calves"],
    };

    // Null means "no meaningful split for this day type" (Rest) - callers
    // should treat that as "don't filter, show every category."
    public static string[]? GetRelevant(DayType dayType) =>
        PrimaryByDayType.TryGetValue(dayType, out var primary) ? [.. primary, .. Universal] : null;
}
