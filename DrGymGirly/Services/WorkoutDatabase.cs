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
        await _connection.CreateTableAsync<Workout>();
        await _connection.CreateTableAsync<WorkoutExercise>();
        await _connection.CreateTableAsync<WeeklyScheduleWorkout>();
        await _connection.CreateTableAsync<ScheduleOverrideWorkout>();
        await _connection.CreateTableAsync<ExerciseCompletion>();
        await _connection.CreateTableAsync<ExerciseLog>();
        await _connection.CreateTableAsync<CardioLog>();
        await _connection.CreateTableAsync<BodyWeightLog>();

        // The single-workout-per-day tables only need recreating (and
        // seeding/migrating) while there's still pre-existing data in them to
        // carry across - once MigrateToMultiWorkoutScheduleAsync below has
        // dropped them, later launches skip straight past this.
        var legacyWeeklyExists = await TableExistsAsync(_connection, "WeeklyScheduleDays");
        if (legacyWeeklyExists)
        {
            await _connection.CreateTableAsync<WeeklyScheduleDay>();
            await SeedWeeklyScheduleAsync(_connection);
        }

        await MigrateToWorkoutsAsync(_connection);
        await MigrateToMultiWorkoutScheduleAsync(_connection, legacyWeeklyExists);
        return _connection;
    }

    private static async Task<bool> TableExistsAsync(SQLiteAsyncConnection connection, string tableName) =>
        await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", tableName) > 0;

    // One-time migration from the old DayType-keyed, per-weekday
    // ScheduledExercises table to the new named Workout entities: every
    // weekday that had a non-Rest DayType (or leftover exercises) becomes its
    // own Workout, carrying that weekday's old exercise list over and becoming
    // that weekday's new default. Guarded by the old table's existence, so
    // this only ever runs once per install; WorkoutExercises is a distinct
    // table name from the old ScheduledExercises, so the old data sits
    // untouched until this reads and migrates it.
    private static async Task MigrateToWorkoutsAsync(SQLiteAsyncConnection connection)
    {
        var oldTableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ScheduledExercises'");
        if (oldTableExists == 0) return;

        var oldSchedule = await connection.QueryAsync<OldScheduleRow>("SELECT DayOfWeek, DayType FROM WeeklyScheduleDays");
        var oldExercises = await connection.QueryAsync<OldScheduledExerciseRow>("SELECT * FROM ScheduledExercises");

        foreach (var scheduleRow in oldSchedule)
        {
            var exercisesForDay = oldExercises.Where(x => x.DayOfWeek == scheduleRow.DayOfWeek).OrderBy(x => x.DisplayOrder).ToList();
            if (scheduleRow.DayType == (int)DayType.Rest && exercisesForDay.Count == 0) continue;

            var dayOfWeek = (DayOfWeek)scheduleRow.DayOfWeek;
            var dayType = (DayType)scheduleRow.DayType;

            var workout = new Workout { Name = $"{dayOfWeek} {dayType}", Category = dayType };
            await connection.InsertAsync(workout);

            var migratedExercises = exercisesForDay.Select(x => new WorkoutExercise
            {
                WorkoutId = workout.Id,
                DisplayOrder = x.DisplayOrder,
                ExerciseName = x.ExerciseName,
                TargetSets = x.TargetSets,
                TargetWeightKg = x.TargetWeightKg
            }).ToList();
            if (migratedExercises.Count > 0)
            {
                await connection.InsertAllAsync(migratedExercises);
            }

            await connection.ExecuteAsync("UPDATE WeeklyScheduleDays SET WorkoutId = ? WHERE DayOfWeek = ?", workout.Id, scheduleRow.DayOfWeek);
        }

        await connection.ExecuteAsync("DROP TABLE ScheduledExercises");
    }

    private sealed class OldScheduleRow
    {
        public int DayOfWeek { get; set; }
        public int DayType { get; set; }
    }

    private sealed class OldScheduledExerciseRow
    {
        public int Id { get; set; }
        public int DayOfWeek { get; set; }
        public int DisplayOrder { get; set; }
        public string ExerciseName { get; set; } = string.Empty;
        public int TargetSets { get; set; }
        public int TargetReps { get; set; }
        public double TargetWeightKg { get; set; }
    }

    private static async Task SeedWeeklyScheduleAsync(SQLiteAsyncConnection connection)
    {
        if (await connection.Table<WeeklyScheduleDay>().CountAsync() > 0) return;

        await connection.InsertAllAsync(Enum.GetValues<DayOfWeek>()
            .Select(d => new WeeklyScheduleDay { DayOfWeek = (int)d, WorkoutId = null }));
    }

    // One-time migration from the single-workout-per-day WeeklyScheduleDay/
    // ScheduleOverride tables to the multi-workout WeeklyScheduleWorkout/
    // ScheduleOverrideWorkout ones, so a day can default to (or be overridden
    // to) more than one Workout. Each non-null old row becomes one new row;
    // the old tables are dropped once migrated, so later launches skip this
    // (legacyWeeklyExists is false and the ScheduleOverrides existence check
    // below is false too).
    private static async Task MigrateToMultiWorkoutScheduleAsync(SQLiteAsyncConnection connection, bool legacyWeeklyExists)
    {
        if (legacyWeeklyExists)
        {
            var oldDefaults = await connection.Table<WeeklyScheduleDay>().ToListAsync();
            var migratedDefaults = oldDefaults
                .Where(x => x.WorkoutId.HasValue)
                .Select(x => new WeeklyScheduleWorkout { DayOfWeek = x.DayOfWeek, WorkoutId = x.WorkoutId!.Value })
                .ToList();
            if (migratedDefaults.Count > 0) await connection.InsertAllAsync(migratedDefaults);

            await connection.ExecuteAsync("DROP TABLE WeeklyScheduleDays");
        }

        if (await TableExistsAsync(connection, "ScheduleOverrides"))
        {
            var oldOverrides = await connection.Table<ScheduleOverride>().ToListAsync();
            var migratedOverrides = oldOverrides
                .Select(x => new ScheduleOverrideWorkout { Date = x.Date, WorkoutId = x.WorkoutId })
                .ToList();
            if (migratedOverrides.Count > 0) await connection.InsertAllAsync(migratedOverrides);

            await connection.ExecuteAsync("DROP TABLE ScheduleOverrides");
        }
    }

    public async Task<List<Workout>> GetAllWorkoutsAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Workout>().OrderBy(w => w.Name).ToListAsync();
    }

    public async Task<Workout?> GetWorkoutAsync(int workoutId)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Workout>().Where(w => w.Id == workoutId).FirstOrDefaultAsync();
    }

    public async Task<int> CreateWorkoutAsync(string name, DayType category)
    {
        var connection = await GetConnectionAsync();
        var workout = new Workout { Name = name, Category = category };
        await connection.InsertAsync(workout);
        return workout.Id;
    }

    // Also removes the workout's own exercise list. Any weekday defaults or
    // one-off overrides that pointed at it are left as dangling WorkoutIds -
    // GetScheduledWorkoutsForRangeAsync treats an unresolvable WorkoutId as a
    // rest day, so this can't surface as a crash, only as that day reverting
    // to rest.
    public async Task DeleteWorkoutAsync(int workoutId)
    {
        var connection = await GetConnectionAsync();
        await connection.Table<WorkoutExercise>().Where(x => x.WorkoutId == workoutId).DeleteAsync();
        await connection.DeleteAsync<Workout>(workoutId);
    }

    public async Task<Dictionary<DayOfWeek, List<int>>> GetWeeklyDefaultsAsync()
    {
        var connection = await GetConnectionAsync();
        var rows = await connection.Table<WeeklyScheduleWorkout>().ToListAsync();
        return rows.GroupBy(r => (DayOfWeek)r.DayOfWeek).ToDictionary(g => g.Key, g => g.Select(r => r.WorkoutId).ToList());
    }

    // Replaces each given weekday's whole default list with the given set of
    // WorkoutIds (an empty list means that weekday defaults to rest).
    public async Task UpdateWeeklyDefaultsAsync(Dictionary<DayOfWeek, List<int>> defaults)
    {
        var connection = await GetConnectionAsync();

        foreach (var (dayOfWeek, workoutIds) in defaults)
        {
            var existing = await connection.Table<WeeklyScheduleWorkout>().Where(x => x.DayOfWeek == (int)dayOfWeek).ToListAsync();
            foreach (var row in existing)
            {
                await connection.DeleteAsync(row);
            }

            if (workoutIds.Count > 0)
            {
                await connection.InsertAllAsync(workoutIds.Select(id => new WeeklyScheduleWorkout { DayOfWeek = (int)dayOfWeek, WorkoutId = id }));
            }
        }
    }

    public async Task<List<WorkoutExercise>> GetWorkoutExercisesAsync(int workoutId)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<WorkoutExercise>()
            .Where(x => x.WorkoutId == workoutId)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();
    }

    // Replaces the whole exercise list for a specific workout with the given set.
    public async Task SaveWorkoutExercisesAsync(int workoutId, List<WorkoutExercise> exercises)
    {
        var connection = await GetConnectionAsync();

        var existing = await connection.Table<WorkoutExercise>().Where(x => x.WorkoutId == workoutId).ToListAsync();
        foreach (var item in existing)
        {
            await connection.DeleteAsync(item);
        }

        for (var i = 0; i < exercises.Count; i++)
        {
            exercises[i].Id = 0;
            exercises[i].WorkoutId = workoutId;
            exercises[i].DisplayOrder = i + 1;
        }

        if (exercises.Count > 0)
        {
            await connection.InsertAllAsync(exercises);
        }
    }

    // What's actually scheduled on a specific date: any ScheduleOverrideWorkout
    // rows for that exact date if any exist (even just the null-WorkoutId rest
    // marker - that still counts as "overridden"), else that weekday's
    // WeeklyScheduleWorkout defaults. Empty list means rest.
    private static async Task<List<int>> GetEffectiveWorkoutIdsAsync(SQLiteAsyncConnection connection, DateTime date)
    {
        var day = date.Date;

        var overrideRows = await connection.Table<ScheduleOverrideWorkout>().Where(x => x.Date == day).ToListAsync();
        if (overrideRows.Count > 0)
        {
            return overrideRows.Where(x => x.WorkoutId.HasValue).Select(x => x.WorkoutId!.Value).ToList();
        }

        var defaultRows = await connection.Table<WeeklyScheduleWorkout>().Where(x => x.DayOfWeek == (int)day.DayOfWeek).ToListAsync();
        return defaultRows.Select(x => x.WorkoutId).ToList();
    }

    // Batch-resolves which Workouts are effectively scheduled for each date in
    // the range - used by the calendar strip, which needs this for 7 days at
    // once rather than one query per day.
    public async Task<Dictionary<DateTime, List<Workout>>> GetScheduledWorkoutsForRangeAsync(DateTime rangeStart, DateTime rangeEndExclusive)
    {
        var connection = await GetConnectionAsync();

        var defaultsByDay = (await connection.Table<WeeklyScheduleWorkout>().ToListAsync())
            .GroupBy(d => (DayOfWeek)d.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Select(x => x.WorkoutId).ToList());

        var overridesByDate = (await connection.Table<ScheduleOverrideWorkout>()
            .Where(x => x.Date >= rangeStart.Date && x.Date < rangeEndExclusive.Date)
            .ToListAsync())
            .GroupBy(o => o.Date)
            .ToDictionary(g => g.Key, g => g.Where(x => x.WorkoutId.HasValue).Select(x => x.WorkoutId!.Value).ToList());

        var workoutsById = (await connection.Table<Workout>().ToListAsync()).ToDictionary(w => w.Id);

        var result = new Dictionary<DateTime, List<Workout>>();
        for (var date = rangeStart.Date; date < rangeEndExclusive.Date; date = date.AddDays(1))
        {
            var workoutIds = overridesByDate.TryGetValue(date, out var overrideIds) ? overrideIds : defaultsByDay.GetValueOrDefault(date.DayOfWeek, []);
            result[date] = workoutIds.Where(workoutsById.ContainsKey).Select(id => workoutsById[id]).ToList();
        }

        return result;
    }

    // Moves one specific workout from fromDate onto toDate for that
    // occurrence only: both dates get a full-replacement ScheduleOverrideWorkout
    // list (the chosen workout removed from fromDate's current list, added to
    // toDate's), leaving every other workout scheduled on either day alone,
    // and leaving WeeklyScheduleWorkout's recurring defaults untouched - so
    // the following week, both weekdays fall back to their normal defaults again.
    public async Task MoveWorkoutOccurrenceAsync(DateTime fromDate, DateTime toDate, int workoutId)
    {
        if (fromDate.Date == toDate.Date) return;

        var connection = await GetConnectionAsync();

        var fromIds = await GetEffectiveWorkoutIdsAsync(connection, fromDate);
        var toIds = await GetEffectiveWorkoutIdsAsync(connection, toDate);

        var newFromIds = fromIds.Where(id => id != workoutId).ToList();
        var newToIds = toIds.Contains(workoutId) ? toIds : [.. toIds, workoutId];

        await ReplaceOverrideAsync(connection, fromDate.Date, newFromIds);
        await ReplaceOverrideAsync(connection, toDate.Date, newToIds);
    }

    private static async Task ReplaceOverrideAsync(SQLiteAsyncConnection connection, DateTime date, List<int> workoutIds)
    {
        var existing = await connection.Table<ScheduleOverrideWorkout>().Where(x => x.Date == date).ToListAsync();
        foreach (var row in existing)
        {
            await connection.DeleteAsync(row);
        }

        if (workoutIds.Count == 0)
        {
            await connection.InsertAsync(new ScheduleOverrideWorkout { Date = date, WorkoutId = null });
        }
        else
        {
            await connection.InsertAllAsync(workoutIds.Select(id => new ScheduleOverrideWorkout { Date = date, WorkoutId = id }));
        }
    }

    // An override (from Move, or a leftover one-off from before this date's
    // weekly default was set up the way it is now) fully replaces that date's
    // weekly defaults for as long as it exists - it never expires on its own.
    // This is the only way to drop it and let the date go back to following
    // WeeklyScheduleWorkout again.
    public async Task<bool> HasOverrideAsync(DateTime date)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<ScheduleOverrideWorkout>().Where(x => x.Date == date.Date).CountAsync() > 0;
    }

    public async Task ClearOverrideAsync(DateTime date)
    {
        var connection = await GetConnectionAsync();
        var existing = await connection.Table<ScheduleOverrideWorkout>().Where(x => x.Date == date.Date).ToListAsync();
        foreach (var row in existing)
        {
            await connection.DeleteAsync(row);
        }
    }

    // Batch version of HasOverrideAsync for the calendar strip, so it can
    // mark every overridden date in the visible week at once.
    public async Task<HashSet<DateTime>> GetOverriddenDatesAsync(DateTime rangeStart, DateTime rangeEndExclusive)
    {
        var connection = await GetConnectionAsync();
        var rows = await connection.Table<ScheduleOverrideWorkout>()
            .Where(x => x.Date >= rangeStart.Date && x.Date < rangeEndExclusive.Date)
            .ToListAsync();
        return rows.Select(x => x.Date).ToHashSet();
    }

    public async Task<WorkoutDayDisplay> GetWorkoutDayViewAsync(DateTime date)
    {
        var connection = await GetConnectionAsync();
        var day = date.Date;

        var workoutIds = await GetEffectiveWorkoutIdsAsync(connection, day);
        var workoutsById = workoutIds.Count == 0
            ? new Dictionary<int, Workout>()
            : (await connection.Table<Workout>().Where(w => workoutIds.Contains(w.Id)).ToListAsync()).ToDictionary(w => w.Id);

        // Preserve the schedule's own ordering (e.g. whichever was assigned
        // first) rather than whatever order the DB happens to return.
        var workouts = workoutIds.Where(workoutsById.ContainsKey).Select(id => workoutsById[id]).ToList();
        var dayType = workouts.Count > 0 ? workouts[0].Category : DayType.Rest;

        var routine = workouts.Count == 0
            ? []
            : await connection.Table<WorkoutExercise>()
                .Where(x => workoutIds.Contains(x.WorkoutId))
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();

        var exerciseNames = routine.Select(e => e.ExerciseName).Distinct().ToList();

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

        // Every set logged today, in the order they were logged - not just
        // the heaviest. Showing only the best set made additional sets look
        // like they hadn't been saved whenever they weren't a new high.
        var todaysSetsMap = allLogs
            .Where(l => l.DateLogged == day)
            .GroupBy(l => l.ExerciseName)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).ToList());

        var completedToday = exerciseNames.Count == 0
            ? []
            : (await connection.Table<ExerciseCompletion>()
                .Where(x => x.DateCompleted == day && exerciseNames.Contains(x.ExerciseName))
                .ToListAsync())
                .Select(x => x.ExerciseName)
                .ToHashSet();

        ExerciseDisplay ToDisplay(WorkoutExercise exercise)
        {
            previousMap.TryGetValue(exercise.ExerciseName, out var previousLog);
            todaysSetsMap.TryGetValue(exercise.ExerciseName, out var todaysSets);
            todaysSets ??= [];

            return new ExerciseDisplay
            {
                ExerciseName = exercise.ExerciseName,
                TargetSets = exercise.TargetSets,
                TargetWeightKg = exercise.TargetWeightKg,
                PreviousPerformance = previousLog is null
                    ? "No previous data"
                    : $"Previous: {previousLog.RepsCompleted} reps @ {previousLog.WeightCompletedKg}kg",
                TodaysSets = todaysSets.Select((log, i) => new LoggedSetDisplay
                {
                    Id = log.Id,
                    SetNumber = i + 1,
                    Reps = log.RepsCompleted,
                    Weight = log.WeightCompletedKg,
                    DateLogged = log.DateLogged,
                    WasEdited = log.WasEdited
                }).ToList(),
                IsCompleted = completedToday.Contains(exercise.ExerciseName)
            };
        }

        // One section per scheduled workout, so a day with both a Physio and
        // a Gym session (say) shows them as two separate, named groups rather
        // than one flattened exercise list.
        var sections = workouts.Select(w => new WorkoutSectionDisplay
        {
            WorkoutId = w.Id,
            WorkoutName = w.Name,
            Category = w.Category,
            Exercises = routine.Where(x => x.WorkoutId == w.Id).Select(ToDisplay).ToList()
        }).ToList();

        var cardioLogs = await connection.Table<CardioLog>()
            .Where(x => x.DateLogged == day)
            .ToListAsync();

        return new WorkoutDayDisplay
        {
            Date = day,
            DayType = dayType,
            Sections = sections,
            CardioLogs = cardioLogs.Select(c => new CardioLogDisplay
            {
                Id = c.Id,
                ActivityName = c.ActivityName,
                DurationMinutes = c.DurationMinutes,
                DistanceKm = c.DistanceKm,
                Intensity = c.Intensity
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

    public async Task UpdateExerciseLogAsync(int logId, int reps, double weightKg)
    {
        var connection = await GetConnectionAsync();
        var log = await connection.Table<ExerciseLog>().Where(x => x.Id == logId).FirstOrDefaultAsync();
        if (log is null) return;

        log.RepsCompleted = reps;
        log.WeightCompletedKg = weightKg;
        log.WasEdited = true;
        await connection.UpdateAsync(log);
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

    public async Task AddCardioLogAsync(DateTime date, string activityName, int durationMinutes, double? distanceKm, CardioIntensity intensity)
    {
        var connection = await GetConnectionAsync();
        await connection.InsertAsync(new CardioLog
        {
            DateLogged = date.Date,
            ActivityName = activityName,
            DurationMinutes = durationMinutes,
            DistanceKm = distanceKm,
            Intensity = intensity
        });
    }

    public async Task DeleteCardioLogAsync(int id)
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAsync<CardioLog>(id);
    }

    // Replaces that day's entry if one already exists, so correcting today's
    // weigh-in doesn't leave a second row behind.
    public async Task LogBodyWeightAsync(DateTime date, double weightKg)
    {
        var connection = await GetConnectionAsync();
        var day = date.Date;

        var existing = await connection.Table<BodyWeightLog>().Where(x => x.DateLogged == day).FirstOrDefaultAsync();
        if (existing is not null)
        {
            existing.WeightKg = weightKg;
            await connection.UpdateAsync(existing);
        }
        else
        {
            await connection.InsertAsync(new BodyWeightLog { DateLogged = day, WeightKg = weightKg });
        }
    }

    // Reuses ProgressionRecord/WeightProgressionDrawable as-is - both are
    // already just "date + weight", the same shape body weight is.
    public async Task<List<ProgressionRecord>> GetBodyWeightHistoryAsync()
    {
        var connection = await GetConnectionAsync();
        var logs = await connection.Table<BodyWeightLog>().OrderBy(x => x.DateLogged).ToListAsync();

        var records = new List<ProgressionRecord>();
        double? previousWeight = null;

        foreach (var log in logs)
        {
            double? difference = previousWeight is null ? null : log.WeightKg - previousWeight;

            records.Add(new ProgressionRecord
            {
                LogDate = log.DateLogged,
                WeightAchieved = log.WeightKg,
                WeightDifference = difference,
                DisplayDetails = $"{log.WeightKg:0.#}kg"
            });

            previousWeight = log.WeightKg;
        }

        records.Reverse();
        return records;
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
                Id = log.Id,
                LogDate = log.DateLogged,
                RepsAchieved = log.RepsCompleted,
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
        public List<WorkoutSectionDisplay> Sections { get; init; } = [];
        public List<CardioLogDisplay> CardioLogs { get; init; } = [];
    }

    // One scheduled Workout's own exercises for the day - a day can have
    // several of these (e.g. Physio + Gym) shown as separate groups.
    public sealed class WorkoutSectionDisplay
    {
        public int WorkoutId { get; init; }
        public string WorkoutName { get; init; } = string.Empty;
        public DayType Category { get; init; }
        public List<ExerciseDisplay> Exercises { get; init; } = [];
    }

    public sealed class ExerciseDisplay
    {
        public string ExerciseName { get; init; } = string.Empty;
        public int TargetSets { get; init; }
        public double TargetWeightKg { get; init; }
        public string PreviousPerformance { get; init; } = string.Empty;
        public List<LoggedSetDisplay> TodaysSets { get; init; } = [];
        public bool IsCompleted { get; init; }

        public bool LoggedToday => TodaysSets.Count > 0;

        public string TargetsDisplay => TargetWeightKg > 0
            ? $"{TargetSets} sets @ {TargetWeightKg:0.#}kg"
            : $"{TargetSets} sets";
    }

    public sealed class LoggedSetDisplay
    {
        public int Id { get; init; }
        public int SetNumber { get; init; }
        public int Reps { get; init; }
        public double Weight { get; init; }
        public DateTime DateLogged { get; init; }
        public bool WasEdited { get; init; }

        public string DisplayText => $"Set {SetNumber}: {Reps} reps @ {Weight:0.#}kg";
    }

    public sealed class CardioLogDisplay
    {
        public int Id { get; init; }
        public string ActivityName { get; init; } = string.Empty;
        public int DurationMinutes { get; init; }
        public double? DistanceKm { get; init; }
        public CardioIntensity Intensity { get; init; }

        public string DisplayText => DistanceKm.HasValue
            ? $"{ActivityName} - {DurationMinutes} min, {DistanceKm:0.##} km ({Intensity})"
            : $"{ActivityName} - {DurationMinutes} min ({Intensity})";
    }

    public class ProgressionRecord
{
    public int Id { get; set; }
    public DateTime LogDate { get; set; }
    public int RepsAchieved { get; set; }
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