using SQLite;

namespace DrGymGirly.Models;

// One row per user holding their daily calorie/macro targets, used to fill the
// MyFitnessPal-style progress bars on the Nutrition page.
[Table("NutritionGoals")]
public sealed class NutritionGoal
{
    [PrimaryKey]
    public string UserId { get; set; } = string.Empty;

    public int DailyCalorieGoal { get; set; } = 2000;
    public double ProteinGoalGrams { get; set; } = 150;
    public double CarbsGoalGrams { get; set; } = 200;
    public double FatGoalGrams { get; set; } = 65;
}
