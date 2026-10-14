using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

const int ProcessTimeoutSeconds = 60;
const int OuterTimeoutMinutes = 10;
const string V6Commit = "e170c4b37bf6200325e41803e6e8b81dc0493897";
const string V18Commit = "8bbee4b06a54717c4c67744e5af8bb38c1203c61";
var historicalUpgradeCommits = new[]
{
    "94440774", "7eabe38d", "c59da8ee", "c424e326", "7d25c7bd",
    "428cb07c", "7b0208b9", "6a71fc3a", "7451f7e1", "e14e5cd2", V18Commit,
};

var repository = FindRepositoryRoot();
var arguments = args.ToArray();
if (arguments is ["--self-check"])
{
    await RunSelfCheckAsync();
    return;
}
if (arguments is ["--spawn-grandchild"])
{
    using var child = Process.Start(new ProcessStartInfo("powershell",
        "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 5")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    });
    return;
}

if (arguments.Length != 1)
{
    Console.Error.WriteLine("Usage: CollectionHistoricalSchemaFixtures <output-directory>");
    Environment.ExitCode = 2;
    return;
}

var outputDirectory = Path.GetFullPath(arguments[0]);
Directory.CreateDirectory(outputDirectory);
using var outerTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(OuterTimeoutMinutes));
try
{
    await ProduceAsync(repository, outputDirectory, "v18", V18Commit, addRaceRepairHold: true,
        historicalUpgradeCommits, outerTimeout.Token);
    await ProduceAsync(repository, outputDirectory, "v6", V6Commit, addRaceRepairHold: false,
        historicalUpgradeCommits, outerTimeout.Token);
    Environment.ExitCode = 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Fixture generation exceeded the bounded time limit.");
    Environment.ExitCode = 2;
}
catch (Exception)
{
    Console.Error.WriteLine("Fixture generation failed; inspect the bounded command exit status.");
    Environment.ExitCode = 1;
}

static async Task ProduceAsync(string repository, string outputDirectory, string label, string commit,
    bool addRaceRepairHold, IReadOnlyList<string> historicalUpgradeCommits,
    CancellationToken cancellationToken)
{
    var work = Path.Combine(Path.GetTempPath(), "hrp-t14-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try
    {
        var archive = Path.Combine(work, "source.tar");
        var source = Path.Combine(work, "source");
        var runner = Path.Combine(work, "runner");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(runner);

        Console.WriteLine($"fixture={label};stage=archive");
        await RunProcessAsync(repository, "git", $"archive --format=tar --output=\"{archive}\" {commit}",
            ProcessTimeoutSeconds, cancellationToken);
        Console.WriteLine($"fixture={label};stage=extract");
        await RunProcessAsync(repository, "tar", $"-xf \"{archive}\" -C \"{source}\"",
            ProcessTimeoutSeconds, cancellationToken);

        var database = Path.Combine(work, $"{label}-pre-v19.db");
        Console.WriteLine($"fixture={label};stage=restore");
        await WriteHistoricalRunnerAsync(runner, source, database, addRaceRepairHold, seed: true,
            cancellationToken);
        var producerProject = Path.Combine(runner, "HistoricalFixtureProducer.csproj");
        await RunProcessAsync(runner, "dotnet", $"restore \"{producerProject}\" --ignore-failed-sources",
            ProcessTimeoutSeconds, cancellationToken);
        Console.WriteLine($"fixture={label};stage=build");
        await RunProcessAsync(runner, "dotnet", $"build \"{producerProject}\" -c Release --no-restore",
            ProcessTimeoutSeconds, cancellationToken);
        Console.WriteLine($"fixture={label};stage=produce");
        await RunProcessAsync(runner, "dotnet", $"run --project \"{producerProject}\" -c Release --no-build -- \"{database}\" {(addRaceRepairHold ? "v18" : "v6")} seed",
            ProcessTimeoutSeconds, cancellationToken);

        Console.WriteLine($"fixture={label};stage=preflight");
        var preSchema = await ReadSchemaAsync(database, cancellationToken);
        if (preSchema.HasFairnessTable || preSchema.History.Count != 1
            || preSchema.History[0] != (addRaceRepairHold ? 18 : 6))
            throw new InvalidDataException("Historical producer did not create the expected pre-v19 schema.");

        var sourceBeforeUpgradeHash = await Sha256Async(database, cancellationToken);
        var upgraded = Path.Combine(work, $"{label}-upgraded.db");
        File.Copy(database, upgraded, overwrite: true);
        if (!addRaceRepairHold)
        {
            foreach (var upgradeCommit in historicalUpgradeCommits)
                await RunHistoricalUpgradeAsync(repository, work, upgraded, upgradeCommit, cancellationToken);
        }
        Console.WriteLine($"fixture={label};stage=current-upgrade");
        try
        {
            _ = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = work,
                DatabaseFileName = Path.GetFileName(upgraded),
            }));
        }
        catch (Exception exception)
        {
            var sqlite = exception as SqliteException;
            Console.WriteLine($"fixture={label};stage=current-upgrade-failed;exception={exception.GetType().Name};inner={exception.InnerException?.GetType().Name ?? "none"};sqliteCode={sqlite?.SqliteErrorCode.ToString() ?? "none"};sqliteExtended={sqlite?.SqliteExtendedErrorCode.ToString() ?? "none"}");
            throw;
        }

        var postSchema = await ReadSchemaAsync(upgraded, cancellationToken);
        Console.WriteLine($"fixture={label};stage=postflight");
        var sourceAfterUpgradeHash = await Sha256Async(database, cancellationToken);
        var sentinelPreserved = await ReadSentinelCreatedAtAsync(upgraded, cancellationToken)
            == "2026-10-14 00:00:00";
        var holdPresent = addRaceRepairHold && await HasRaceRepairHoldAsync(upgraded, cancellationToken);
        var holdPreserved = !addRaceRepairHold || holdPresent;
        Console.WriteLine($"fixture={label};post-check=preFairness:{preSchema.HasFairnessTable};preHistory:{preSchema.History.Count};postFairness:{postSchema.HasFairnessTable};postLast:{postSchema.History.LastOrDefault()};sentinel:{sentinelPreserved};hold:{holdPresent};sourceUnchanged:{string.Equals(sourceBeforeUpgradeHash, sourceAfterUpgradeHash, StringComparison.OrdinalIgnoreCase)}");
        if (!postSchema.HasFairnessTable || postSchema.History.Count == 0
            || postSchema.History[^1] != 25 || !sentinelPreserved || !holdPreserved
            || !string.Equals(sourceBeforeUpgradeHash, sourceAfterUpgradeHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Current upgrade did not preserve the bounded source evidence.");

        Console.WriteLine($"fixture={label};stage=compress");
        var fixturePath = Path.Combine(outputDirectory, $"{label}-{commit[..8]}.sqlite.gz");
        await WriteGzipAsync(upgraded, fixturePath, cancellationToken);
        var sourcePaths = new[]
        {
            "src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformSchemaMigrator.cs",
            "src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformDbContext.cs",
            "src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionEntities.cs",
        };
        var sourceHashes = sourcePaths.ToDictionary(path => path, path =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, path)))),
            StringComparer.Ordinal);
        var provenance = new
        {
            fixture = Path.GetFileName(fixturePath),
            sourceCommit = commit,
            sourcePaths,
            sourceHashes,
            historicalVersion = addRaceRepairHold ? 18 : 6,
            currentVersion = 25,
            sqliteVersion = postSchema.SqliteVersion,
            preUpgrade = new
            {
                schemaSha256 = preSchema.Hash,
                history = preSchema.History,
                fairnessTablePresent = preSchema.HasFairnessTable,
                databaseSha256 = sourceBeforeUpgradeHash,
            },
            postUpgrade = new
            {
                schemaSha256 = postSchema.Hash,
                history = postSchema.History,
                fairnessTablePresent = postSchema.HasFairnessTable,
                databaseSha256 = await Sha256Async(upgraded, cancellationToken),
            },
            sourceBytesUnchanged = string.Equals(sourceBeforeUpgradeHash, sourceAfterUpgradeHash,
                StringComparison.OrdinalIgnoreCase),
            sentinelCreatedAtPreserved = sentinelPreserved,
            raceRepairHoldPreserved = holdPreserved,
            raceRepairHoldPresent = holdPresent,
            fixtureSha256 = await Sha256Async(fixturePath, cancellationToken),
            generatedBy = "CollectionHistoricalSchemaFixtures",
            deterministicNote = "SQLite file bytes may vary by SQLite/.NET runtime; schema and sentinel digests are the contract.",
        };
        Console.WriteLine($"fixture={label};stage=provenance");
        var provenancePath = Path.Combine(outputDirectory, $"{label}-{commit[..8]}.provenance.json");
        await File.WriteAllTextAsync(provenancePath,
            JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            cancellationToken);
    }
    finally
    {
        TryDeleteDirectory(work);
    }
}

static async Task RunHistoricalUpgradeAsync(string repository, string work, string database,
    string commit, CancellationToken cancellationToken)
{
    var label = "upgrade-" + commit[..8];
    var source = Path.Combine(work, label, "source");
    var runner = Path.Combine(work, label, "runner");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(runner);
    var archive = Path.Combine(work, label + ".tar");
    Console.WriteLine($"stage={label};archive");
    await RunProcessAsync(repository, "git", $"archive --format=tar --output=\"{archive}\" {commit}",
        ProcessTimeoutSeconds, cancellationToken);
    await RunProcessAsync(repository, "tar", $"-xf \"{archive}\" -C \"{source}\"",
        ProcessTimeoutSeconds, cancellationToken);
    await WriteHistoricalRunnerAsync(runner, source, database, addRaceRepairHold: false, seed: false,
        cancellationToken);
    var project = Path.Combine(runner, "HistoricalFixtureProducer.csproj");
    await RunProcessAsync(runner, "dotnet", $"restore \"{project}\" --ignore-failed-sources",
        ProcessTimeoutSeconds, cancellationToken);
    await RunProcessAsync(runner, "dotnet", $"build \"{project}\" -c Release --no-restore",
        ProcessTimeoutSeconds, cancellationToken);
    await RunProcessAsync(runner, "dotnet", $"run --project \"{project}\" -c Release --no-build -- \"{database}\" upgrade",
        ProcessTimeoutSeconds, cancellationToken);
}

static async Task WriteHistoricalRunnerAsync(string runner, string source, string database,
    bool addRaceRepairHold, bool seed, CancellationToken cancellationToken)
{
    var operationsProject = Path.Combine(source,
        "src", "HorseRacingPrediction.CollectionOperations", "HorseRacingPrediction.CollectionOperations.csproj");
    var project = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include=""Microsoft.AspNetCore.App"" />
    <ProjectReference Include=""{EscapeXml(operationsProject)}"" />
  </ItemGroup>
</Project>
";
    var program = """"
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

var database = Path.GetFullPath(args[0]);
var addRaceRepairHold = string.Equals(args[1], "v18", StringComparison.Ordinal);
var seed = args.Length > 2 && string.Equals(args[2], "seed", StringComparison.Ordinal);
var options = Options.Create(new CollectionPlatformOptions
{
    StateDirectory = Path.GetDirectoryName(database)!,
    DatabaseFileName = Path.GetFileName(database),
});
_ = new CollectionPlatformStore(options);
if (!seed) return;
await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = database,
    Pooling = false,
}.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();
await using (var command = connection.CreateCommand())
{
    command.Transaction = (SqliteTransaction)transaction;
    command.CommandText = """
        INSERT INTO collection_resources
            (Type, Provider, ResourceId, EffectiveDate, AttributesJson, CreatedAt)
        VALUES ('Horse', 'JRA', 't14-sentinel', NULL, '{"sourceIdentity":"t14-sentinel"}', '2026-10-14 00:00:00');
        """;
    await command.ExecuteNonQueryAsync();
}
if (addRaceRepairHold)
{
    await using var command = connection.CreateCommand();
    command.Transaction = (SqliteTransaction)transaction;
    command.CommandText = """
        INSERT INTO race_repair_holds
            (RaceId, Generation, OperationId, Reason, CreatedAt, ReleasedAt, AssignmentFingerprint, ReleaseOperationId)
        VALUES ('t14-race', 1, 't14-op', 'fixture', '2026-10-14 00:00:00', NULL, NULL, NULL);
        """;
    await command.ExecuteNonQueryAsync();
}
await transaction.CommitAsync();
"""";
    await File.WriteAllTextAsync(Path.Combine(runner, "HistoricalFixtureProducer.csproj"), project, cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(runner, "Program.cs"), program, cancellationToken);
}

static async Task RunSelfCheckAsync()
{
    try
    {
        await RunProcessAsync(Environment.CurrentDirectory, "powershell",
            "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 5\"", 1, CancellationToken.None);
        throw new InvalidOperationException("Timeout self-check did not time out.");
    }
    catch (TimeoutException)
    {
        Console.WriteLine("timeout-self-check=passed");
    }

    try
    {
        var childAssembly = Path.Combine(AppContext.BaseDirectory, "CollectionHistoricalSchemaFixtures.dll");
        var launcher = Environment.ProcessPath!;
        var childArguments = launcher.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? "--spawn-grandchild"
            : $"\"{childAssembly}\" --spawn-grandchild";
        await RunProcessAsync(Environment.CurrentDirectory, Environment.ProcessPath!, childArguments,
            1, CancellationToken.None);
        throw new InvalidOperationException("Inherited-pipe drain self-check did not time out.");
    }
    catch (TimeoutException)
    {
        Console.WriteLine("normal-exit-drain-self-check=passed");
    }
}

static async Task RunProcessAsync(string workingDirectory, string fileName, string arguments,
    int timeoutSeconds, CancellationToken cancellationToken)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        },
    };
    if (!process.Start()) throw new InvalidOperationException("Child process did not start.");
    process.StandardInput.Close();
    var stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
    var stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(cleanup.Token); } catch { }
        try { await Task.WhenAll(stdoutDrain, stderrDrain).WaitAsync(cleanup.Token); } catch { }
        if (cancellationToken.IsCancellationRequested) throw;
        throw new TimeoutException("Child process exceeded its bounded timeout.");
    }
    using var drainTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    drainTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
    try
    {
        await Task.WhenAll(stdoutDrain, stderrDrain).WaitAsync(drainTimeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(cleanup.Token); } catch { }
        try { await Task.WhenAll(stdoutDrain, stderrDrain).WaitAsync(cleanup.Token); } catch { }
        if (cancellationToken.IsCancellationRequested) throw;
        throw new TimeoutException("Child process exited but redirected streams exceeded their bounded drain time.");
    }
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Bounded child process failed with exit code {process.ExitCode}.");
}

static async Task<SchemaSnapshot> ReadSchemaAsync(string database, CancellationToken cancellationToken)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = database,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
    }.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    var sqliteVersion = await ScalarStringAsync(connection, "SELECT sqlite_version();", cancellationToken);
    var builder = new StringBuilder();
    var names = new List<string>();
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT type, name, tbl_name, COALESCE(sql, '') FROM sqlite_master WHERE name NOT GLOB 'sqlite_*' ORDER BY type, name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            builder.Append(reader.GetString(0)).Append('\u001f').Append(reader.GetString(1)).Append('\u001f')
                .Append(reader.GetString(2)).Append('\u001f').Append(reader.GetString(3)).Append('\n');
            if (reader.GetString(0) == "table") names.Add(reader.GetString(1));
        }
    }
    foreach (var name in names.Order(StringComparer.Ordinal))
    {
        builder.Append("table_xinfo:").Append(name).Append('\n');
        await AppendPragmaAsync(connection, $"PRAGMA table_xinfo({Quote(name)});", builder, cancellationToken);
        await AppendPragmaAsync(connection, $"PRAGMA foreign_key_list({Quote(name)});", builder, cancellationToken);
        await AppendPragmaAsync(connection, $"PRAGMA index_list({Quote(name)});", builder, cancellationToken);
        await AppendIndexMetadataAsync(connection, name, builder, cancellationToken);
    }
    var history = new List<int>();
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT version FROM collection_schema_history ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) history.Add(reader.GetInt32(0));
    }
    return new SchemaSnapshot(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))),
        history, names.Contains("collection_dispatcher_fairness_state", StringComparer.Ordinal), sqliteVersion);
}

static async Task AppendPragmaAsync(SqliteConnection connection, string sql, StringBuilder builder,
    CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        for (var i = 0; i < reader.FieldCount; i++)
            builder.Append(reader.IsDBNull(i) ? "<null>" : reader.GetValue(i)).Append('\u001f');
        builder.Append('\n');
    }
}

static async Task AppendIndexMetadataAsync(SqliteConnection connection, string table, StringBuilder builder,
    CancellationToken cancellationToken)
{
    var indexes = new List<string>();
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $table AND name NOT GLOB 'sqlite_*' ORDER BY name;";
        command.Parameters.AddWithValue("$table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) indexes.Add(reader.GetString(0));
    }
    foreach (var index in indexes)
    {
        builder.Append("index_xinfo:").Append(index).Append('\n');
        await AppendPragmaAsync(connection, $"PRAGMA index_xinfo({Quote(index)});", builder, cancellationToken);
    }
}

static async Task<string?> ReadSentinelCreatedAtAsync(string database, CancellationToken cancellationToken)
{
    await using var connection = await OpenReadOnlyAsync(database, cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT CreatedAt FROM collection_resources WHERE ResourceId = $id;";
    command.Parameters.AddWithValue("$id", "t14-sentinel");
    return (string?)await command.ExecuteScalarAsync(cancellationToken);
}

static async Task<bool> HasRaceRepairHoldAsync(string database, CancellationToken cancellationToken)
{
    await using var connection = await OpenReadOnlyAsync(database, cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM race_repair_holds WHERE RaceId = $id AND Generation = 1;";
    command.Parameters.AddWithValue("$id", "t14-race");
    return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
}

static async Task<SqliteConnection> OpenReadOnlyAsync(string database, CancellationToken cancellationToken)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = database,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
    }.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    return connection;
}

static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "unknown";
}

static async Task WriteGzipAsync(string input, string output, CancellationToken cancellationToken)
{
    await using var source = File.OpenRead(input);
    await using var destination = File.Create(output);
    await using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize);
    await source.CopyToAsync(gzip, cancellationToken);
}

static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
{
    await using var stream = File.OpenRead(path);
    var hash = await SHA256.HashDataAsync(stream, cancellationToken);
    return Convert.ToHexString(hash);
}

static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

static string EscapeXml(string value) => System.Security.SecurityElement.Escape(value) ?? value;

static string FindRepositoryRoot()
{
    var path = Directory.GetCurrentDirectory();
    while (path is not null && !Directory.Exists(Path.Combine(path, ".git")))
        path = Directory.GetParent(path)?.FullName;
    return path ?? throw new DirectoryNotFoundException("Repository root not found.");
}

static void TryDeleteDirectory(string path)
{
    try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
}

file sealed record SchemaSnapshot(string Hash, IReadOnlyList<int> History, bool HasFairnessTable, string SqliteVersion);
