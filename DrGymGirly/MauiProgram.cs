using System.Net.Http.Headers;
using DrGymGirly.Services;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace DrGymGirly;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// FIX 5: sqlite-net-pcl needs its native provider registered before the
		// first SQLiteConnection is touched, otherwise SQLiteConnection's static
		// constructor throws ("The type initializer for 'SQLite.SQLiteConnection'
		// threw an exception.") the first time the database is opened.
		SQLitePCL.Batteries_V2.Init();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Poppins-Regular.ttf", "PoppinsRegular");
				fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
				fonts.AddFont("Poppins-Bold.ttf", "PoppinsBold");
			});

		builder.Services.AddSingleton<WorkoutDatabase>();
		builder.Services.AddSingleton<AppShell>();

		// Shared HttpClient for the two free, open APIs this app talks to
		// (wger for exercises, Open Food Facts for nutrition). A descriptive
		// User-Agent is required by Open Food Facts' API usage policy.
		builder.Services.AddSingleton(_ =>
		{
			var httpClient = new HttpClient();
			httpClient.DefaultRequestHeaders.UserAgent.Add(
				new ProductInfoHeaderValue("DrGymGirly", "1.0"));
			return httpClient;
		});
		builder.Services.AddSingleton<WgerExerciseApiService>();
		builder.Services.AddSingleton<OpenFoodFactsApiService>();

		// FIX 4: Shell route navigation (GoToAsync) resolves pages through this
		// container first and only falls back to a parameterless constructor if
		// a type isn't registered. These pages all require WorkoutDatabase in
		// their constructor and have no parameterless one, so without these
		// registrations navigating to them threw MissingMethodException.
		builder.Services.AddTransient<WorkoutPage>();
		builder.Services.AddTransient<EditSchedulePage>();
		builder.Services.AddTransient<EditDayTypeExercisesPage>();
		builder.Services.AddTransient<CreateExercisePage>();
		builder.Services.AddTransient<ExercisePickerPage>();
		builder.Services.AddTransient<ProgressionReportPage>();
		builder.Services.AddTransient<NutritionPage>();
		builder.Services.AddTransient<CreateMealPage>();
		builder.Services.AddTransient<FoodSearchPage>();
		builder.Services.AddTransient<BarcodeScanPage>();
		builder.Services.AddTransient<AddCustomFoodPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
