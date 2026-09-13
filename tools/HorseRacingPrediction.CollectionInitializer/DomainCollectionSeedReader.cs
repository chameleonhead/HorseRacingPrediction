using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.CollectionInitializer;

public sealed class DomainCollectionSeedReader(string domainDatabasePath)
{
    public async Task<IReadOnlyList<CollectionInitializationSeed>> ReadAsync(
        DateTimeOffset importedAt, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(domainDatabasePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Domain database was not found.", fullPath);
        var seeds = new List<CollectionInitializationSeed>();
        await using var connection = new SqliteConnection($"Data Source={fullPath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ReadRacesAsync(connection, seeds, importedAt, cancellationToken).ConfigureAwait(false);
        await ReadProfilesAsync(connection, seeds, cancellationToken).ConfigureAwait(false);
        return seeds;
    }

    private static async Task ReadRacesAsync(SqliteConnection connection, List<CollectionInitializationSeed> seeds,
        DateTimeOffset importedAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RaceId, RaceDate, RacecourseCode, RaceNumber, EntryCount, ResultDeclaredAt FROM RaceSummaries;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var raceId = reader.GetString(0);
            DateOnly? date = reader.IsDBNull(1) ? null : DateOnly.Parse(reader.GetString(1));
            var attributes = new Dictionary<string, string> { ["domainRaceId"] = raceId };
            if (!reader.IsDBNull(2)) attributes["course"] = reader.GetString(2);
            if (!reader.IsDBNull(3)) attributes["number"] = reader.GetInt32(3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (date.HasValue && attributes.TryGetValue("course", out var course)
                && attributes.TryGetValue("number", out var number))
            {
                var parsed = importedAt;
                var resultDeclared = !reader.IsDBNull(5)
                    && DateTimeOffset.TryParse(reader.GetString(5), out parsed);
                var collectedAt = resultDeclared ? parsed : importedAt;
                seeds.Add(new(new(ResourceType.Race, "JRA", $"{date:yyyyMMdd}:{CanonicalCourse(course)}:{number}"),
                    new("race-detail"), 1, collectedAt, date, attributes, IsComplete: resultDeclared));
            }
        }
    }

    private static string CanonicalCourse(string value) => value switch
    {
        "札幌" => "Sapporo", "函館" => "Hakodate", "福島" => "Fukushima", "新潟" => "Niigata",
        "東京" => "Tokyo", "中山" => "Nakayama", "中京" => "Chukyo", "京都" => "Kyoto",
        "阪神" => "Hanshin", "小倉" => "Kokura", _ => value,
    };

    private static async Task ReadProfilesAsync(SqliteConnection connection, List<CollectionInitializationSeed> seeds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.SubjectId, p.SourceUrl, p.AcquiredAt,
                   CASE WHEN h.HorseId IS NOT NULL THEN 'Horse'
                        WHEN t.TrainerId IS NOT NULL THEN 'Trainer' END
            FROM JraSubjectProfileReadModel p
            LEFT JOIN Horses h ON h.HorseId = p.SubjectId
            LEFT JOIN Trainers t ON t.TrainerId = p.SubjectId;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(3) || !Uri.TryCreate(reader.GetString(1), UriKind.Absolute, out var sourceUrl)) continue;
            var type = reader.GetString(3) == "Horse" ? ResourceType.Horse : ResourceType.Trainer;
            var definition = type == ResourceType.Horse ? "horse-profile" : "trainer-profile";
            var acquiredAt = DateTimeOffset.Parse(reader.GetString(2));
            seeds.Add(new(new(type, "JRA", reader.GetString(0)), new(definition), 1,
                acquiredAt, null, new Dictionary<string, string>(), sourceUrl));
        }
    }
}
