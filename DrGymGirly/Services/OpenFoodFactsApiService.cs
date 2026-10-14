using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrGymGirly.Services;

public sealed record FoodSearchResult(string Code, string Name, string? Quantity);

public sealed record NutrientInfo(
    double? CaloriesPer100g,
    double? ProteinPer100g,
    double? CarbsPer100g,
    double? FatPer100g);

public sealed record ScannedProduct(string Code, string Name, string? Quantity, NutrientInfo Nutrients);

// Talks to the real, free, open-source/open-data Open Food Facts API.
// Verified response shapes against the live API before writing this:
//   Search: GET https://search.openfoodfacts.org/search?q=...&fields=code,product_name,quantity
//   Detail: GET https://world.openfoodfacts.org/api/v2/product/{code}.json
// Nutriment numeric fields (e.g. energy-kcal_100g) are inconsistently typed as
// either a JSON number or a JSON string across different products in OFF's
// crowd-sourced data, so they're read defensively via JsonElement below.
public sealed class OpenFoodFactsApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public OpenFoodFactsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Open Food Facts' own country tagging is incomplete - plenty of
    // genuinely-UK products aren't tagged countries_tags:"en:united-kingdom".
    // Hard-filtering on it (as this used to) silently drops real UK products
    // from results, so this is now just a sort bias: UK-tagged hits float to
    // the top, everything else still shows up underneath.
    private const string UkCountryTag = "en:united-kingdom";

    public async Task<List<FoodSearchResult>> SearchFoodsAsync(string searchTerm, int pageSize = 10)
    {
        var encodedTerm = Uri.EscapeDataString(searchTerm);
        var url = $"https://search.openfoodfacts.org/search?q={encodedTerm}&page_size={pageSize}&fields=code,product_name,quantity,countries_tags";

        var json = await _httpClient.GetStringAsync(url);
        var response = JsonSerializer.Deserialize<SearchResponse>(json, JsonOptions);

        if (response?.Hits is null) return [];

        return response.Hits
            .Where(h => !string.IsNullOrWhiteSpace(h.ProductName) && !string.IsNullOrWhiteSpace(h.Code))
            .OrderByDescending(h => h.CountriesTags?.Contains(UkCountryTag) == true)
            .Select(h => new FoodSearchResult(h.Code!, h.ProductName!.Trim(), h.Quantity))
            .ToList();
    }

    // Looks a product up directly by barcode (from a scan), returning its name
    // and quantity alongside macros so a scan result doesn't need a second
    // round-trip. Returns null if Open Food Facts has no name for this code.
    public async Task<ScannedProduct?> GetProductByBarcodeAsync(string code)
    {
        var url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(code)}.json?fields=code,product_name,quantity,nutriments";
        var json = await _httpClient.GetStringAsync(url);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("product", out var product)) return null;

        var name = product.TryGetProperty("product_name", out var nameElement) ? nameElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var quantity = product.TryGetProperty("quantity", out var quantityElement) ? quantityElement.GetString() : null;

        var nutrients = new NutrientInfo(null, null, null, null);
        if (product.TryGetProperty("nutriments", out var nutriments))
        {
            nutrients = new NutrientInfo(
                ReadNutrient(nutriments, "energy-kcal_100g"),
                ReadNutrient(nutriments, "proteins_100g"),
                ReadNutrient(nutriments, "carbohydrates_100g"),
                ReadNutrient(nutriments, "fat_100g"));
        }

        return new ScannedProduct(code, name!.Trim(), quantity, nutrients);
    }

    public async Task<NutrientInfo> GetNutrientsPer100gAsync(string code)
    {
        var url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(code)}.json";
        var json = await _httpClient.GetStringAsync(url);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("product", out var product))
            return new NutrientInfo(null, null, null, null);
        if (!product.TryGetProperty("nutriments", out var nutriments))
            return new NutrientInfo(null, null, null, null);

        return new NutrientInfo(
            ReadNutrient(nutriments, "energy-kcal_100g"),
            ReadNutrient(nutriments, "proteins_100g"),
            ReadNutrient(nutriments, "carbohydrates_100g"),
            ReadNutrient(nutriments, "fat_100g"));
    }

    private static double? ReadNutrient(JsonElement nutriments, string propertyName)
    {
        if (!nutriments.TryGetProperty(propertyName, out var element)) return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private sealed class SearchResponse
    {
        public List<SearchHit> Hits { get; set; } = [];
    }

    private sealed class SearchHit
    {
        public string? Code { get; set; }

        [JsonPropertyName("product_name")]
        public string? ProductName { get; set; }

        public string? Quantity { get; set; }

        [JsonPropertyName("countries_tags")]
        public List<string>? CountriesTags { get; set; }
    }
}
