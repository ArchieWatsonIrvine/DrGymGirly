using DrGymGirly.Services;
using Plugin.Maui.OCR;

namespace DrGymGirly;

public partial class AddCustomFoodPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly IOcrService _ocr;

    private TaskCompletionSource<FoodSelection?>? _completion;
    private string? _barcode;

    public AddCustomFoodPage(WorkoutDatabase database, IOcrService ocr)
    {
        _database = database;
        _ocr = ocr;
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

    // Best-effort: label layouts vary a lot and OCR isn't perfect, so this
    // only ever pre-fills fields it found - it never blocks you from typing
    // the rest manually, and you should always check the result against the
    // actual packaging before saving.
    private async void OnScanLabelClicked(object sender, EventArgs e)
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not Supported", "This device can't take photos.", "OK");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo is null) return;

            using var stream = await photo.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var imageData = memoryStream.ToArray();

            await _ocr.InitAsync();
            var result = await _ocr.RecognizeTextAsync(imageData, tryHard: true);

            if (!result.Success || result.Lines.Count == 0)
            {
                await DisplayAlert("No Text Found", "Couldn't read any text from that photo - try again with the nutrition panel filling the frame in good light.", "OK");
                return;
            }

            var parsed = NutritionLabelParser.Parse(result.Lines);

            if (parsed.CaloriesPer100g.HasValue) CaloriesEntry.Text = parsed.CaloriesPer100g.Value.ToString("0.#");
            if (parsed.ProteinPer100g.HasValue) ProteinEntry.Text = parsed.ProteinPer100g.Value.ToString("0.#");
            if (parsed.CarbsPer100g.HasValue) CarbsEntry.Text = parsed.CarbsPer100g.Value.ToString("0.#");
            if (parsed.FatPer100g.HasValue) FatEntry.Text = parsed.FatPer100g.Value.ToString("0.#");

            var foundCount = new[] { parsed.CaloriesPer100g, parsed.ProteinPer100g, parsed.CarbsPer100g, parsed.FatPer100g }.Count(v => v.HasValue);
            await DisplayAlert(
                "Label Scanned",
                foundCount > 0
                    ? $"Found {foundCount} of 4 values. Please check them against the packaging before saving - label scanning isn't always perfect."
                    : "Couldn't confidently find calories/protein/carbs/fat in that photo. Please enter them manually.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Scan Failed", $"Could not scan the label: {ex.Message}", "OK");
        }
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
