using System.Collections.ObjectModel;
using DrGymGirly.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

public partial class CreateMealPage : ContentPage
{
    private readonly WorkoutDatabase _database;
    private readonly IServiceProvider _services;

    public ObservableCollection<IngredientLineDraft> CurrentDraftIngredients { get; set; } = [];

    public CreateMealPage(WorkoutDatabase database, IServiceProvider services)
    {
        _database = database;
        _services = services;
        InitializeComponent();
        BindingContext = this;
    }

    private async void OnAddIngredientClicked(object sender, EventArgs e)
    {
        var searchPage = _services.GetRequiredService<FoodSearchPage>();
        var selection = await searchPage.SelectFoodAsync(Navigation);
        if (selection is null) return;

        if (!selection.CaloriesPer100g.HasValue)
        {
            await DisplayAlert("No Calorie Data", $"Open Food Facts doesn't have calorie data for \"{selection.Name}\". Try a different search result.", "OK");
            return;
        }

        var quantityStr = await DisplayPromptAsync(selection.Name, "How many grams are you using?", "Add", "Cancel", "100", -1, Keyboard.Numeric);
        if (string.IsNullOrEmpty(quantityStr) || !double.TryParse(quantityStr, out double grams) || grams <= 0) return;

        CurrentDraftIngredients.Add(new IngredientLineDraft
        {
            Name = selection.Name,
            CaloriesPer100g = selection.CaloriesPer100g.Value,
            ProteinPer100g = selection.ProteinPer100g ?? 0,
            CarbsPer100g = selection.CarbsPer100g ?? 0,
            FatPer100g = selection.FatPer100g ?? 0,
            QuantityGrams = grams
        });
    }

    private async void OnSaveMealClicked(object sender, EventArgs e)
    {
        var title = MealTitleEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title) || CurrentDraftIngredients.Count == 0)
        {
            await DisplayAlert("Incomplete Meal", "Please provide a meal name and add at least one ingredient.", "OK");
            return;
        }

        var meal = new Meal { Name = title };
        var ingredients = CurrentDraftIngredients.Select(i => new MealIngredient
        {
            Name = i.Name,
            CaloriesPer100g = i.CaloriesPer100g,
            ProteinPer100g = i.ProteinPer100g,
            CarbsPer100g = i.CarbsPer100g,
            FatPer100g = i.FatPer100g,
            QuantityGrams = i.QuantityGrams
        }).ToList();

        await _database.SaveCompleteMealAsync(meal, ingredients);
        await DisplayAlert("Success", $"Meal '{title}' saved to your library!", "OK");
        await Shell.Current.GoToAsync("..");
    }

    public class IngredientLineDraft
    {
        public string Name { get; set; } = string.Empty;
        public double CaloriesPer100g { get; set; }
        public double ProteinPer100g { get; set; }
        public double CarbsPer100g { get; set; }
        public double FatPer100g { get; set; }
        public double QuantityGrams { get; set; }

        public double Protein => Math.Round(ProteinPer100g * QuantityGrams / 100.0, 1);
        public double Carbs => Math.Round(CarbsPer100g * QuantityGrams / 100.0, 1);
        public double Fat => Math.Round(FatPer100g * QuantityGrams / 100.0, 1);

        public string QuantityDisplay => $"{QuantityGrams:N0} g";
        public string CaloriesDisplay => $"{Math.Round(CaloriesPer100g * QuantityGrams / 100.0)} kcal";
        public string MacrosDisplay => $"P {Protein:0.#}g · C {Carbs:0.#}g · F {Fat:0.#}g";
    }
}
