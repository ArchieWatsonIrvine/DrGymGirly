using DrGymGirly.Models;
using DrGymGirly.Services;

namespace DrGymGirly;

public partial class CreateExercisePage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly WgerExerciseApiService _exerciseApi;

    public CreateExercisePage(WorkoutDatabase database, WgerExerciseApiService exerciseApi)
    {
        _database = database;
        _exerciseApi = exerciseApi;
        InitializeComponent();
    }

    private async void OnSaveExerciseClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        var category = CategoryPicker.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(category))
        {
            await DisplayAlert("Missing Details", "Please give the exercise a name and choose a target category muscle group.", "OK");
            return;
        }

        var newExercise = new Exercise { Name = name, Category = category };
        await _database.SaveExerciseToLibraryAsync(newExercise);

        await DisplayAlert("Success", $"{name} added to your library!", "OK");
        await Shell.Current.GoToAsync("..");
    }

    private async void OnLoadStarterExercisesClicked(object sender, EventArgs e)
    {
        var addedCount = await _database.ImportExercisesToLibraryAsync(StarterExercisePresets.All);
        var skipped = StarterExercisePresets.All.Count - addedCount;

        await DisplayAlert(
            "Starter Exercises Loaded",
            $"Added {addedCount} new exercise(s)" + (skipped > 0 ? $" (skipped {skipped} already in your library)." : "."),
            "OK");
    }

    private async void OnImportFromWgerClicked(object sender, EventArgs e)
    {
        try
        {
            var fetched = await _exerciseApi.FetchExercisesAsync(limit: 40);

            if (fetched.Count == 0)
            {
                await DisplayAlert("No Results", "wger didn't return any exercises just now. Please try again.", "OK");
                return;
            }

            var addedCount = await _database.ImportExercisesToLibraryAsync(
                fetched.Select(x => (x.Name, x.Category)));

            var skipped = fetched.Count - addedCount;
            await DisplayAlert(
                "Import Complete",
                $"Added {addedCount} new exercise(s) from wger" + (skipped > 0 ? $" (skipped {skipped} already in your library)." : "."),
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import Failed", $"Could not reach wger: {ex.Message}", "OK");
        }
    }
}