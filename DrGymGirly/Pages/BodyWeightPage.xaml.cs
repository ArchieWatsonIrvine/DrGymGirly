namespace DrGymGirly;

public partial class BodyWeightPage : ContentPage
{
    private readonly WorkoutDatabase _database;

    public List<WorkoutDatabase.ProgressionRecord> Records { get; private set; } = [];
    public string BaselineSummary { get; private set; } = string.Empty;
    public bool HasBaselineSummary => !string.IsNullOrEmpty(BaselineSummary);
    public bool HasChartData => Records.Count > 0;

    private readonly WeightProgressionDrawable _chartDrawable = new();

    public BodyWeightPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
        BindingContext = this;

        _chartDrawable.LineColor = ResourceColor("Primary", Colors.OrangeRed);
        _chartDrawable.BaselineColor = ResourceColor("Gray400", Colors.Gray);
        _chartDrawable.TextColor = ResourceColor("Gray500", Colors.Gray);
        WeightChart.Drawable = _chartDrawable;
    }

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        Records = await _database.GetBodyWeightHistoryAsync();
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(HasChartData));

        var chronological = Records.AsEnumerable().Reverse().ToList();
        _chartDrawable.Points = chronological.Select(r => (r.LogDate, r.WeightAchieved)).ToList();
        WeightChart.Invalidate();

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
            BaselineSummary = $"Since your first weigh-in ({first.LogDate:d MMM yyyy}, {first.WeightAchieved:N1}kg): {diffText}";
        }
        else
        {
            BaselineSummary = string.Empty;
        }
        OnPropertyChanged(nameof(BaselineSummary));
        OnPropertyChanged(nameof(HasBaselineSummary));
    }

    private async void OnLogWeightClicked(object sender, EventArgs e)
    {
        var weightStr = await DisplayPromptAsync("Log Weight", "Today's weight in kg:", "Save", "Cancel", keyboard: Keyboard.Numeric);
        if (!double.TryParse(weightStr, out double weight) || weight <= 0)
        {
            if (!string.IsNullOrEmpty(weightStr)) await DisplayAlert("Invalid Input", "Please enter a valid weight.", "OK");
            return;
        }

        await _database.LogBodyWeightAsync(DateTime.Today, weight);
        await ReloadAsync();
    }
}
