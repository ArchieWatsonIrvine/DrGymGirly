using SQLite;

namespace DrGymGirly.Models;

// No longer the schedule key itself (see WeeklyScheduleDay/Workout below) -
// kept only as an optional tag on Workout.Category so the "Add Exercise"
// picker can still filter toward relevant Exercise.Category values.
public enum DayType
{
    Rest,
    Pull,
    Push,
    Legs,
    Upper,
    Lower
}

// A named, reusable set of exercises (e.g. "Push Day A") that can be
// assigned as the default for one or more weekdays via WeeklyScheduleDay, or
// slotted into a single date via ScheduleOverride.
[Table("Workouts")]
public sealed class Workout
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Purely for ExercisePickerPage's category filter (DayTypeCategories) -
    // not a scheduling concept.
    public DayType Category { get; set; } = DayType.Rest;
}

// The exercise list belonging to one Workout. Several weekdays can share the
// same Workout (and so the same list) by pointing at the same WorkoutId.
[Table("WorkoutExercises")]
public sealed class WorkoutExercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int WorkoutId { get; set; }

    public int DisplayOrder { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public int TargetSets { get; set; }

    // Planned/target weight, as opposed to ExerciseLog.WeightCompletedKg which
    // records what was actually lifted on a given day. 0 means bodyweight /
    // not tracked (e.g. Plank, Pull-Up).
    public double TargetWeightKg { get; set; }
}

// Superseded by WeeklyScheduleWorkout (a weekday can now default to several
// Workouts at once - e.g. Physio every day plus Gym on top some days). Kept
// only so WorkoutDatabase can read any pre-existing single-workout-per-day
// rows on upgrade and migrate them across before dropping this table.
[Table("WeeklyScheduleDays")]
public sealed class WeeklyScheduleDay
{
    [PrimaryKey]
    public int DayOfWeek { get; set; }

    public int? WorkoutId { get; set; }
}

// Superseded by ScheduleOverrideWorkout, for the same reason as above.
[Table("ScheduleOverrides")]
public sealed class ScheduleOverride
{
    [PrimaryKey]
    public DateTime Date { get; set; }

    public int? WorkoutId { get; set; }
}

// One row per (weekday, Workout) pairing that defaults onto that weekday -
// several rows can share a DayOfWeek, so a day can default to more than one
// Workout. No rows for a given weekday = rest day. This is the recurring
// default - moving a workout to another day for one occurrence
// (ScheduleOverrideWorkout) never touches these rows.
[Table("WeeklyScheduleWorkouts")]
public sealed class WeeklyScheduleWorkout
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int DayOfWeek { get; set; }

    public int WorkoutId { get; set; }
}

// A one-off override for a single calendar date, taking precedence over that
// date's weekday defaults entirely (it's a full replacement list, not an
// addition to them) - e.g. "move Thursday's workout to Friday just this
// week". Several rows can share a Date, so an override can also list more
// than one Workout. A single row with a null WorkoutId means "this date is
// explicitly overridden to rest" and only ever appears alone for that date.
// Keyed by the specific Date rather than DayOfWeek, so the following week's
// same weekday has no override rows and automatically falls back to its
// WeeklyScheduleWorkout defaults.
[Table("ScheduleOverrideWorkouts")]
public sealed class ScheduleOverrideWorkout
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime Date { get; set; }

    public int? WorkoutId { get; set; }
}

// Maps each Workout's Category to the Exercise.Category values relevant to
// it, so the "Add Exercise" picker on EditWorkoutExercisesPage can default to
// showing only exercises that make sense (no bicep curls on leg day) while
// still allowing the full library via an explicit "Show All" escape hatch.
public static class DayTypeCategories
{
    // Abs/Cardio aren't tied to a push/pull/legs split, so they're always
    // offered regardless of category.
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

    // Null means "no meaningful split for this category" (Rest) - callers
    // should treat that as "don't filter, show every category."
    public static string[]? GetRelevant(DayType dayType) =>
        PrimaryByDayType.TryGetValue(dayType, out var primary) ? [.. primary, .. Universal] : null;
}
