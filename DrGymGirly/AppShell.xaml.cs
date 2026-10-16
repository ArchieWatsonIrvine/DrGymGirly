namespace DrGymGirly;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

        Routing.RegisterRoute(nameof(ProgressionReportPage), typeof(ProgressionReportPage));
        Routing.RegisterRoute(nameof(CreateExercisePage), typeof(CreateExercisePage));
        // WorkoutPage, NutritionPage and BodyWeightPage are NOT registered
        // here - they already have an implicit route from their own
        // <ShellContent Route="..."> entries in AppShell.xaml. Registering
        // them again here too caused Shell to find two matching routes for
        // any path that includes one of those as a segment ("Ambiguous
        // routes matched"), which only surfaced once something got pushed on
        // top of a page reached through it - confirmed via hands-on device
        // testing + adb logcat.
        Routing.RegisterRoute(nameof(EditSchedulePage), typeof(EditSchedulePage));
        Routing.RegisterRoute(nameof(EditWorkoutExercisesPage), typeof(EditWorkoutExercisesPage));
        Routing.RegisterRoute(nameof(WorkoutLibraryPage), typeof(WorkoutLibraryPage));
        Routing.RegisterRoute(nameof(CreateMealPage), typeof(CreateMealPage));
    }
}
