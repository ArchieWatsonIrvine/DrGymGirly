namespace DrGymGirly;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

        Routing.RegisterRoute(nameof(ProgressionReportPage), typeof(ProgressionReportPage));
        Routing.RegisterRoute(nameof(CreateExercisePage), typeof(CreateExercisePage));
        // WorkoutPage is NOT registered here - it already has an implicit
        // route from its <ShellContent Route="WorkoutPage"> in AppShell.xaml.
        // Registering it again here too caused Shell to find two matching
        // routes for any path that includes "WorkoutPage" as a segment
        // ("Ambiguous routes matched"), which only surfaced once something
        // got pushed on top of a page reached through it - confirmed via
        // hands-on device testing + adb logcat.
        Routing.RegisterRoute(nameof(EditSchedulePage), typeof(EditSchedulePage));
        Routing.RegisterRoute(nameof(EditDayTypeExercisesPage), typeof(EditDayTypeExercisesPage));
        Routing.RegisterRoute(nameof(CreateMealPage), typeof(CreateMealPage));
    }
}
