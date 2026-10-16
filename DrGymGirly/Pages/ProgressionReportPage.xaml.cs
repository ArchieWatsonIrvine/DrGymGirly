namespace DrGymGirly;

// Map the dictionary keys to the public properties
[QueryProperty(nameof(ExerciseName), "SelectedExerciseName")]
public partial class ProgressionReportPage : ContentPage
{
    private readonly WorkoutDatabase _database;

    private string _exerciseName = string.Empty;
    public string ExerciseName
    {
        get => _exerciseName;
        set
        {
            _exerciseName = value;
            OnPropertyChanged(); // Notifies the UI to update the Title binding
        }
    }

    public List<WorkoutDatabase.ProgressionRecord> Records { get; private set; } = [];
    public string BaselineSummary { get; private set; } = string.Empty;
    public bool HasBaselineSummary => !string.IsNullOrEmpty(BaselineSummary);
    public bool HasChartData => Records.Count > 0;

    private readonly WeightProgressionDrawable _chartDrawable = new();

    // The constructor only takes the DI service now
    public ProgressionReportPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
        BindingContext = this;

        _chartDrawable.LineColor = ResourceColor("Primary", Colors.OrangeRed);
        _chartDrawable.BaselineColor = ResourceColor("Gray400", Colors.Gray);
        _chartDrawable.TextColor = ResourceColor("Gray500", Colors.Gray);
        ProgressionChart.Drawable = _chartDrawable;
    }

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        try
        {
            Records = await _database.GetExerciseProgressionReportAsync(ExerciseName);
            OnPropertyChanged(nameof(Records));
            OnPropertyChanged(nameof(HasChartData));

            // Records is newest-first; the chart needs oldest-first (left to
            // right), and the baseline is literally the first thing you ever
            // logged for this exercise - "the default" everything else is
            // measured against, per how this was asked for.
            var chronological = Records.AsEnumerable().Reverse().ToList();
            _chartDrawable.Points = chronological.Select(r => (r.LogDate, r.WeightAchieved)).ToList();
            ProgressionChart.Invalidate();

            if (chronological.Count > 1)
            {
                var first = chronological[0];
                var latest = chronological[^1];
                var diff = latest.WeightAchieved - first.WeightAchieved;
                var diffText = diff switch
                {
                    > 0 => $"+{diff:N1} kg",
                    < 0 => $"{diff:N1} kg",
                    _ => "no change"
                };
                BaselineSummary = $"Since your first session ({first.LogDate:d MMM yyyy}, {first.WeightAchieved:N1}kg): {diffText}";
            }
            else
            {
                BaselineSummary = string.Empty;
            }
            OnPropertyChanged(nameof(BaselineSummary));
            OnPropertyChanged(nameof(HasBaselineSummary));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load data: {ex.Message}", "OK");
        }
    }

    private async void OnRecordTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not WorkoutDatabase.ProgressionRecord record) return;

        var repsStr = await DisplayPromptAsync("Edit Entry", "Reps:", "Next", "Cancel", record.RepsAchieved.ToString(), -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(repsStr)) return;
        if (!int.TryParse(repsStr, out int reps))
        {
            await DisplayAlert("Invalid Input", "Reps must be a whole number.", "OK");
            return;
        }

        var weightStr = await DisplayPromptAsync("Edit Entry", "Weight (kg):", "Save", "Cancel", record.WeightAchieved.ToString("0.#"), -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(weightStr) || !double.TryParse(weightStr, out double weight))
        {
            await DisplayAlert("Invalid Input", "Please enter a valid weight.", "OK");
            return;
        }

        await _database.UpdateExerciseLogAsync(record.Id, reps, weight);
        await LoadReportAsync();
    }
}
