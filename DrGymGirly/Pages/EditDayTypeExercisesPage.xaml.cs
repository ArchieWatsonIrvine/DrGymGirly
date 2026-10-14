using System.Collections.ObjectModel;
using DrGymGirly.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

// Edits a specific weekday's routine, not a DayType's - Tuesday's Upper day
// can have a different exercise list than Thursday's Upper day. DayType is
// still looked up (via WeeklyScheduleDay) purely for the heading and for
// category-filtering the exercise picker, not as the storage key.
[QueryProperty(nameof(DayOfWeek), "DayOfWeek")]
public partial class EditDayTypeExercisesPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly IServiceProvider _services;

    private DayType _dayType;

    public DayOfWeek DayOfWeek { get; set; }
    public string Heading => $"Edit {DayOfWeek} ({_dayType})";

    public ObservableCollection<DraftExercise> DraftExercises { get; set; } = [];

    public EditDayTypeExercisesPage(WorkoutDatabase database, IServiceProvider services)
    {
        _database = database;
        _services = services;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var schedule = await _database.GetWeeklyScheduleAsync();
        _dayType = schedule.TryGetValue(DayOfWeek, out var dayType) ? dayType : DayType.Rest;
        OnPropertyChanged(nameof(Heading));

        var existing = await _database.GetScheduledExercisesAsync((int)DayOfWeek);
        DraftExercises.Clear();
        foreach (var exercise in existing)
        {
            DraftExercises.Add(new DraftExercise
            {
                ExerciseName = exercise.ExerciseName,
                TargetSets = exercise.TargetSets,
                TargetReps = exercise.TargetReps,
                TargetWeightKg = exercise.TargetWeightKg
            });
        }
    }

    private async void OnAddExerciseClicked(object sender, EventArgs e)
    {
        var exercises = await _database.GetAllLibraryExercisesAsync();
        if (exercises.Count == 0)
        {
            await DisplayAlert("Library Empty", "Please create or import some exercises in your library first.", "OK");
            return;
        }

        var pickerPage = _services.GetRequiredService<ExercisePickerPage>();
        var selectedExercise = await pickerPage.SelectExerciseAsync(Navigation, exercises, _dayType);
        if (selectedExercise is null) return;

        var selected = selectedExercise.Name;

        var setsStr = await DisplayPromptAsync(selected, "Target sets:", "Next", "Cancel", "3", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(setsStr)) return;

        var repsStr = await DisplayPromptAsync(selected, "Target reps:", "Next", "Cancel", "10", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(repsStr)) return;

        var weightStr = await DisplayPromptAsync(selected, "Target weight in kg (leave blank for bodyweight):", "Save", "Skip", keyboard: Keyboard.Numeric);
        var targetWeight = double.TryParse(weightStr, out double parsedWeight) ? parsedWeight : 0;

        if (int.TryParse(setsStr, out int sets) && int.TryParse(repsStr, out int reps))
        {
            DraftExercises.Add(new DraftExercise
            {
                ExerciseName = selected,
                TargetSets = sets,
                TargetReps = reps,
                TargetWeightKg = targetWeight
            });
        }
    }

    private void OnRemoveExerciseInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.CommandParameter is DraftExercise item)
        {
            DraftExercises.Remove(item);
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var exercises = DraftExercises.Select(d => new ScheduledExercise
        {
            ExerciseName = d.ExerciseName,
            TargetSets = d.TargetSets,
            TargetReps = d.TargetReps,
            TargetWeightKg = d.TargetWeightKg
        }).ToList();

        await _database.SaveScheduledExercisesAsync((int)DayOfWeek, exercises);
        await DisplayAlert("Saved", $"{DayOfWeek} routine updated.", "OK");
        await Shell.Current.GoToAsync("..");
    }

    public class DraftExercise
    {
        public string ExerciseName { get; set; } = string.Empty;
        public int TargetSets { get; set; }
        public int TargetReps { get; set; }
        public double TargetWeightKg { get; set; }
        public string TargetsDisplay => TargetWeightKg > 0
            ? $"{TargetSets}x{TargetReps} @ {TargetWeightKg:0.#}kg"
            : $"{TargetSets}x{TargetReps}";
    }
}
