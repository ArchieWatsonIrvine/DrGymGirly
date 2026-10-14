using DrGymGirly.Models;
using SQLite;

namespace DrGymGirly;

public sealed class WorkoutDatabase
{
    private const string DemoUserId = "demo-user";
    private SQLiteAsyncConnection? _connection;

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null) return _connection;

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "drgymgirly.db3");

        var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;
        _connection = new SQLiteAsyncConnection(databasePath, flags);

        await _connection.CreateTableAsync<Exercise>();
        await _connection.CreateTableAsync<FoodEntry>();
        await _connection.CreateTableAsync<Meal>();
        await _connection.CreateTableAsync<MealIngredient>();
        await _connection.CreateTableAsync<RecentFoodSelection>();
        await _connection.CreateTableAsync<CustomFoodItem>();
        await _connection.CreateTableAsync<NutritionGoal>();
        await _connection.CreateTableAsync<WeeklyScheduleDay>();
        await _connection.CreateTableAsync<ScheduledExercise>();
        await _connection.CreateTableAsync<ExerciseCompletion>();
        await _connection.CreateTableAsync<ExerciseLog>();
        await _connection.CreateTableAsync<CardioLog>();

        await SeedWeeklyScheduleAsync(_connection);
        await MigrateDayTypeExercisesAsync(_connection);
        return _connection;
    }

    // One-time migration from the old DayType-keyed routine table to the new
    // per-weekday ScheduledExercises table: every weekday sharing a DayType
    // (e.g. Tuesday and Thursday both "Upper") gets its own copy of that
    // DayType's old exercise list as a starting point, which can then be
    // edited independently per weekday going forward. Guarded by the old
    // table's existence, so this only ever runs once per install.
    private static async Task MigrateDayTypeExercisesAsync(SQLiteAsyncConnection connection)
    {
        var oldTableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DayTypeExercises'");
        if (oldTableExists == 0) return;

        var oldRows = await connection.QueryAsync<OldDayTypeExerciseRow>("SELECT * FROM DayTypeExercises");
        var schedule = await connection.Table<WeeklyScheduleDay>().ToListAsync();

        var migrated = new List<ScheduledExercise>();
        foreach (var scheduleRow in schedule)
        {
            foreach (var old in oldRows.Where(r => r.DayType == (int)scheduleRow.DayType))
            {
                migrated.Add(new ScheduledExercise
                {
                    DayOfWeek = scheduleRow.DayOfWeek,
                    DisplayOrder = old.DisplayOrder,
                    ExerciseName = old.ExerciseName,
                    TargetSets = old.TargetSets,
                    TargetReps = old.TargetReps,
                    TargetWeightKg = old.TargetWeightKg
                });
            }
        }

        if (migrated.Count > 0)
        {
            await connection.InsertAllAsync(migrated);
        }

        await connection.ExecuteAsync("DROP TABLE DayTypeExercises");
    }

    private sealed class OldDayTypeExerciseRow
    {
        public int Id { get; set; }
        public int DayType { get; set; }
        public int DisplayOrder { get; set; }
        public string ExerciseName { get; set; } = string.Empty;
        public int TargetSets { get; set; }
        public int TargetReps { get; set; }
        public double TargetWeightKg { get; set; }
    }

    private static async Task SeedWeeklyScheduleAsync(SQLiteAsyncConnection connection)
    {
        if (await connection.Table<WeeklyScheduleDay>().CountAsync() > 0) return;

        await connection.InsertAllAsync(new[]
        {
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Sunday, DayType = DayType.Legs },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Monday, DayType = DayType.Pull },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Tuesday, DayType = DayType.Push },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Wednesday, DayType = DayType.Rest },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Thursday, DayType = DayType.Legs },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Friday, DayType = DayType.Pull },
            new WeeklyScheduleDay { DayOfWeek = (int)DayOfWeek.Saturday, DayType = DayType.Push }
        });
    }

    public async Task<Dictionary<DayOfWeek, DayType>> GetWeeklyScheduleAsync()
    {
        var connection = await GetConnectionAsync();
        var rows = await connection.Table<WeeklyScheduleDay>().ToListAsync();
        return rows.ToDictionary(r => (DayOfWeek)r.DayOfWeek, r => r.DayType);
    }

    public async Task UpdateWeeklyScheduleAsync(Dictionary<DayOfWeek, DayType> schedule)
    {
        var connection = await GetConnectionAsync();

        foreach (var (dayOfWeek, dayType) in schedule)
        {
            var existing = await connection.Table<WeeklyScheduleDay>()
                .Where(d => d.DayOfWeek == (int)dayOfWeek)
                .FirstOrDefaultAsync();

            if (existing is not null)
            {
                existing.DayType = dayType;
                await connection.UpdateAsync(existing);
            }
            else
            {
                await connection.InsertAsync(new WeeklyScheduleDay { DayOfWeek = (int)dayOfWeek, DayType = dayType });
            }
        }
    }

    public async Task<List<ScheduledExercise>> GetScheduledExercisesAsync(int dayOfWeek)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<ScheduledExercise>()
            .Where(x => x.DayOfWeek == dayOfWeek)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();
    }

    // Replaces the whole exercise list for a specific weekday with the given set.
    public async Task SaveScheduledExercisesAsync(int dayOfWeek, List<ScheduledExercise> exercises)
    {
        var connection = await GetConnectionAsync();

        var existing = await connection.Table<ScheduledExercise>().Where(x => x.DayOfWeek == dayOfWeek).ToListAsync();
        foreach (var item in existing)
        {
            await connection.DeleteAsync(item);
        }

        for (var i = 0; i < exercises.Count; i++)
        {
            exercises[i].Id = 0;
            exercises[i].DayOfWeek = dayOfWeek;
            exercises[i].DisplayOrder = i + 1;
        }

        if (exercises.Count > 0)
        {
            await connection.InsertAllAsync(exercises);
        }
    }

    // Swaps two weekdays' entire exercise lists (e.g. "move Thursday's
    // workout to Friday" by swapping Thursday <-> Friday) - the weekdays'
    // DayType assignments in WeeklyScheduleDay are untouched, only which
    // exercise list lives on which weekday.
    public async Task SwapScheduledExercisesAsync(int dayOfWeekA, int dayOfWeekB)
    {
        if (dayOfWeekA == dayOfWeekB) return;

        var connection = await GetConnectionAsync();

        var exercisesA = await connection.Table<ScheduledExercise>().Where(x => x.DayOfWeek == dayOfWeekA).ToListAsync();
        var exercisesB = await connection.Table<ScheduledExercise>().Where(x => x.DayOfWeek == dayOfWeekB).ToListAsync();

        foreach (var item in exercisesA) item.DayOfWeek = dayOfWeekB;
        foreach (var item in exercisesB) item.DayOfWeek = dayOfWeekA;

        await connection.UpdateAllAsync(exercisesA.Concat(exercisesB));
    }

    public async Task<WorkoutDayDisplay> GetWorkoutDayViewAsync(DateTime date)
    {
        var connection = await GetConnectionAsync();
        var day = date.Date;

        var scheduleRow = await connection.Table<WeeklyScheduleDay>()
            .Where(x => x.DayOfWeek == (int)day.DayOfWeek)
            .FirstOrDefaultAsync();
        var dayType = scheduleRow?.DayType ?? DayType.Rest;

        var routine = await connection.Table<ScheduledExercise>()
            .Where(x => x.DayOfWeek == (int)day.DayOfWeek)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        var exerciseNames = routine.Select(e => e.ExerciseName).ToList();

        // Optimized lookups (batch-fetch then group) rather than one query per
        // exercise, matching the pattern used for the nutrition lookups above.
        var allLogs = exerciseNames.Count == 0
            ? []
            : await connection.Table<ExerciseLog>().Where(x => exerciseNames.Contains(x.ExerciseName)).ToListAsync();

        // "Previous"/"Today" each show the heaviest set, not just whichever
        // set happened to be logged first/last - multiple sets in one
        // session shouldn't make progression look noisier than it is.
        var previousMap = allLogs
            .Where(l => l.DateLogged < day)
            .GroupBy(l => l.ExerciseName)
            .ToDictionary(g => g.Key, g =>
            {
                var mostRecentDate = g.Max(l => l.DateLogged);
                return g.Where(l => l.DateLogged == mostRecentDate)
                    .OrderByDescending(l => l.WeightCompletedKg)
                    .First();
            });

        var todayMap = allLogs
            .Where(l => l.DateLogged == day)
            .GroupBy(l => l.ExerciseName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.WeightCompletedKg).First());

        var completedToday = exerciseNames.Count == 0
            ? []
            : (await connection.Table<ExerciseCompletion>()
                .Where(x => x.DateCompleted == day && exerciseNames.Contains(x.ExerciseName))
                .ToListAsync())
                .Select(x => x.ExerciseName)
                .ToHashSet();

        var exerciseDisplays = routine.Select(exercise =>
        {
            previousMap.TryGetValue(exercise.ExerciseName, out var previousLog);
            todayMap.TryGetValue(exercise.ExerciseName, out var todayLog);

            return new ExerciseDisplay
            {
                ExerciseName = exercise.ExerciseName,
                TargetSets = exercise.TargetSets,
                TargetReps = exercise.TargetReps,
                TargetWeightKg = exercise.TargetWeightKg,
                PreviousPerformance = previousLog is null
                    ? "No previous data"
                    : $"Previous: {previousLog.RepsCompleted} reps @ {previousLog.WeightCompletedKg}kg",
                LoggedToday = todayLog is not null,
                LoggedTodayText = todayLog is null ? string.Empty : $"Logged: {todayLog.RepsCompleted} reps @ {todayLog.WeightCompletedKg}kg",
                IsCompleted = completedToday.Contains(exercise.ExerciseName)
            };
        }).ToList();

        var cardioLogs = await connection.Table<CardioLog>()
            .Where(x => x.DateLogged == day)
            .ToListAsync();

        return new WorkoutDayDisplay
        {
            Date = day,
            DayType = dayType,
            Exercises = exerciseDisplays,
            CardioLogs = cardioLogs.Select(c => new CardioLogDisplay
            {
                Id = c.Id,
                ActivityName = c.ActivityName,
                DurationMinutes = c.DurationMinutes,
                DistanceKm = c.DistanceKm
            }).ToList()
        };
    }

    public async Task LogExerciseSetAsync(DateTime date, string exerciseName, int reps, double weightKg)
    {
        var connection = await GetConnectionAsync();
        await connection.InsertAsync(new ExerciseLog
        {
            ExerciseName = exerciseName,
            DateLogged = date.Date,
            RepsCompleted = reps,
            WeightCompletedKg = weightKg
        });
    }

    // Plain done/not-done toggle for "did the reps for this exercise today" -
    // independent of how many sets were logged via LogExerciseSetAsync.
    public async Task ToggleExerciseCompletionAsync(DateTime date, string exerciseName)
    {
        var connection = await GetConnectionAsync();
        var day = date.Date;

        var existing = await connection.Table<ExerciseCompletion>()
            .Where(x => x.ExerciseName == exerciseName && x.DateCompleted == day)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            await connection.DeleteAsync(existing);
        }
        else
        {
            await connection.InsertAsync(new ExerciseCompletion { ExerciseName = exerciseName, DateCompleted = day });
        }
    }

    public async Task AddCardioLogAsync(DateTime date, string activityName, int durationMinutes, double? distanceKm)
    {
        var connection = await GetConnectionAsync();
        await connection.InsertAsync(new CardioLog
        {
            DateLogged = date.Date,
            ActivityName = activityName,
            DurationMinutes = durationMinutes,
            DistanceKm = distanceKm
        });
    }

    public async Task DeleteCardioLogAsync(int id)
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAsync<CardioLog>(id);
    }

    // Used by the workout calendar to mark which days already have something
    // logged (either lifting sets or cardio).
    public async Task<HashSet<DateTime>> GetLoggedWorkoutDatesAsync(DateTime rangeStart, DateTime rangeEndExclusive)
    {
        var connection = await GetConnectionAsync();

        var exerciseLogs = await connection.Table<ExerciseLog>()
            .Where(x => x.DateLogged >= rangeStart.Date && x.DateLogged < rangeEndExclusive.Date)
            .ToListAsync();
        var cardioLogs = await connection.Table<CardioLog>()
            .Where(x => x.DateLogged >= rangeStart.Date && x.DateLogged < rangeEndExclusive.Date)
            .ToListAsync();

        var dates = new HashSet<DateTime>();
        foreach (var log in exerciseLogs) dates.Add(log.DateLogged.Date);
        foreach (var log in cardioLogs) dates.Add(log.DateLogged.Date);
        return dates;
    }

    public async Task<List<ProgressionRecord>> GetExerciseProgressionReportAsync(string exerciseName)
    {
        var connection = await GetConnectionAsync();

        var allLogs = await connection.Table<ExerciseLog>()
            .Where(x => x.ExerciseName == exerciseName)
            .ToListAsync();

        // One record per day - the heaviest set logged that day - rather than
        // one record per individual set. Comparing every set against the one
        // immediately before it made multi-set sessions look like noisy
        // back-to-back regressions even when the day as a whole was a PR.
        var bestPerDay = allLogs
            .GroupBy(l => l.DateLogged.Date)
            .Select(g => g.OrderByDescending(l => l.WeightCompletedKg).First())
            .OrderBy(l => l.DateLogged)
            .ToList();

        var records = new List<ProgressionRecord>();
        double? previousWeight = null;

        foreach (var log in bestPerDay)
        {
            double? difference = previousWeight is null ? null : log.WeightCompletedKg - previousWeight;

            records.Add(new ProgressionRecord
            {
                LogDate = log.DateLogged,
                WeightAchieved = log.WeightCompletedKg,
                WeightDifference = difference,
                DisplayDetails = $"{log.RepsCompleted} reps @ {log.WeightCompletedKg}kg"
            });

            previousWeight = log.WeightCompletedKg;
        }

        // Reverse the list so the most recent workout is at the top of the screen
        records.Reverse();
        return records;
    }

    public async Task SaveExerciseToLibraryAsync(Exercise exercise)
    {
        var connection = await GetConnectionAsync();
        await connection.InsertAsync(exercise);
    }

    public async Task<List<Exercise>> GetAllLibraryExercisesAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Exercise>().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<int> ImportExercisesToLibraryAsync(IEnumerable<(string Name, string Category)> exercises)
    {
        var connection = await GetConnectionAsync();

        var existingNames = (await connection.Table<Exercise>().ToListAsync())
            .Select(e => e.Name.Trim().ToLowerInvariant())
            .ToHashSet();

        var newExercises = exercises
            .GroupBy(e => e.Name.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .Where(e => !existingNames.Contains(e.Name.Trim().ToLowerInvariant()))
            .Select(e => new Exercise { Name = e.Name, Category = e.Category })
            .ToList();

        if (newExercises.Count > 0)
        {
            await connection.InsertAllAsync(newExercises);
        }

        return newExercises.Count;
    }

    public async Task<NutritionSummaryDisplay> GetNutritionSummaryForDateAsync(DateTime date)
    {
        var connection = await GetConnectionAsync();
        var day = date.Date;

        var entries = await connection.Table<FoodEntry>()
            .Where(x => x.UserId == DemoUserId && x.DateLogged == day)
            .ToListAsync();

        var goal = await GetOrCreateNutritionGoalAsync(connection);

        List<FoodEntryDisplay> ToDisplay(MealType type) =>
            entries
                .Where(e => e.MealType == type)
                .Select(e => new FoodEntryDisplay
                {
                    Id = e.Id,
                    Name = e.Name,
                    QuantityGrams = e.QuantityGrams,
                    CaloriesPer100g = e.CaloriesPer100g,
                    ProteinPer100g = e.ProteinPer100g,
                    CarbsPer100g = e.CarbsPer100g,
                    FatPer100g = e.FatPer100g
                })
                .ToList();

        var breakfast = ToDisplay(MealType.Breakfast);
        var lunch = ToDisplay(MealType.Lunch);
        var dinner = ToDisplay(MealType.Dinner);
        var snacks = ToDisplay(MealType.Snacks);

        var totalCalories = entries.Sum(e => (int)Math.Round(e.CaloriesPer100g * e.QuantityGrams / 100.0));
        var totalProtein = entries.Sum(e => e.ProteinPer100g * e.QuantityGrams / 100.0);
        var totalCarbs = entries.Sum(e => e.CarbsPer100g * e.QuantityGrams / 100.0);
        var totalFat = entries.Sum(e => e.FatPer100g * e.QuantityGrams / 100.0);

        static double Progress(double value, double goal) => goal > 0 ? Math.Clamp(value / goal, 0, 1) : 0;
        static string MealHeading(string title, List<FoodEntryDisplay> list) => $"{title} - {list.Sum(x => x.Calories)} kcal";
        static MealNutrientTotals Totals(MealType type, List<FoodEntryDisplay> list) => new(
            type,
            list.Sum(x => (double)x.Calories),
            list.Sum(x => x.Protein),
            list.Sum(x => x.Carbs),
            list.Sum(x => x.Fat));

        return new NutritionSummaryDisplay
        {
            TotalCalories = totalCalories,
            CalorieGoal = goal.DailyCalorieGoal,
            CalorieProgress = Progress(totalCalories, goal.DailyCalorieGoal),
            CaloriesSummaryText = $"Calories - {totalCalories} / {goal.DailyCalorieGoal} kcal",

            TotalProtein = totalProtein,
            ProteinGoal = goal.ProteinGoalGrams,
            ProteinProgress = Progress(totalProtein, goal.ProteinGoalGrams),
            ProteinSummaryText = $"{Math.Round(totalProtein)}g / {Math.Round(goal.ProteinGoalGrams)}g",

            TotalCarbs = totalCarbs,
            CarbsGoal = goal.CarbsGoalGrams,
            CarbsProgress = Progress(totalCarbs, goal.CarbsGoalGrams),
            CarbsSummaryText = $"{Math.Round(totalCarbs)}g / {Math.Round(goal.CarbsGoalGrams)}g",

            TotalFat = totalFat,
            FatGoal = goal.FatGoalGrams,
            FatProgress = Progress(totalFat, goal.FatGoalGrams),
            FatSummaryText = $"{Math.Round(totalFat)}g / {Math.Round(goal.FatGoalGrams)}g",

            BreakfastEntries = breakfast,
            LunchEntries = lunch,
            DinnerEntries = dinner,
            SnacksEntries = snacks,

            BreakfastHeading = MealHeading("Breakfast", breakfast),
            LunchHeading = MealHeading("Lunch", lunch),
            DinnerHeading = MealHeading("Dinner", dinner),
            SnacksHeading = MealHeading("Snacks", snacks),

            MealTotals =
            [
                Totals(MealType.Breakfast, breakfast),
                Totals(MealType.Lunch, lunch),
                Totals(MealType.Dinner, dinner),
                Totals(MealType.Snacks, snacks)
            ]
        };
    }

    private async Task<NutritionGoal> GetOrCreateNutritionGoalAsync(SQLiteAsyncConnection connection)
    {
        var goal = await connection.Table<NutritionGoal>().Where(g => g.UserId == DemoUserId).FirstOrDefaultAsync();
        if (goal is not null) return goal;

        goal = new NutritionGoal { UserId = DemoUserId };
        await connection.InsertAsync(goal);
        return goal;
    }

    public async Task UpdateNutritionGoalAsync(int calorieGoal, double proteinGoalGrams, double carbsGoalGrams, double fatGoalGrams)
    {
        var connection = await GetConnectionAsync();
        var goal = await GetOrCreateNutritionGoalAsync(connection);

        goal.DailyCalorieGoal = calorieGoal;
        goal.ProteinGoalGrams = proteinGoalGrams;
        goal.CarbsGoalGrams = carbsGoalGrams;
        goal.FatGoalGrams = fatGoalGrams;

        await connection.UpdateAsync(goal);
    }

    public async Task AddFoodEntryAsync(
        DateTime date,
        MealType mealType,
        string name,
        double caloriesPer100g,
        double proteinPer100g,
        double carbsPer100g,
        double fatPer100g,
        double quantityGrams)
    {
        var connection = await GetConnectionAsync();
        await connection.InsertAsync(new FoodEntry
        {
            UserId = DemoUserId,
            DateLogged = date.Date,
            MealType = mealType,
            Name = name,
            CaloriesPer100g = caloriesPer100g,
            ProteinPer100g = proteinPer100g,
            CarbsPer100g = carbsPer100g,
            FatPer100g = fatPer100g,
            QuantityGrams = quantityGrams
        });
    }

    public async Task UpdateFoodEntryQuantityAsync(int entryId, double quantityGrams)
    {
        var connection = await GetConnectionAsync();
        var entry = await connection.Table<FoodEntry>().Where(e => e.Id == entryId).FirstOrDefaultAsync();
        if (entry is null) return;

        entry.QuantityGrams = quantityGrams;
        await connection.UpdateAsync(entry);
    }

    public async Task DeleteFoodEntryAsync(int id)
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAsync<FoodEntry>(id);
    }

    // Used by the calendar to mark which days (within the visible grid range)
    // already have food logged, so it can show a dot on those cells.
    public async Task<HashSet<DateTime>> GetLoggedDatesAsync(DateTime rangeStart, DateTime rangeEndExclusive)
    {
        var connection = await GetConnectionAsync();
        var entries = await connection.Table<FoodEntry>()
            .Where(x => x.UserId == DemoUserId && x.DateLogged >= rangeStart.Date && x.DateLogged < rangeEndExclusive.Date)
            .ToListAsync();

        return entries.Select(e => e.DateLogged.Date).ToHashSet();
    }

    public sealed class NutritionSummaryDisplay
    {
        public int TotalCalories { get; init; }
        public int CalorieGoal { get; init; }
        public double CalorieProgress { get; init; }
        public string CaloriesSummaryText { get; init; } = string.Empty;

        public double TotalProtein { get; init; }
        public double ProteinGoal { get; init; }
        public double ProteinProgress { get; init; }
        public string ProteinSummaryText { get; init; } = string.Empty;

        public double TotalCarbs { get; init; }
        public double CarbsGoal { get; init; }
        public double CarbsProgress { get; init; }
        public string CarbsSummaryText { get; init; } = string.Empty;

        public double TotalFat { get; init; }
        public double FatGoal { get; init; }
        public double FatProgress { get; init; }
        public string FatSummaryText { get; init; } = string.Empty;

        public List<FoodEntryDisplay> BreakfastEntries { get; init; } = [];
        public List<FoodEntryDisplay> LunchEntries { get; init; } = [];
        public List<FoodEntryDisplay> DinnerEntries { get; init; } = [];
        public List<FoodEntryDisplay> SnacksEntries { get; init; } = [];

        public string BreakfastHeading { get; init; } = string.Empty;
        public string LunchHeading { get; init; } = string.Empty;
        public string DinnerHeading { get; init; } = string.Empty;
        public string SnacksHeading { get; init; } = string.Empty;

        // Always 4 entries in Breakfast, Lunch, Dinner, Snacks order (zeros for
        // meals with nothing logged), used to render the stacked progress bars.
        public List<MealNutrientTotals> MealTotals { get; init; } = [];
    }

    public sealed record MealNutrientTotals(MealType MealType, double Calories, double Protein, double Carbs, double Fat);

    public sealed class FoodEntryDisplay
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double QuantityGrams { get; set; }
        public double CaloriesPer100g { get; set; }
        public double ProteinPer100g { get; set; }
        public double CarbsPer100g { get; set; }
        public double FatPer100g { get; set; }

        public int Calories => (int)Math.Round(CaloriesPer100g * QuantityGrams / 100.0);
        public double Protein => Math.Round(ProteinPer100g * QuantityGrams / 100.0, 1);
        public double Carbs => Math.Round(CarbsPer100g * QuantityGrams / 100.0, 1);
        public double Fat => Math.Round(FatPer100g * QuantityGrams / 100.0, 1);

        public string QuantityDisplay => $"{QuantityGrams:N0} g";
        public string CaloriesDisplay => $"{Calories} kcal";
        public string MacrosDisplay => $"P {Protein:0.#}g · C {Carbs:0.#}g · F {Fat:0.#}g";
    }

    public async Task SaveCompleteMealAsync(Meal meal, List<MealIngredient> ingredients)
    {
        var connection = await GetConnectionAsync();

        await connection.InsertAsync(meal);

        foreach (var ingredient in ingredients)
        {
            ingredient.MealId = meal.Id;
        }
        await connection.InsertAllAsync(ingredients);
    }

    public async Task<List<MealSummary>> GetMealLibraryAsync()
    {
        var connection = await GetConnectionAsync();
        var meals = await connection.Table<Meal>().ToListAsync();
        var allIngredients = await connection.Table<MealIngredient>().ToListAsync();

        return meals
            .Select(m => new MealSummary
            {
                Id = m.Id,
                Name = m.Name,
                TotalCalories = allIngredients
                    .Where(i => i.MealId == m.Id)
                    .Sum(i => (int)Math.Round(i.CaloriesPer100g * i.QuantityGrams / 100.0))
            })
            .OrderBy(m => m.Name)
            .ToList();
    }

    // Expands a saved meal into one FoodEntry per ingredient (rather than a single
    // lump total) so each ingredient's grams can still be edited/removed individually
    // once it's logged into a meal slot for today.
    public async Task LogMealAsync(int mealId, DateTime date, MealType mealType)
    {
        var connection = await GetConnectionAsync();
        var ingredients = await connection.Table<MealIngredient>().Where(i => i.MealId == mealId).ToListAsync();

        var entries = ingredients.Select(i => new FoodEntry
        {
            UserId = DemoUserId,
            DateLogged = date.Date,
            MealType = mealType,
            Name = i.Name,
            CaloriesPer100g = i.CaloriesPer100g,
            ProteinPer100g = i.ProteinPer100g,
            CarbsPer100g = i.CarbsPer100g,
            FatPer100g = i.FatPer100g,
            QuantityGrams = i.QuantityGrams
        }).ToList();

        if (entries.Count > 0)
        {
            await connection.InsertAllAsync(entries);
        }
    }

    public async Task<List<RecentFoodSelection>> GetRecentFoodSelectionsAsync(int take = 10)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<RecentFoodSelection>()
            .Where(x => x.UserId == DemoUserId)
            .OrderByDescending(x => x.LastUsedUtc)
            .Take(take)
            .ToListAsync();
    }

    public async Task RecordFoodSelectionAsync(string code, string name, double? caloriesPer100g, string? quantity)
    {
        var connection = await GetConnectionAsync();

        var existing = await connection.Table<RecentFoodSelection>()
            .Where(x => x.UserId == DemoUserId && x.Code == code)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.Name = name;
            existing.CaloriesPer100g = caloriesPer100g;
            existing.Quantity = quantity;
            existing.LastUsedUtc = DateTime.UtcNow;
            await connection.UpdateAsync(existing);
        }
        else
        {
            await connection.InsertAsync(new RecentFoodSelection
            {
                UserId = DemoUserId,
                Code = code,
                Name = name,
                CaloriesPer100g = caloriesPer100g,
                Quantity = quantity,
                LastUsedUtc = DateTime.UtcNow
            });
        }
    }

    public class MealSummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalCalories { get; set; }
    }

    // Lets a repeat scan of the same barcode skip straight to logging instead
    // of asking for macros again.
    public async Task<CustomFoodItem?> GetCustomFoodItemByBarcodeAsync(string barcode)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<CustomFoodItem>()
            .Where(x => x.UserId == DemoUserId && x.Barcode == barcode)
            .FirstOrDefaultAsync();
    }

    // Lets custom entries surface in a plain name search too, not just on a
    // repeat barcode scan.
    public async Task<List<CustomFoodItem>> SearchCustomFoodItemsAsync(string term)
    {
        var connection = await GetConnectionAsync();
        var all = await connection.Table<CustomFoodItem>()
            .Where(x => x.UserId == DemoUserId)
            .ToListAsync();

        return all.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<CustomFoodItem> SaveCustomFoodItemAsync(
        string? barcode,
        string name,
        double caloriesPer100g,
        double proteinPer100g,
        double carbsPer100g,
        double fatPer100g)
    {
        var connection = await GetConnectionAsync();
        var item = new CustomFoodItem
        {
            UserId = DemoUserId,
            Barcode = barcode,
            Name = name,
            CaloriesPer100g = caloriesPer100g,
            ProteinPer100g = proteinPer100g,
            CarbsPer100g = carbsPer100g,
            FatPer100g = fatPer100g,
            CreatedUtc = DateTime.UtcNow
        };

        await connection.InsertAsync(item);
        return item;
    }

    public sealed class WorkoutDayDisplay
    {
        public DateTime Date { get; init; }
        public DayType DayType { get; init; }
        public List<ExerciseDisplay> Exercises { get; init; } = [];
        public List<CardioLogDisplay> CardioLogs { get; init; } = [];
    }

    public sealed class ExerciseDisplay
    {
        public string ExerciseName { get; init; } = string.Empty;
        public int TargetSets { get; init; }
        public int TargetReps { get; init; }
        public double TargetWeightKg { get; init; }
        public string PreviousPerformance { get; init; } = string.Empty;
        public bool LoggedToday { get; init; }
        public string LoggedTodayText { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }

        public string TargetsDisplay => TargetWeightKg > 0
            ? $"{TargetSets} x {TargetReps} @ {TargetWeightKg:0.#}kg"
            : $"{TargetSets} x {TargetReps}";
    }

    public sealed class CardioLogDisplay
    {
        public int Id { get; init; }
        public string ActivityName { get; init; } = string.Empty;
        public int DurationMinutes { get; init; }
        public double? DistanceKm { get; init; }

        public string DisplayText => DistanceKm.HasValue
            ? $"{ActivityName} - {DurationMinutes} min, {DistanceKm:0.##} km"
            : $"{ActivityName} - {DurationMinutes} min";
    }

    public class ProgressionRecord
{
    public DateTime LogDate { get; set; }
    public double WeightAchieved { get; set; }
    public double? WeightDifference { get; set; }
    public string DisplayDetails { get; set; } = string.Empty;

    // Helper property to automatically format the UI text
    public string DifferenceText
    {
        get
        {
            if (WeightDifference is null) return "Baseline";
            if (WeightDifference == 0) return "No change";

            // Adds a "+" for positive numbers. Negatives naturally get a "-"
            return WeightDifference > 0 ? $"+{WeightDifference:N1} kg" : $"{WeightDifference:N1} kg";
        }
    }

    // Pulled from the shared resource palette rather than hardcoded hex
    // (these were leftover literal values from the old green/gold palette).
    public Color DifferenceColor
    {
        get
        {
            if (WeightDifference is null || WeightDifference == 0) return ResourceColor("Gray400", Colors.Gray);
            return WeightDifference > 0 ? ResourceColor("Success", Colors.Green) : ResourceColor("Danger", Colors.Red);
        }
    }

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;
}
}