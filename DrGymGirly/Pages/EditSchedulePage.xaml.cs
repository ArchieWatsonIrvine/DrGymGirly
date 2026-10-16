using DrGymGirly.Models;

namespace DrGymGirly;

public partial class EditSchedulePage : ContentPage
{
    private readonly WorkoutDatabase _database;

    private List<Workout> _workouts = [];
    private readonly Dictionary<DayOfWeek, HashSet<int>> _selections =
        Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => new HashSet<int>());

    private Dictionary<Button, DayOfWeek> _dayByButton = [];

    public EditSchedulePage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();

        _dayByButton = new Dictionary<Button, DayOfWeek>
        {
            [MondayButton] = DayOfWeek.Monday,
            [TuesdayButton] = DayOfWeek.Tuesday,
            [WednesdayButton] = DayOfWeek.Wednesday,
            [ThursdayButton] = DayOfWeek.Thursday,
            [FridayButton] = DayOfWeek.Friday,
            [SaturdayButton] = DayOfWeek.Saturday,
            [SundayButton] = DayOfWeek.Sunday
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _workouts = await _database.GetAllWorkoutsAsync();
        var defaults = await _database.GetWeeklyDefaultsAsync();

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            _selections[day] = new HashSet<int>(defaults.GetValueOrDefault(day, []));
        }

        foreach (var (button, day) in _dayByButton)
        {
            RefreshButtonText(button, day);
        }
    }

    private void RefreshButtonText(Button button, DayOfWeek day)
    {
        var names = _workouts.Where(w => _selections[day].Contains(w.Id)).Select(w => w.Name).ToList();
        button.Text = names.Count == 0 ? "Rest" : string.Join(", ", names);
    }

    private async void OnDayClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || !_dayByButton.TryGetValue(button, out var day)) return;

        if (_workouts.Count == 0)
        {
            await DisplayAlert("No Workouts Yet", "Create a workout in Workouts first, then assign it here.", "OK");
            return;
        }

        await PickWorkoutsForDayAsync(day);
        RefreshButtonText(button, day);
    }

    // Toggles membership one tap at a time via a repeated action sheet - the
    // closest thing to a multi-select this app's dialog-only UI style has,
    // same idiom as the rest of its DisplayPromptAsync/DisplayActionSheet flows.
    private async Task PickWorkoutsForDayAsync(DayOfWeek day)
    {
        var selected = _selections[day];

        while (true)
        {
            var options = _workouts.Select(w => (selected.Contains(w.Id) ? "✓ " : "   ") + w.Name).ToList();
            options.Add("Done");

            var chosen = await DisplayActionSheet($"{day}'s workouts - tap to toggle", "Cancel", null, options.ToArray());
            if (string.IsNullOrEmpty(chosen) || chosen == "Cancel" || chosen == "Done") return;

            var index = options.IndexOf(chosen);
            if (index < 0 || index >= _workouts.Count) continue;

            var workoutId = _workouts[index].Id;
            if (!selected.Remove(workoutId)) selected.Add(workoutId);
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var defaults = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, d => _selections[d].ToList());

        await _database.UpdateWeeklyDefaultsAsync(defaults);
        await DisplayAlert("Saved", "Your weekly schedule has been updated.", "OK");
        await Shell.Current.GoToAsync("..");
    }
}
