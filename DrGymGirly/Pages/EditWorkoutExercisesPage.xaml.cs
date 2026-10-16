using System.Collections.ObjectModel;
using DrGymGirly.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

// Edits one Workout's exercise list. A Workout can be the default for
// several weekdays at once (WeeklyScheduleDay), so this edits the shared
// list itself, not any one day's copy of it.
[QueryProperty(nameof(WorkoutId), "WorkoutId")]
public partial class EditWorkoutExercisesPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly IServiceProvider _services;

    private Workout? _workout;
    private bool _hasLoadedExercises;

    public int WorkoutId { get; set; }
    public string Heading => _workout is null ? "Edit Workout" : $"Edit {_workout.Name}";

    public ObservableCollection<DraftExercise> DraftExercises { get; set; } = [];

    public EditWorkoutExercisesPage(WorkoutDatabase database, IServiceProvider services)
    {
        _database = database;
        _services = services;
        InitializeComponent();
        BindingContext = this;
    }

    // OnAppearing fires again every time the ExercisePickerPage modal (opened
    // from OnAddExerciseClicked) pops back off, since this is the same page
    // instance becoming active again - not just on first navigation here. The
    // DraftExercises reload used to run unconditionally, which clobbered any
    // exercise just added but not yet Saved with whatever was last persisted.
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _workout = await _database.GetWorkoutAsync(WorkoutId);
        OnPropertyChanged(nameof(Heading));

        if (_hasLoadedExercises) return;
        _hasLoadedExercises = true;

        var existing = await _database.GetWorkoutExercisesAsync(WorkoutId);
        DraftExercises.Clear();
        foreach (var exercise in existing)
        {
            DraftExercises.Add(new DraftExercise
            {
                ExerciseName = exercise.ExerciseName,
                TargetSets = exercise.TargetSets,
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
        var selectedExercise = await pickerPage.SelectExerciseAsync(Navigation, exercises, _workout?.Category ?? DayType.Rest);
        if (selectedExercise is null) return;

        var selected = selectedExercise.Name;

        var setsStr = await DisplayPromptAsync(selected, "Target sets:", "Next", "Cancel", "3", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(setsStr)) return;

        if (!int.TryParse(setsStr, out int sets))
        {
            await DisplayAlert("Invalid Input", "Target sets must be a whole number.", "OK");
            return;
        }

        var weightStr = await DisplayPromptAsync(selected, "Target weight in kg (leave blank for bodyweight):", "Save", "Skip", keyboard: Keyboard.Numeric);
        var targetWeight = double.TryParse(weightStr, out double parsedWeight) ? parsedWeight : 0;

        DraftExercises.Add(new DraftExercise
        {
            ExerciseName = selected,
            TargetSets = sets,
            TargetWeightKg = targetWeight
        });
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
        var exercises = DraftExercises.Select(d => new WorkoutExercise
        {
            ExerciseName = d.ExerciseName,
            TargetSets = d.TargetSets,
            TargetWeightKg = d.TargetWeightKg
        }).ToList();

        await _database.SaveWorkoutExercisesAsync(WorkoutId, exercises);
        await DisplayAlert("Saved", $"{_workout?.Name} updated.", "OK");
        await Shell.Current.GoToAsync("..");
    }

    public class DraftExercise
    {
        public string ExerciseName { get; set; } = string.Empty;
        public int TargetSets { get; set; }
        public double TargetWeightKg { get; set; }
        public string TargetsDisplay => TargetWeightKg > 0
            ? $"{TargetSets} sets @ {TargetWeightKg:0.#}kg"
            : $"{TargetSets} sets";
    }
}
