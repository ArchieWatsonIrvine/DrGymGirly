using ZXing.Net.Maui;

namespace DrGymGirly;

public partial class BarcodeScanPage : ContentPage
{
    private TaskCompletionSource<string?>? _completion;

    public BarcodeScanPage()
    {
        InitializeComponent();

        // BarcodeReaderOptions.Formats defaults to 0 (no formats enabled) -
        // without this, the camera preview runs but never detects anything,
        // confirmed via hands-on device testing. These cover the barcode
        // types that appear on grocery packaging.
        BarcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.Ean13 | BarcodeFormat.Ean8 | BarcodeFormat.UpcA | BarcodeFormat.UpcE | BarcodeFormat.Code128,
            AutoRotate = true,
            TryHarder = true
        };
    }

    // Pushed onto the caller's own navigation stack (FoodSearchPage's modal
    // NavigationPage) rather than as a second nested modal - stacking two
    // independent modal windows and popping them back-to-back races on
    // Android's window teardown ("Attempting to destroy the window while
    // drawing!", leaving a duplicate orphaned MainActivity window behind).
    // Returns the scanned/typed barcode, or null if cancelled/dismissed.
    public async Task<string?> ScanAsync(INavigation callerNavigation)
    {
        _completion = new TaskCompletionSource<string?>();
        await callerNavigation.PushAsync(this);
        return await _completion.Task;
    }

    // Not OnDisappearing: it fires DURING Navigation.PopAsync() below, and
    // TrySetResult only ever wins once - a blanket null safety-net there
    // would race the real value and can discard it depending on timing.
    // The hardware back button is the one dismiss path not already covered
    // by an explicit completion handler below.
    protected override bool OnBackButtonPressed()
    {
        _completion?.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_completion is null || _completion.Task.IsCompleted) return;
            // Pop before completing: the caller's continuation can start the
            // instant TrySetResult runs, racing this page's own in-flight pop
            // and corrupting the native window if it hasn't finished yet.
            await Navigation.PopAsync();
            _completion.TrySetResult(value);
        });
    }

    private async void OnManualBarcodeSubmitted(object sender, EventArgs e)
    {
        var code = ManualBarcodeEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;

        await Navigation.PopAsync();
        _completion?.TrySetResult(code);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
        _completion?.TrySetResult(null);
    }
}
