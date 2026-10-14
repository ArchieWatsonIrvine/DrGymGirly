using System.Collections.ObjectModel;
using DrGymGirly.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

public partial class NutritionPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly IServiceProvider _services;

    private DateTime _selectedDate = DateTime.Today;

    public WorkoutDatabase.NutritionSummaryDisplay Summary { get; private set; } = new();
    public ObservableCollection<CalendarDay> CalendarDays { get; } = [];

    // Monday-start week: DayOfWeek has Sunday=0..Saturday=6, so shift by 6
    // first to make Monday the 0-offset day instead of Sunday.
    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public string SelectedDateHeading =>
        _selectedDate == DateTime.Today ? "Today's Nutrition" : _selectedDate.ToString("dddd, d MMMM");

    public NutritionPage(WorkoutDatabase database, IServiceProvider services)
    {
        _database = database;
        _services = services;
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
        Summary = await _database.GetNutritionSummaryForDateAsync(_selectedDate);
        OnPropertyChanged(nameof(Summary));
        RefreshBars();
    }

    // Order matches WorkoutDatabase.MealTotals: Breakfast, Lunch, Dinner, Snacks.
    private static readonly Color[] MealColors =
    [
        ResourceColor("MealBreakfast", Colors.Orange),
        ResourceColor("MealLunch", Colors.Green),
        ResourceColor("MealDinner", Colors.DarkGreen),
        ResourceColor("MealSnacks", Colors.SkyBlue)
    ];

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    private void RefreshBars()
    {
        var totals = Summary.MealTotals;
        BuildSegmentedBar(CalorieBarTrack, totals.Select(m => m.Calories), Summary.CalorieGoal);
        BuildSegmentedBar(ProteinBarTrack, totals.Select(m => m.Protein), Summary.ProteinGoal);
        BuildSegmentedBar(CarbsBarTrack, totals.Select(m => m.Carbs), Summary.CarbsGoal);
        BuildSegmentedBar(FatBarTrack, totals.Select(m => m.Fat), Summary.FatGoal);
    }

    // Fills a track Grid with one colored segment per meal (proportional to
    // that meal's share of the goal) plus a transparent remainder segment, so
    // the bar still reads as "progress toward goal" while also showing which
    // meals it's made up of.
    private static void BuildSegmentedBar(Grid track, IEnumerable<double> mealValues, double goal)
    {
        track.ColumnDefinitions.Clear();
        track.Children.Clear();

        var values = mealValues.ToList();
        var remainder = Math.Max(0, goal - values.Sum());

        for (var i = 0; i < values.Count; i++)
        {
            track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(values[i], 0.0001), GridUnitType.Star)));
            var segment = new BoxView { Color = MealColors[i] };
            Grid.SetColumn(segment, i);
            track.Children.Add(segment);
        }

        track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(remainder, 0.0001), GridUnitType.Star)));
        var remainderSegment = new BoxView { Color = Colors.Transparent };
        Grid.SetColumn(remainderSegment, values.Count);
        track.Children.Add(remainderSegment);
    }

    // Renders the 7 days of the current week (Sunday-Saturday) and marks which
    // of them already have food logged.
    private async Task BuildCalendarAsync()
    {
        var weekStart = StartOfWeek(DateTime.Today);
        const int totalCells = 7;
        var weekEndExclusive = weekStart.AddDays(totalCells);

        var loggedDates = await _database.GetLoggedDatesAsync(weekStart, weekEndExclusive);

        CalendarDays.Clear();
        for (var i = 0; i < totalCells; i++)
        {
            var date = weekStart.AddDays(i);
            CalendarDays.Add(new CalendarDay
            {
                Date = date,
                IsToday = date == DateTime.Today,
                IsSelected = date == _selectedDate,
                HasEntries = loggedDates.Contains(date)
            });
        }
    }

    private async void OnCalendarDayTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not CalendarDay day) return;

        _selectedDate = day.Date;

        await BuildCalendarAsync();
        await ReloadAsync();
        OnPropertyChanged(nameof(SelectedDateHeading));
    }

    private async void OnAddFoodClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string mealTypeText) return;
        if (!Enum.TryParse<MealType>(mealTypeText, out var mealType)) return;

        var searchPage = _services.GetRequiredService<FoodSearchPage>();
        var selection = await searchPage.SelectFoodAsync(Navigation);
        if (selection is null) return;

        if (!selection.CaloriesPer100g.HasValue)
        {
            await DisplayAlert("No Calorie Data", $"Open Food Facts doesn't have calorie data for \"{selection.Name}\". Try a different search result.", "OK");
            return;
        }

        var quantityStr = await DisplayPromptAsync(selection.Name, "How many grams are you having?", "Add", "Cancel", "100", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(quantityStr) || !double.TryParse(quantityStr, out double grams) || grams <= 0) return;

        await _database.AddFoodEntryAsync(
            _selectedDate,
            mealType,
            selection.Name,
            selection.CaloriesPer100g.Value,
            selection.ProteinPer100g ?? 0,
            selection.CarbsPer100g ?? 0,
            selection.FatPer100g ?? 0,
            grams);

        await ReloadAsync();
    }

    private async void OnEditEntryInvoked(object sender, EventArgs e)
    {
        if (sender is not SwipeItem swipeItem || swipeItem.CommandParameter is not WorkoutDatabase.FoodEntryDisplay entry) return;

        var newGramsStr = await DisplayPromptAsync(
            entry.Name,
            "Update the amount in grams:",
            "Save",
            "Cancel",
            entry.QuantityGrams.ToString("N0"),
            -1,
            Keyboard.Numeric);

        if (string.IsNullOrEmpty(newGramsStr) || !double.TryParse(newGramsStr, out double grams) || grams <= 0) return;

        await _database.UpdateFoodEntryQuantityAsync(entry.Id, grams);
        await ReloadAsync();
    }

    private async void OnRemoveEntryInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.CommandParameter is WorkoutDatabase.FoodEntryDisplay entry)
        {
            await _database.DeleteFoodEntryAsync(entry.Id);
            await ReloadAsync();
        }
    }

    private void OnEditGoalsClicked(object sender, EventArgs e)
    {
        CalorieGoalEntry.Text = Summary.CalorieGoal.ToString();
        ProteinGoalEntry.Text = Math.Round(Summary.ProteinGoal).ToString();
        CarbsGoalEntry.Text = Math.Round(Summary.CarbsGoal).ToString();
        FatGoalEntry.Text = Math.Round(Summary.FatGoal).ToString();

        GoalsViewLayout.IsVisible = false;
        GoalsEditLayout.IsVisible = true;
    }

    private void OnCancelGoalsClicked(object sender, EventArgs e)
    {
        GoalsEditLayout.IsVisible = false;
        GoalsViewLayout.IsVisible = true;
    }

    private async void OnSaveGoalsClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(CalorieGoalEntry.Text, out int calorieGoal) || calorieGoal <= 0 ||
            !double.TryParse(ProteinGoalEntry.Text, out double proteinGoal) || proteinGoal < 0 ||
            !double.TryParse(CarbsGoalEntry.Text, out double carbsGoal) || carbsGoal < 0 ||
            !double.TryParse(FatGoalEntry.Text, out double fatGoal) || fatGoal < 0)
        {
            await DisplayAlert("Invalid Input", "Please enter valid numbers for all goals.", "OK");
            return;
        }

        await _database.UpdateNutritionGoalAsync(calorieGoal, proteinGoal, carbsGoal, fatGoal);

        GoalsEditLayout.IsVisible = false;
        GoalsViewLayout.IsVisible = true;

        await ReloadAsync();
    }

    private async void OnCreateMealClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(CreateMealPage));
    }

    private async void OnLogMealClicked(object sender, EventArgs e)
    {
        var meals = await _database.GetMealLibraryAsync();
        if (meals.Count == 0)
        {
            await DisplayAlert("No Meals Found", "You haven't saved any meals yet. Use \"+ Create New Meal\" to build one from real ingredient data.", "OK");
            return;
        }

        var mealOptions = meals.Select(m => $"{m.Name} ({m.TotalCalories} kcal)").ToArray();
        var selectedMeal = await DisplayActionSheet("Log a Saved Meal", "Cancel", null, mealOptions);
        if (selectedMeal == "Cancel" || string.IsNullOrEmpty(selectedMeal)) return;

        var mealIndex = Array.IndexOf(mealOptions, selectedMeal);
        if (mealIndex < 0) return;

        var mealTypeText = await DisplayActionSheet("Add to which meal?", "Cancel", null, "Breakfast", "Lunch", "Dinner", "Snacks");
        if (mealTypeText == "Cancel" || string.IsNullOrEmpty(mealTypeText)) return;
        if (!Enum.TryParse<MealType>(mealTypeText, out var mealType)) return;

        await _database.LogMealAsync(meals[mealIndex].Id, _selectedDate, mealType);
        await ReloadAsync();
    }
}

public sealed class CalendarDay
{
    public DateTime Date { get; set; }
    public bool IsSelected { get; set; }
    public bool IsToday { get; set; }
    public bool HasEntries { get; set; }

    public string DayNumber => Date.Day.ToString();
}
