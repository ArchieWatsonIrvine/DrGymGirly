using System.Collections.ObjectModel;
using DrGymGirly.Models;

namespace DrGymGirly;

// Create/manage named, reusable workout sets. Assigning one to a weekday (as
// a default or a one-off) happens elsewhere (EditSchedulePage / WorkoutPage's
// Move) - this page only manages the sets themselves and their exercises.
public partial class WorkoutLibraryPage : ContentPage
{
    private static readonly string[] CategoryOptions = Enum.GetNames<DayType>();

    private readonly WorkoutDatabase _database;

    public ObservableCollection<Workout> Workouts { get; } = [];

    public WorkoutLibraryPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var workouts = await _database.GetAllWorkoutsAsync();
        Workouts.Clear();
        foreach (var workout in workouts) Workouts.Add(workout);
    }

    private async void OnNewWorkoutClicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("New Workout", "Name this workout set:", "Next", "Cancel", "e.g. Push Day A");
        if (string.IsNullOrWhiteSpace(name)) return;

        var categoryName = await DisplayActionSheet("Category (for exercise filtering):", "Cancel", null, CategoryOptions);
        if (categoryName == "Cancel" || string.IsNullOrEmpty(categoryName)) return;

        var category = Enum.Parse<DayType>(categoryName);
        var workoutId = await _database.CreateWorkoutAsync(name.Trim(), category);

        var navParams = new Dictionary<string, object> { { "WorkoutId", workoutId } };
        await Shell.Current.GoToAsync(nameof(EditWorkoutExercisesPage), navParams);
    }

    private async void OnWorkoutTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not Workout workout) return;

        var navParams = new Dictionary<string, object> { { "WorkoutId", workout.Id } };
        await Shell.Current.GoToAsync(nameof(EditWorkoutExercisesPage), navParams);
    }

    private async void OnDeleteWorkoutInvoked(object sender, EventArgs e)
    {
        if (sender is not SwipeItem swipeItem || swipeItem.CommandParameter is not Workout workout) return;

        var confirmed = await DisplayAlert("Delete Workout", $"Delete \"{workout.Name}\"? Any days using it as their default will revert to rest.", "Delete", "Cancel");
        if (!confirmed) return;

        await _database.DeleteWorkoutAsync(workout.Id);
        await ReloadAsync();
    }
}
