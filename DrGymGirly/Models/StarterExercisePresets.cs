namespace DrGymGirly.Models;

// A curated starter list covering every Category offered on CreateExercisePage,
// so a fresh library isn't empty or solely dependent on the wger API being
// reachable. Fed through WorkoutDatabase.ImportExercisesToLibraryAsync, which
// already dedupes by name - safe to run more than once.
public static class StarterExercisePresets
{
    public static readonly IReadOnlyList<(string Name, string Category)> All =
    [
        ("Plank", "Abs"),
        ("Crunches", "Abs"),
        ("Hanging Leg Raise", "Abs"),
        ("Russian Twist", "Abs"),
        ("Cable Crunch", "Abs"),
        ("Sit-Up", "Abs"),

        ("Barbell Curl", "Arms"),
        ("Hammer Curl", "Arms"),
        ("Tricep Pushdown", "Arms"),
        ("Skull Crusher", "Arms"),
        ("Dumbbell Curl", "Arms"),
        ("Close-Grip Bench Press", "Arms"),

        ("Pull-Up", "Back"),
        ("Lat Pulldown", "Back"),
        ("Barbell Row", "Back"),
        ("Seated Cable Row", "Back"),
        ("Deadlift", "Back"),
        ("T-Bar Row", "Back"),

        ("Standing Calf Raise", "Calves"),
        ("Seated Calf Raise", "Calves"),
        ("Leg Press Calf Raise", "Calves"),
        ("Donkey Calf Raise", "Calves"),

        ("Treadmill Run", "Cardio"),
        ("Rowing Machine", "Cardio"),
        ("Stationary Bike", "Cardio"),
        ("Jump Rope", "Cardio"),
        ("Stair Climber", "Cardio"),

        ("Bench Press", "Chest"),
        ("Incline Dumbbell Press", "Chest"),
        ("Push-Up", "Chest"),
        ("Cable Fly", "Chest"),
        ("Chest Dip", "Chest"),
        ("Decline Bench Press", "Chest"),

        ("Back Squat", "Legs"),
        ("Leg Press", "Legs"),
        ("Romanian Deadlift", "Legs"),
        ("Walking Lunge", "Legs"),
        ("Leg Extension", "Legs"),
        ("Leg Curl", "Legs"),

        ("Overhead Press", "Shoulders"),
        ("Lateral Raise", "Shoulders"),
        ("Face Pull", "Shoulders"),
        ("Front Raise", "Shoulders"),
        ("Arnold Press", "Shoulders"),
        ("Rear Delt Fly", "Shoulders"),
    ];
}
