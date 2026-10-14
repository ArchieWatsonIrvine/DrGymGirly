using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrGymGirly.Services;

public sealed record ImportableExercise(string Name, string Category);

// Talks to the real, free, open-source wger exercise database API (wger.de).
// Verified response shape against the live API before writing this:
// GET https://wger.de/api/v2/exerciseinfo/?language=2&limit=N&format=json
public sealed class WgerExerciseApiService
{
    private const string BaseUrl = "https://wger.de/api/v2/exerciseinfo/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public WgerExerciseApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<ImportableExercise>> FetchExercisesAsync(int limit = 40, int? offset = null)
    {
        // wger has ~900+ exercises; pick a random page so repeated imports
        // surface different exercises instead of always the same first batch.
        offset ??= Random.Shared.Next(0, 800);

        var url = $"{BaseUrl}?language=2&limit={limit}&offset={offset}&format=json";
        var json = await _httpClient.GetStringAsync(url);
        var response = JsonSerializer.Deserialize<WgerExerciseInfoResponse>(json, JsonOptions);

        if (response?.Results is null) return [];

        var exercises = new List<ImportableExercise>();
        foreach (var result in response.Results)
        {
            var translation = result.Translations.FirstOrDefault(t => t.Language == 2)
                ?? result.Translations.FirstOrDefault();

            if (translation is null || string.IsNullOrWhiteSpace(translation.Name)) continue;

            exercises.Add(new ImportableExercise(translation.Name.Trim(), result.Category.Name));
        }

        return exercises;
    }

    private sealed class WgerExerciseInfoResponse
    {
        public List<WgerExerciseInfo> Results { get; set; } = [];
    }

    private sealed class WgerExerciseInfo
    {
        public WgerCategory Category { get; set; } = new();
        public List<WgerTranslation> Translations { get; set; } = [];
    }

    private sealed class WgerCategory
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class WgerTranslation
    {
        [JsonPropertyName("language")]
        public int Language { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
