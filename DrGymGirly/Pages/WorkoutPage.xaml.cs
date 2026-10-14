using System.Collections.ObjectModel;
using DrGymGirly.Models;

namespace DrGymGirly;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutDatabase _database;

    private DateTime _selectedDate = DateTime.Today;
    private DayType _currentDayType = DayType.Rest;

    public ObservableCollection<WorkoutCalendarDay> CalendarDays { get; } = [];
    public List<ExerciseRowDisplay> Exercises { get; private set; } = [];
    public List<WorkoutDatabase.CardioLogDisplay> CardioLogs { get; private set; } = [];

    public string SelectedDateHeading =>
        _selectedDate == DateTime.Today ? "Today's Workout" : _selectedDate.ToString("dddd, d MMMM");

    public string DayTypeHeading { get; private set; } = string.Empty;

    // Monday-start week: DayOfWeek has Sunday=0..Saturday=6, so shift by 6
    // first to make Monday the 0-offset day instead of Sunday.
    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public WorkoutPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await BuildCalendarAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var dayView = await _database.GetWorkoutDayViewAsync(_selectedDate);
        _currentDayType = dayView.DayType;

        DayTypeHeading = dayView.DayType == DayType.Rest ? "Rest Day" : $"{dayView.DayType} Day";

        var dayTypeColor = DayTypeColors.For(_currentDayType);
        Exercises = dayView.Exercises.Select(e => new ExerciseRowDisplay
        {
            ExerciseName = e.ExerciseName,
            TargetsDisplay = e.TargetsDisplay,
            PreviousPerformance = e.PreviousPerformance,
            LoggedToday = e.LoggedToday,
            LoggedTodayText = e.LoggedTodayText,
            IsCompleted = e.IsCompleted,
            DayTypeColor = dayTypeColor
        }).ToList();

        CardioLogs = dayView.CardioLogs;

        JumpDatePicker.Date = _selectedDate;

        OnPropertyChanged(nameof(DayTypeHeading));
        OnPropertyChanged(nameof(Exercises));
        OnPropertyChanged(nameof(CardioLogs));
    }

    // Renders the 7 days of the current week, colored by each day's scheduled
    // DayType, with a dot marking days that already have something logged.
    private async Task BuildCalendarAsync()
    {
        var weekStart = StartOfWeek(DateTime.Today);
        const int totalCells = 7;
        var weekEndExclusive = weekStart.AddDays(totalCells);

        var schedule = await _database.GetWeeklyScheduleAsync();
        var loggedDates = await _database.GetLoggedWorkoutDatesAsync(weekStart, weekEndExclusive);

        CalendarDays.Clear();
        for (var i = 0; i < totalCells; i++)
        {
            var date = weekStart.AddDays(i);
            schedule.TryGetValue(date.DayOfWeek, out var dayType);

            CalendarDays.Add(new WorkoutCalendarDay
            {
                Date = date,
                IsToday = date == DateTime.Today,
                IsSelected = date == _selectedDate,
                HasLogged = loggedDates.Contains(date),
                DayType = dayType
            });
        }
    }

    private async void OnCalendarDayTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not WorkoutCalendarDay day) return;

        _selectedDate = day.Date;

        await BuildCalendarAsync();
        await ReloadAsync();
        OnPropertyChanged(nameof(SelectedDateHeading));
    }

    private async void OnLogSetClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ExerciseRowDisplay row) return;

        if (!int.TryParse(row.CurrentReps, out int reps) || !double.TryParse(row.CurrentWeight, out double weight))
        {
            await DisplayAlert("Invalid Input", "Please enter valid numbers for reps and weight.", "OK");
            return;
        }

        await _database.LogExerciseSetAsync(_selectedDate, row.ExerciseName, reps, weight);
        await ReloadAsync();
    }

    private async void OnToggleCompleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ExerciseRowDisplay row) return;

        await _database.ToggleExerciseCompletionAsync(_selectedDate, row.ExerciseName);
        await ReloadAsync();
    }

    private async void OnJumpDateSelected(object sender, DateChangedEventArgs e)
    {
        var newDate = e.NewDate ?? DateTime.Today;
        if (newDate == _selectedDate) return;

        _selectedDate = newDate;
        await BuildCalendarAsync();
        await ReloadAsync();
        OnPropertyChanged(nameof(SelectedDateHeading));
    }

    // Swaps today's whole exercise list with another weekday's (e.g. "move
    // Thursday's workout to Friday" by swapping the two) - a straight swap
    // rather than a one-way move, so it's reversible by running it again and
    // neither day is ever left empty as a side effect.
    private async void OnSwapDayClicked(object sender, EventArgs e)
    {
        var otherDays = Enum.GetValues<DayOfWeek>()
            .Where(d => d != _selectedDate.DayOfWeek)
            .Select(d => d.ToString())
            .ToArray();

        var chosen = await DisplayActionSheet($"Swap {_selectedDate.DayOfWeek}'s exercises with:", "Cancel", null, otherDays);
        if (chosen == "Cancel" || string.IsNullOrEmpty(chosen)) return;

        var otherDayOfWeek = Enum.Parse<DayOfWeek>(chosen);
        await _database.SwapScheduledExercisesAsync((int)_selectedDate.DayOfWeek, (int)otherDayOfWeek);
        await DisplayAlert("Swapped", $"Swapped exercises between {_selectedDate.DayOfWeek} and {otherDayOfWeek}.", "OK");
        await ReloadAsync();
    }

    private async void OnExerciseTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExerciseRowDisplay row) return;

        var navParams = new Dictionary<string, object>
        {
            { "SelectedExerciseName", row.ExerciseName }
        };
        await Shell.Current.GoToAsync(nameof(ProgressionReportPage), navParams);
    }

    private async void OnAddCardioClicked(object sender, EventArgs e)
    {
        var activity = await DisplayPromptAsync("Add Cardio", "What activity?", "Next", "Cancel", "Treadmill Run");
        if (string.IsNullOrWhiteSpace(activity)) return;

        var durationStr = await DisplayPromptAsync("Duration", "How many minutes?", "Next", "Cancel", "30", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(durationStr) || !int.TryParse(durationStr, out int duration) || duration <= 0) return;

        var distanceStr = await DisplayPromptAsync("Distance (optional)", "Distance in km (leave blank to skip):", "Save", "Skip", keyboard: Keyboard.Numeric);
        double? distance = double.TryParse(distanceStr, out double parsedDistance) ? parsedDistance : null;

        await _database.AddCardioLogAsync(_selectedDate, activity.Trim(), duration, distance);
        await ReloadAsync();
    }

    private async void OnRemoveCardioInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.CommandParameter is WorkoutDatabase.CardioLogDisplay cardio)
        {
            await _database.DeleteCardioLogAsync(cardio.Id);
            await ReloadAsync();
        }
    }

    private async void OnEditScheduleClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(EditSchedulePage));
    }

    private async void OnManageLibraryClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(CreateExercisePage));
    }

    private async void OnEditDayTypeExercisesClicked(object sender, EventArgs e)
    {
        var navParams = new Dictionary<string, object> { { "DayOfWeek", _selectedDate.DayOfWeek } };
        await Shell.Current.GoToAsync(nameof(EditDayTypeExercisesPage), navParams);
    }
}

public static class DayTypeColors
{
    // Pulled from the shared resource palette (Resources/Styles/Colors.xaml)
    // rather than hardcoded hex, so these stay in sync with the app's palette
    // instead of silently drifting (these used to be leftover literal values
    // from the old green/gold palette, unchanged by the warm-palette pass).
    public static Color For(DayType dayType) => dayType switch
    {
        DayType.Pull => ResourceColor("Primary", Colors.OrangeRed),
        DayType.Push => ResourceColor("Tertiary", Colors.Orange),
        DayType.Legs => ResourceColor("MealSnacks", Colors.Teal),
        DayType.Upper => ResourceColor("MealLunch", Colors.SaddleBrown),
        DayType.Lower => ResourceColor("MealDinner", Colors.Maroon),
        _ => ResourceColor("Gray400", Colors.Gray)
    };

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;
}

public sealed class WorkoutCalendarDay
{
    public DateTime Date { get; set; }
    public bool IsSelected { get; set; }
    public bool IsToday { get; set; }
    public bool HasLogged { get; set; }
    public DayType DayType { get; set; }

    public string DayNumber => Date.Day.ToString();
    public string DayTypeAbbrev => DayType == DayType.Rest ? string.Empty : DayType.ToString();
    public Color DayTypeColor => DayTypeColors.For(DayType);
}

public sealed class ExerciseRowDisplay
{
    public string ExerciseName { get; set; } = string.Empty;
    public string TargetsDisplay { get; set; } = string.Empty;
    public string PreviousPerformance { get; set; } = string.Empty;
    public bool LoggedToday { get; set; }
    public string LoggedTodayText { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public Color DayTypeColor { get; set; } = Colors.Gray;

    public string CurrentReps { get; set; } = string.Empty;
    public string CurrentWeight { get; set; } = string.Empty;

    public string CompleteButtonText => IsCompleted ? "✓ Completed" : "Complete";
}
