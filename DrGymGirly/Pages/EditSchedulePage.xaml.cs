using DrGymGirly.Models;

namespace DrGymGirly;

public partial class EditSchedulePage : ContentPage
{
    private readonly WorkoutDatabase _database;

    public EditSchedulePage(WorkoutDatabase database)
    {
        _database = database;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var schedule = await _database.GetWeeklyScheduleAsync();

        SetPicker(MondayPicker, schedule, DayOfWeek.Monday);
        SetPicker(TuesdayPicker, schedule, DayOfWeek.Tuesday);
        SetPicker(WednesdayPicker, schedule, DayOfWeek.Wednesday);
        SetPicker(ThursdayPicker, schedule, DayOfWeek.Thursday);
        SetPicker(FridayPicker, schedule, DayOfWeek.Friday);
        SetPicker(SaturdayPicker, schedule, DayOfWeek.Saturday);
        SetPicker(SundayPicker, schedule, DayOfWeek.Sunday);
    }

    private static void SetPicker(Picker picker, Dictionary<DayOfWeek, DayType> schedule, DayOfWeek day)
    {
        var dayType = schedule.TryGetValue(day, out var value) ? value : DayType.Rest;
        picker.SelectedIndex = (int)dayType;
    }

    private static DayType GetSelected(Picker picker) => (DayType)Math.Max(picker.SelectedIndex, 0);

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var schedule = new Dictionary<DayOfWeek, DayType>
        {
            [DayOfWeek.Monday] = GetSelected(MondayPicker),
            [DayOfWeek.Tuesday] = GetSelected(TuesdayPicker),
            [DayOfWeek.Wednesday] = GetSelected(WednesdayPicker),
            [DayOfWeek.Thursday] = GetSelected(ThursdayPicker),
            [DayOfWeek.Friday] = GetSelected(FridayPicker),
            [DayOfWeek.Saturday] = GetSelected(SaturdayPicker),
            [DayOfWeek.Sunday] = GetSelected(SundayPicker)
        };

        await _database.UpdateWeeklyScheduleAsync(schedule);
        await DisplayAlert("Saved", "Your weekly schedule has been updated.", "OK");
        await Shell.Current.GoToAsync("..");
    }
}
