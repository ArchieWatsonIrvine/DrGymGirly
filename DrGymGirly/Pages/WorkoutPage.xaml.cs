using System.Collections.ObjectModel;
using DrGymGirly.Models;

namespace DrGymGirly;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutDatabase _database;

    private DateTime _selectedDate = DateTime.Today;
    private DayType _currentDayType = DayType.Rest;

    public ObservableCollection<WorkoutCalendarDay> CalendarDays { get; } = [];
    public List<WorkoutSectionRowDisplay> Sections { get; private set; } = [];
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

        DayTypeHeading = dayView.Sections.Count == 0 ? "Rest Day" : string.Join(" + ", dayView.Sections.Select(s => s.WorkoutName));

        var successColor = DayTypeColors.ResourceColor("Success", Colors.Green);
        Sections = dayView.Sections.Select(section =>
        {
            var sectionColor = DayTypeColors.For(section.Category);
            var rows = section.Exercises.Select(e =>
            {
                var lastSet = e.TodaysSets.Count > 0 ? e.TodaysSets[^1] : null;
                return new ExerciseRowDisplay
                {
                    ExerciseName = e.ExerciseName,
                    TargetsDisplay = e.TargetsDisplay,
                    PreviousPerformance = e.PreviousPerformance,
                    TodaysSets = e.TodaysSets,
                    IsCompleted = e.IsCompleted,
                    DayTypeColor = sectionColor,
                    CardTopColor = e.IsCompleted ? successColor : sectionColor,
                    LastReps = lastSet is null ? "-" : $"{lastSet.Reps} reps",
                    LastWeight = lastSet is null ? "-" : $"{lastSet.Weight:0.#} kg"
                };
            });
            return new WorkoutSectionRowDisplay(section.WorkoutName, sectionColor, rows);
        }).ToList();

        CardioLogs = dayView.CardioLogs;

        JumpDatePicker.Date = _selectedDate;

        OnPropertyChanged(nameof(DayTypeHeading));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(CardioLogs));
    }

    // Renders the 7 days of the current week, colored by each day's scheduled
    // DayType, with a dot marking days that already have something logged.
    private async Task BuildCalendarAsync()
    {
        var weekStart = StartOfWeek(DateTime.Today);
        const int totalCells = 7;
        var weekEndExclusive = weekStart.AddDays(totalCells);

        var scheduledWorkouts = await _database.GetScheduledWorkoutsForRangeAsync(weekStart, weekEndExclusive);
        var loggedDates = await _database.GetLoggedWorkoutDatesAsync(weekStart, weekEndExclusive);
        var overriddenDates = await _database.GetOverriddenDatesAsync(weekStart, weekEndExclusive);

        CalendarDays.Clear();
        for (var i = 0; i < totalCells; i++)
        {
            var date = weekStart.AddDays(i);
            scheduledWorkouts.TryGetValue(date, out var workouts);
            workouts ??= [];

            CalendarDays.Add(new WorkoutCalendarDay
            {
                Date = date,
                IsToday = date == DateTime.Today,
                IsSelected = date == _selectedDate,
                HasLogged = loggedDates.Contains(date),
                DayType = workouts.Count > 0 ? workouts[0].Category : DayType.Rest,
                WorkoutCount = workouts.Count,
                IsOverridden = overriddenDates.Contains(date)
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

    // One-off move for this week only: relocates one of the selected date's
    // workouts onto another day within the same calendar week, leaving any
    // other workout scheduled on either day alone. This writes a
    // ScheduleOverrideWorkout list for both dates - next week, both weekdays
    // fall back to their normal WeeklyScheduleWorkout defaults again.
    private async void OnMoveOrAddDayClicked(object sender, EventArgs e)
    {
        var workoutId = await ResolveWorkoutToActOnAsync("Move which workout?");
        if (workoutId is null) return;

        var otherDays = Enum.GetValues<DayOfWeek>()
            .Where(d => d != _selectedDate.DayOfWeek)
            .Select(d => d.ToString())
            .ToArray();

        var chosen = await DisplayActionSheet($"Move {_selectedDate.DayOfWeek}'s workout to which day this week?", "Cancel", null, otherDays);
        if (chosen == "Cancel" || string.IsNullOrEmpty(chosen)) return;

        var otherDayOfWeek = Enum.Parse<DayOfWeek>(chosen);
        var targetDate = StartOfWeek(_selectedDate).AddDays(((int)otherDayOfWeek + 6) % 7);

        await _database.MoveWorkoutOccurrenceAsync(_selectedDate, targetDate, workoutId.Value);
        await DisplayAlert("Moved", $"Moved this week's {_selectedDate.DayOfWeek} workout to {otherDayOfWeek}. Next week is unaffected.", "OK");

        await BuildCalendarAsync();
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

    private static readonly string[] CardioActivityOptions = ["Treadmill Run", "Outdoor Run", "Cycling", "Rowing", "Swimming", "Elliptical", "Walk", "Other"];

    private async void OnAddCardioClicked(object sender, EventArgs e)
    {
        var chosen = await DisplayActionSheet("What activity?", "Cancel", null, CardioActivityOptions);
        if (chosen == "Cancel" || string.IsNullOrEmpty(chosen)) return;

        var activity = chosen;
        if (chosen == "Other")
        {
            activity = await DisplayPromptAsync("Add Cardio", "Name this activity:", "Next", "Cancel");
            if (string.IsNullOrWhiteSpace(activity)) return;
        }

        var durationStr = await DisplayPromptAsync("Duration", "How many minutes?", "Next", "Cancel", "30", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(durationStr) || !int.TryParse(durationStr, out int duration) || duration <= 0) return;

        var distanceStr = await DisplayPromptAsync("Distance (optional)", "Distance in km (leave blank to skip):", "Save", "Skip", keyboard: Keyboard.Numeric);
        double? distance = double.TryParse(distanceStr, out double parsedDistance) ? parsedDistance : null;

        var intensityNames = Enum.GetNames<CardioIntensity>();
        var intensityChoice = await DisplayActionSheet("Intensity?", "Cancel", null, intensityNames);
        if (intensityChoice == "Cancel" || string.IsNullOrEmpty(intensityChoice)) return;
        var intensity = Enum.Parse<CardioIntensity>(intensityChoice);

        await _database.AddCardioLogAsync(_selectedDate, activity.Trim(), duration, distance, intensity);
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

    private async void OnMenuClicked(object sender, EventArgs e)
    {
        var options = new List<string> { "Exercises", "Workouts", "Edit Workout", "Move", "Schedule" };

        // A one-off override (from Move, or from testing before this day's
        // weekly default was set the way it is now) fully replaces this
        // day's weekly defaults for as long as it exists - it never expires
        // on its own, which otherwise looks like "my weekly schedule isn't
        // applying" on whichever specific dates still have one.
        if (await _database.HasOverrideAsync(_selectedDate)) options.Add("Reset to Default");

        var chosen = await DisplayActionSheet("Workout Menu", "Cancel", null, options.ToArray());
        switch (chosen)
        {
            case "Exercises": OnManageLibraryClicked(sender, e); break;
            case "Workouts": OnManageWorkoutsClicked(sender, e); break;
            case "Edit Workout": OnEditWorkoutExercisesClicked(sender, e); break;
            case "Move": OnMoveOrAddDayClicked(sender, e); break;
            case "Schedule": OnEditScheduleClicked(sender, e); break;
            case "Reset to Default": await OnResetToDefaultAsync(); break;
        }
    }

    private async Task OnResetToDefaultAsync()
    {
        await _database.ClearOverrideAsync(_selectedDate);
        await DisplayAlert("Reset", $"{_selectedDate:dddd, d MMMM} now follows your weekly schedule again.", "OK");
        await BuildCalendarAsync();
        await ReloadAsync();
    }

    private async void OnManageLibraryClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(CreateExercisePage));
    }

    private async void OnManageWorkoutsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(WorkoutLibraryPage));
    }

    private async void OnEditWorkoutExercisesClicked(object sender, EventArgs e)
    {
        var workoutId = await ResolveWorkoutToActOnAsync("Edit which workout?");
        if (workoutId is null) return;

        var navParams = new Dictionary<string, object> { { "WorkoutId", workoutId.Value } };
        await Shell.Current.GoToAsync(nameof(EditWorkoutExercisesPage), navParams);
    }

    // Prompts to pick one of the selected date's scheduled workouts (if more
    // than one), since Edit Workout/Move now need to know which one - a plain
    // "Rest Day" alert if none are scheduled at all.
    private async Task<int?> ResolveWorkoutToActOnAsync(string promptTitle)
    {
        var workouts = await ResolveWorkoutsForDateAsync(_selectedDate);
        if (workouts.Count == 0)
        {
            await DisplayAlert("Rest Day", "No workout is assigned to this day. Assign one from Schedule or create one in Workouts first.", "OK");
            return null;
        }
        if (workouts.Count == 1) return workouts[0].Id;

        var names = workouts.Select(w => w.Name).ToArray();
        var chosen = await DisplayActionSheet(promptTitle, "Cancel", null, names);
        if (chosen == "Cancel" || string.IsNullOrEmpty(chosen)) return null;

        return workouts.First(w => w.Name == chosen).Id;
    }

    private async Task<List<Workout>> ResolveWorkoutsForDateAsync(DateTime date)
    {
        var weekStart = StartOfWeek(date);
        var scheduled = await _database.GetScheduledWorkoutsForRangeAsync(weekStart, weekStart.AddDays(7));
        return scheduled.TryGetValue(date.Date, out var workouts) ? workouts : [];
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

    public static Color ResourceColor(string key, Color fallback) =>
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
    public int WorkoutCount { get; set; }
    public bool IsOverridden { get; set; }

    public string DayNumber => Date.Day.ToString();

    // Colored/abbreviated by the first scheduled workout's category; a "+N"
    // suffix hints that more workouts are scheduled that day than just that one.
    public string DayTypeAbbrev => DayType == DayType.Rest
        ? string.Empty
        : DayType + (WorkoutCount > 1 ? $"+{WorkoutCount - 1}" : string.Empty);
    public Color DayTypeColor => DayTypeColors.For(DayType);
}

// A CollectionView group: one scheduled Workout's exercises for the day,
// under its own header. Deriving from List<T> is the standard .NET MAUI
// pattern for CollectionView grouping - the group header template binds
// directly to this object's own properties (WorkoutName/HeaderColor).
public sealed class WorkoutSectionRowDisplay : List<ExerciseRowDisplay>
{
    public string WorkoutName { get; }
    public Color HeaderColor { get; }

    public WorkoutSectionRowDisplay(string workoutName, Color headerColor, IEnumerable<ExerciseRowDisplay> items) : base(items)
    {
        WorkoutName = workoutName;
        HeaderColor = headerColor;
    }
}

public sealed class ExerciseRowDisplay
{
    public string ExerciseName { get; set; } = string.Empty;
    public string TargetsDisplay { get; set; } = string.Empty;
    public string PreviousPerformance { get; set; } = string.Empty;
    public List<WorkoutDatabase.LoggedSetDisplay> TodaysSets { get; set; } = [];
    public bool IsCompleted { get; set; }
    public Color DayTypeColor { get; set; } = Colors.Gray;
    public Color CardTopColor { get; set; } = Colors.Gray;

    // Shown in place of the Reps/Weight entries once this exercise is marked
    // complete - the last set actually logged today, not an editable value.
    public string LastReps { get; set; } = "-";
    public string LastWeight { get; set; } = "-";

    public string CurrentReps { get; set; } = string.Empty;
    public string CurrentWeight { get; set; } = string.Empty;

    public bool LoggedToday => TodaysSets.Count > 0;
    public bool ShowEntry => !IsCompleted;
    public string CompleteButtonText => IsCompleted ? "✓ Completed" : "Complete";
}
