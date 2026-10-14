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

    // The constructor only takes the DI service now
    public ProgressionReportPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
        BindingContext = this;
    }

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
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load data: {ex.Message}", "OK");
        }
    }
}
