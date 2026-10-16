using System.Collections.ObjectModel;
using DrGymGirly.Models;

namespace DrGymGirly;

public partial class ExercisePickerPage : ContentPage
{
    private TaskCompletionSource<Exercise?>? _completion;
    private List<Exercise> _allExercises = [];
    private List<Exercise> _relevantExercises = [];
    private bool _showingAll;

    public ObservableCollection<Exercise> FilteredExercises { get; } = [];
    public string FilterHint { get; private set; } = string.Empty;

    public ExercisePickerPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    // Pushed onto the caller's own navigation stack, matching BarcodeScanPage's
    // convention - returns the chosen exercise, or null if cancelled/dismissed.
    // Defaults to exercises relevant to dayType (DayTypeCategories) with a
    // "Show All"/"Just This Day" toggle as the escape hatch to the full
    // library, same intent as EditWorkoutExercisesPage's old action-sheet flow.
    public async Task<Exercise?> SelectExerciseAsync(INavigation callerNavigation, List<Exercise> allExercises, DayType dayType)
    {
        _allExercises = allExercises;

        var relevantCategories = DayTypeCategories.GetRelevant(dayType);
        _relevantExercises = relevantCategories is null
            ? allExercises
            : allExercises.Where(x => relevantCategories.Contains(x.Category)).ToList();
        if (_relevantExercises.Count == 0) _relevantExercises = allExercises;

        _showingAll = _relevantExercises.Count == allExercises.Count;
        UpdateShowAllButton();
        ApplyFilter(string.Empty);

        // Pushed as its own modal NavigationPage rather than onto the
        // caller's stack directly: EditWorkoutExercisesPage was itself
        // reached via Shell.GoToAsync, so its Navigation is Shell's own
        // route-tracked stack - popping a plain PushAsync page back off that
        // stack makes Shell try to resync its current route internally,
        // which throws "Ambiguous routes matched" (a real Shell bug, hit via
        // hands-on device testing). A separate modal NavigationPage never
        // touches Shell's tracked stack at all, same as FoodSearchPage.
        _completion = new TaskCompletionSource<Exercise?>();
        await callerNavigation.PushModalAsync(new NavigationPage(this));
        var result = await _completion.Task;

        // EditWorkoutExercisesPage immediately opens DisplayPromptAsync after
        // this returns - same race FoodSearchPage.SelectFoodAsync documents:
        // the next native dialog can render before this page's pop has
        // actually finished tearing down, leaving its buttons unresponsive
        // (or worse, racing the window teardown outright). Matching that
        // page's fix with the same buffer.
        await Task.Delay(300);
        return result;
    }

    protected override bool OnBackButtonPressed()
    {
        _completion?.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(e.NewTextValue ?? string.Empty);

    private void OnToggleShowAllClicked(object sender, EventArgs e)
    {
        _showingAll = !_showingAll;
        UpdateShowAllButton();
        ApplyFilter(SearchBox.Text ?? string.Empty);
    }

    private void UpdateShowAllButton()
    {
        var hiddenCount = _allExercises.Count - _relevantExercises.Count;

        ShowAllButton.Text = _showingAll ? "Just This Workout" : "Show All";
        ShowAllButton.IsVisible = hiddenCount > 0;

        FilterHint = _showingAll || hiddenCount == 0
            ? $"Showing all {_allExercises.Count} exercises"
            : $"Showing exercises for this workout's category ({hiddenCount} more hidden)";
        OnPropertyChanged(nameof(FilterHint));
    }

    private void ApplyFilter(string term)
    {
        var source = _showingAll ? _allExercises : _relevantExercises;
        var trimmed = term.Trim();

        var matches = string.IsNullOrEmpty(trimmed)
            ? source
            : source.Where(x => x.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredExercises.Clear();
        foreach (var exercise in matches.OrderBy(x => x.Name))
        {
            FilteredExercises.Add(exercise);
        }
    }

    private async void OnExerciseTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not Exercise exercise) return;

        await Navigation.PopModalAsync();
        _completion?.TrySetResult(exercise);
    }
}
