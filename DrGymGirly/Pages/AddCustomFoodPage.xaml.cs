namespace DrGymGirly;

public partial class AddCustomFoodPage : ContentPage
{
    private readonly WorkoutDatabase _database;

    private TaskCompletionSource<FoodSelection?>? _completion;
    private string? _barcode;

    public AddCustomFoodPage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
    }

    // Pushed onto the caller's own navigation stack (FoodSearchPage's modal
    // NavigationPage) rather than as a second nested modal - see the same
    // note on BarcodeScanPage.ScanAsync. Pre-filled with a barcode/name if
    // this came from a failed scan lookup. Returns the saved food, or null if
    // the user cancelled/dismissed without saving.
    public async Task<FoodSelection?> ShowAsync(INavigation callerNavigation, string? barcode = null, string? prefilledName = null)
    {
        _barcode = barcode;
        _completion = new TaskCompletionSource<FoodSelection?>();

        NameEntry.Text = prefilledName;
        BarcodeLabel.Text = barcode is null ? string.Empty : $"Barcode {barcode} - not in Open Food Facts yet.";

        await callerNavigation.PushAsync(this);
        return await _completion.Task;
    }

    // Not OnDisappearing: it fires DURING Navigation.PopAsync() below, and
    // TrySetResult only ever wins once - a blanket null safety-net there
    // would race the real value and can discard it depending on timing.
    protected override bool OnBackButtonPressed()
    {
        _completion?.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            !double.TryParse(CaloriesEntry.Text, out var calories) || calories < 0 ||
            !double.TryParse(ProteinEntry.Text, out var protein) || protein < 0 ||
            !double.TryParse(CarbsEntry.Text, out var carbs) || carbs < 0 ||
            !double.TryParse(FatEntry.Text, out var fat) || fat < 0)
        {
            await DisplayAlert("Incomplete", "Please enter a name and valid per-100g values for calories, protein, carbs, and fat.", "OK");
            return;
        }

        await _database.SaveCustomFoodItemAsync(_barcode, name, calories, protein, carbs, fat);

        // Pop before completing: the caller's continuation can start the
        // instant TrySetResult runs, racing this page's own in-flight pop
        // and corrupting the native window if it hasn't finished yet.
        var selection = new FoodSelection(name, calories, protein, carbs, fat);
        await Navigation.PopAsync();
        _completion?.TrySetResult(selection);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
        _completion?.TrySetResult(null);
    }
}
