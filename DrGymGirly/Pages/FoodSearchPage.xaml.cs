using System.Collections.ObjectModel;
using DrGymGirly.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

public sealed record FoodSelection(
    string Name,
    double? CaloriesPer100g,
    double? ProteinPer100g,
    double? CarbsPer100g,
    double? FatPer100g);

public partial class FoodSearchPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly OpenFoodFactsApiService _foodApi;
    private readonly IServiceProvider _services;

    private TaskCompletionSource<FoodSelection?>? _completion;
    private CancellationTokenSource? _debounceCts;

    public ObservableCollection<FoodSearchItem> Results { get; } = [];

    public FoodSearchPage(WorkoutDatabase database, OpenFoodFactsApiService foodApi, IServiceProvider services)
    {
        _database = database;
        _foodApi = foodApi;
        _services = services;
        InitializeComponent();
        BindingContext = this;
    }

    // Pushes this page modally on the caller's navigation stack and returns the
    // chosen food, or null if the user cancelled/dismissed without picking one.
    public async Task<FoodSelection?> SelectFoodAsync(INavigation callerNavigation)
    {
        _completion = new TaskCompletionSource<FoodSelection?>();
        await callerNavigation.PushModalAsync(new NavigationPage(this));
        var result = await _completion.Task;

        // The awaited PopModalAsync inside each completion handler above
        // resolves before Android's native window has actually finished
        // tearing down ("Attempting to destroy the window while drawing!").
        // Callers typically show another native dialog (DisplayAlert/
        // DisplayPromptAsync) immediately after this returns, and that races
        // the still-finishing teardown - the new dialog renders but its
        // buttons silently stop responding. Confirmed via hands-on device
        // testing: a short buffer here reliably avoids it.
        await Task.Delay(300);
        return result;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ShowRecentAsync();
    }

    // Not OnDisappearing: it also fires when BarcodeScanPage/AddCustomFoodPage
    // get pushed on top of this page (same modal stack) since this page merely
    // loses focus rather than being dismissed - that would complete this with
    // null the instant scanning/manual entry opens, long before either can
    // produce a real result. The hardware back button is the one dismiss path
    // not already covered by an explicit completion handler below.
    protected override bool OnBackButtonPressed()
    {
        _completion?.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    private async Task ShowRecentAsync()
    {
        var recents = await _database.GetRecentFoodSelectionsAsync();
        Results.Clear();
        foreach (var r in recents)
        {
            Results.Add(new FoodSearchItem
            {
                Code = r.Code,
                Name = r.Name,
                Quantity = r.Quantity,
                IsRecent = true
            });
        }

        BeginLoadingNutrients(Results.ToList());
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceCts?.Cancel();

        var term = e.NewTextValue?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(term))
        {
            _ = ShowRecentAsync();
            return;
        }

        if (term.Length < 2) return;

        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSearchAsync(term, cts.Token);
    }

    private async Task DebouncedSearchAsync(string term, CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        List<FoodSearchResult> apiResults = [];
        try
        {
            apiResults = await _foodApi.SearchFoodsAsync(term);
        }
        catch
        {
            // Fail silently here; custom-library matches below still show,
            // and the user can keep typing or retry the API search.
        }

        // Surfaces previously saved custom foods (manual entries / scan
        // misses) alongside Open Food Facts results, so you never have to
        // retype macros for something you've already logged once.
        var customMatches = await _database.SearchCustomFoodItemsAsync(term);

        if (token.IsCancellationRequested) return;

        Results.Clear();
        foreach (var c in customMatches)
        {
            Results.Add(new FoodSearchItem
            {
                Code = c.Barcode ?? string.Empty,
                Name = c.Name,
                IsCustom = true,
                CaloriesPer100g = c.CaloriesPer100g,
                ProteinPer100g = c.ProteinPer100g,
                CarbsPer100g = c.CarbsPer100g,
                FatPer100g = c.FatPer100g,
                NutrientsLoaded = true
            });
        }

        foreach (var r in apiResults)
        {
            Results.Add(new FoodSearchItem
            {
                Code = r.Code,
                Name = r.Name,
                Quantity = r.Quantity,
                IsRecent = false
            });
        }

        BeginLoadingNutrients(Results.Where(x => !x.IsCustom).ToList());
    }

    // Live search and the recent-items list only ever carry name/quantity from
    // their source (search-a-licious doesn't return nutriments, and recents are
    // kept lightweight), so macros for every row are fetched in the background
    // and the row is refreshed in place as each one resolves.
    private void BeginLoadingNutrients(List<FoodSearchItem> items)
    {
        foreach (var item in items)
        {
            _ = LoadNutrientsAsync(item);
        }
    }

    private async Task LoadNutrientsAsync(FoodSearchItem item)
    {
        try
        {
            var nutrients = await _foodApi.GetNutrientsPer100gAsync(item.Code);
            item.CaloriesPer100g = nutrients.CaloriesPer100g;
            item.ProteinPer100g = nutrients.ProteinPer100g;
            item.CarbsPer100g = nutrients.CarbsPer100g;
            item.FatPer100g = nutrients.FatPer100g;
        }
        catch
        {
            // Leave as "no data" rather than fail the whole list.
        }
        finally
        {
            item.NutrientsLoaded = true;

            var index = Results.IndexOf(item);
            if (index >= 0)
            {
                // Reassigning at the same index still raises a Replace
                // notification, which is what refreshes this row's binding.
                Results[index] = item;
            }
        }
    }

    private async void OnResultTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not FoodSearchItem item) return;

        if (item.IsCustom)
        {
            // Already fully known - no Open Food Facts round-trip, and
            // nothing new to record into recents.
            var customSelection = new FoodSelection(
                item.Name, item.CaloriesPer100g, item.ProteinPer100g, item.CarbsPer100g, item.FatPer100g);
            await Navigation.PopModalAsync();
            _completion?.TrySetResult(customSelection);
            return;
        }

        // The background prefetch usually already has these; only re-fetch if
        // the user tapped before it resolved.
        var nutrients = item.NutrientsLoaded
            ? new NutrientInfo(item.CaloriesPer100g, item.ProteinPer100g, item.CarbsPer100g, item.FatPer100g)
            : await _foodApi.GetNutrientsPer100gAsync(item.Code);

        await _database.RecordFoodSelectionAsync(item.Code, item.Name, nutrients.CaloriesPer100g, item.Quantity);

        // Pop before completing: the caller's continuation (e.g.
        // DisplayPromptAsync, or another pop) can start the instant
        // TrySetResult runs, racing this page's own in-flight modal pop and
        // corrupting the native window ("Attempting to destroy the window
        // while drawing!") if it hasn't finished yet.
        var selection = new FoodSelection(
            item.Name,
            nutrients.CaloriesPer100g,
            nutrients.ProteinPer100g,
            nutrients.CarbsPer100g,
            nutrients.FatPer100g);
        await Navigation.PopModalAsync();
        _completion?.TrySetResult(selection);
    }

    private async void OnScanBarcodeClicked(object sender, EventArgs e)
    {
        var scanPage = _services.GetRequiredService<BarcodeScanPage>();
        var barcode = await scanPage.ScanAsync(Navigation);
        if (string.IsNullOrWhiteSpace(barcode)) return;

        await HandleBarcodeAsync(barcode);
    }

    private async Task HandleBarcodeAsync(string barcode)
    {
        // A barcode already saved from a previous scan-miss/manual add skips
        // straight to a result - no re-asking for macros on repeat scans.
        var customMatch = await _database.GetCustomFoodItemByBarcodeAsync(barcode);
        if (customMatch is not null)
        {
            var customSelection = new FoodSelection(
                customMatch.Name, customMatch.CaloriesPer100g, customMatch.ProteinPer100g, customMatch.CarbsPer100g, customMatch.FatPer100g);
            await Navigation.PopModalAsync();
            _completion?.TrySetResult(customSelection);
            return;
        }

        ScannedProduct? offProduct = null;
        try
        {
            offProduct = await _foodApi.GetProductByBarcodeAsync(barcode);
        }
        catch
        {
            // Treat a lookup failure the same as "not found" below.
        }

        if (offProduct is not null && offProduct.Nutrients.CaloriesPer100g.HasValue)
        {
            await _database.RecordFoodSelectionAsync(offProduct.Code, offProduct.Name, offProduct.Nutrients.CaloriesPer100g, offProduct.Quantity);

            var offSelection = new FoodSelection(
                offProduct.Name,
                offProduct.Nutrients.CaloriesPer100g,
                offProduct.Nutrients.ProteinPer100g,
                offProduct.Nutrients.CarbsPer100g,
                offProduct.Nutrients.FatPer100g);
            await Navigation.PopModalAsync();
            _completion?.TrySetResult(offSelection);
            return;
        }

        // Not in Open Food Facts (or no macros there) - fall into manual
        // entry, pre-filled with the barcode so a future scan recognizes it.
        await OpenManualEntryAsync(barcode, offProduct?.Name);
    }

    private async void OnAddManuallyClicked(object sender, TappedEventArgs e) => await OpenManualEntryAsync(null, null);

    private async Task OpenManualEntryAsync(string? barcode, string? prefilledName)
    {
        var manualPage = _services.GetRequiredService<AddCustomFoodPage>();
        var result = await manualPage.ShowAsync(Navigation, barcode, prefilledName);
        if (result is null) return;

        await Navigation.PopModalAsync();
        _completion?.TrySetResult(result);
    }
}

public sealed class FoodSearchItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Quantity { get; set; }
    public bool IsRecent { get; set; }
    public bool IsCustom { get; set; }

    public double? CaloriesPer100g { get; set; }
    public double? ProteinPer100g { get; set; }
    public double? CarbsPer100g { get; set; }
    public double? FatPer100g { get; set; }
    public bool NutrientsLoaded { get; set; }

    public string SubtitleText =>
        IsCustom
            ? "Your saved food"
            : IsRecent
                ? (string.IsNullOrWhiteSpace(Quantity) ? "Recently used" : $"Recently used · {Quantity}")
                : (Quantity ?? string.Empty);

    public string MacrosDisplay =>
        !NutrientsLoaded
            ? "Loading macros…"
            : CaloriesPer100g.HasValue
                ? $"Per 100g: {Math.Round(CaloriesPer100g.Value)} kcal · P {Round1(ProteinPer100g)}g · C {Round1(CarbsPer100g)}g · F {Round1(FatPer100g)}g"
                : "No nutrition data available";

    private static double Round1(double? value) => Math.Round(value ?? 0, 1);
}
