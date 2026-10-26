[CmdletBinding()]
param(
    [string] $Docker = 'docker',
    [string] $Dotnet = 'dotnet',
    [switch] $WrapperSmokeOnly,
    [switch] $ObserverSmokeOnly,
    [switch] $FastLifecycleSmokeOnly,
    [ValidateSet('127.0.0.1', 'host.docker.internal')]
    [string] $PublishedHost = '127.0.0.1'
)
$smokeModeCount = [int]$WrapperSmokeOnly.IsPresent + [int]$ObserverSmokeOnly.IsPresent + [int]$FastLifecycleSmokeOnly.IsPresent
if ($smokeModeCount -gt 1) {
    throw 'Wrapper, observer, and fast-lifecycle smoke modes are mutually exclusive.'
}
if ($FastLifecycleSmokeOnly) {
    $harnessMode = 'fast-lifecycle-smoke-only'
    Write-Output 'MODE aged-pause FAST lifecycle preflight (not a 900-second timing proof)'
} elseif ($WrapperSmokeOnly) {
    $harnessMode = 'wrapper-smoke-only'
    Write-Output 'MODE bounded process-wrapper smoke'
} elseif ($ObserverSmokeOnly) {
    $harnessMode = 'observer-smoke-only'
    Write-Output 'MODE fixed-CLI observer smoke'
} else {
    $harnessMode = 'real-900-second-lifecycle'
    Write-Output 'MODE full real 900-second lifecycle gate'
}
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$dockerCommand = (Get-Command $Docker -ErrorAction Stop).Source
$dotnetCommand = (Get-Command $Dotnet -ErrorAction Stop).Source
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runId = [guid]::NewGuid().ToString('N')
$tempRoot = Join-Path $tempParent "hrp-horse-id-supervisor-$runId"
$image = "hrp-horse-id-supervisor-test:$runId"
$happyContainer = "hrp-horse-id-supervisor-happy-$runId"
$crashContainer = "hrp-horse-id-supervisor-crash-$runId"
$happyData = Join-Path $tempRoot 'happy-data'
$crashData = Join-Path $tempRoot 'crash-data'
$evidenceRoot = Join-Path $tempRoot 'evidence'
$evidenceTranscript = Join-Path $evidenceRoot 'lifecycle.jsonl'
$testProject = Join-Path $repositoryRoot 'tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj'
$fixtureMethod = 'FullyQualifiedName~ExportProductionContainerFixtureWhenRequested'
$oldHorseId = 'horse-11111111-1111-4111-8111-111111111111'
$jraIdentity = 'jra-horse:2024100828'
$maxOutputCharacters = 64 * 1024
$harnessSucceeded = $false
$happyCliHeld = $false
$sensitiveValues = [Collections.Generic.List[string]]::new()

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

public sealed class HrpBoundedProcessResult
{
    public int ExitCode { get; set; }
    public int ProcessId { get; set; }
    public bool TimedOut { get; set; }
    public string Output { get; set; } = String.Empty;
}

public static class HrpBoundedProcess
{
    public static HrpBoundedProcessResult Run(string filePath, string workingDirectory, string[] arguments,
        int timeoutSeconds, int maximumCharacters, string[] environment)
    {
        var start = new ProcessStartInfo(filePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var entry in environment)
        {
            var separator = entry.IndexOf('=');
            if (separator > 0) start.Environment[entry.Substring(0, separator)] = entry.Substring(separator + 1);
        }

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var outputLock = new object();
        void Append(string line)
        {
            lock (outputLock)
            {
                if (output.Length >= maximumCharacters) return;
                var remaining = maximumCharacters - output.Length;
                output.Append(line.AsSpan(0, Math.Min(line.Length, remaining))).AppendLine();
            }
        }
        async Task DrainAsync(System.IO.StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) Append(line);
        }

        if (!process.Start()) throw new InvalidOperationException("The bounded child process did not start.");
        var processId = process.Id;
        process.StandardInput.Close();
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        var result = new HrpBoundedProcessResult();
        result.ProcessId = processId;
        if (!process.WaitForExit(checked(timeoutSeconds * 1000)))
        {
            result.TimedOut = true;
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(10000))
                throw new TimeoutException("The bounded child process tree remained alive after termination.");
        }
        if (!Task.WaitAll(new[] { stdout, stderr }, TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The bounded child output streams did not reach EOF.");
        result.ExitCode = process.ExitCode;
        lock (outputLock) result.Output = output.ToString();
        return result;
    }
}
'@ -ErrorAction Stop

function Invoke-External([string] $FilePath, [string[]] $Arguments, [int] $TimeoutSeconds = 60,
    [hashtable] $Environment = @{}) {
    $environmentEntries = @($Environment.Keys | ForEach-Object { "$_=$([string]$Environment[$_])" })
    $result = [HrpBoundedProcess]::Run($FilePath, $repositoryRoot, $Arguments, $TimeoutSeconds,
        $maxOutputCharacters, $environmentEntries)
    if ($result.TimedOut) {
        throw "Test process '$FilePath' exceeded its ${TimeoutSeconds}s bound (pid $($result.ProcessId)). $($result.Output)"
    }
    if ($result.ExitCode -ne 0) {
        throw "Test process '$FilePath' exited $($result.ExitCode). $($result.Output)"
    }
    return $result.Output
}

function Test-ExternalProcessWrapper {
    $powerShellPath = (Get-Process -Id $PID).Path
    $probe = Invoke-External $powerShellPath @('-NoProfile', '-Command',
        "[Console]::Out.WriteLine('bounded-stdout-proof'); [Console]::Error.WriteLine('bounded-stderr-proof')") 15
    if (-not $probe.Contains('bounded-stdout-proof') -or -not $probe.Contains('bounded-stderr-proof')) {
        throw 'The bounded child wrapper did not capture deterministic stdout and stderr.'
    }

    $markerPath = Join-Path $tempRoot 'stalled-child.pid'
    $probeRoot = Join-Path $tempRoot 'process-tree-probe'
    [void](New-Item -ItemType Directory -Path $probeRoot)
    $projectPath = Join-Path $probeRoot 'ProcessTreeProbe.csproj'
    $sourcePath = Join-Path $probeRoot 'Program.cs'
    $helperPath = Join-Path $probeRoot 'bin/Release/net10.0/ProcessTreeProbe.dll'
    Set-Content -LiteralPath $projectPath -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath $sourcePath -Value @'
using System.Diagnostics;
using System.Runtime.InteropServices;

if (args[0] == "parent")
{
    var parentId = Environment.ProcessId;
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { args[2], "child", args[3], parentId.ToString() })
        start.ArgumentList.Add(argument);
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
    var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
    while (!File.Exists(args[3]) && DateTimeOffset.UtcNow < deadline) Thread.Sleep(25);
    if (!File.Exists(args[3])) return 4;
    Thread.Sleep(Timeout.Infinite);
}
else if (args[0] == "child")
{
    var expectedParent = int.Parse(args[2]);
    var actualParent = Native.GetParentProcessId();
    File.WriteAllText(args[1], $"{expectedParent};{Environment.ProcessId};{actualParent}");
    Thread.Sleep(Timeout.Infinite);
}
return 2;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2A;
        internal IntPtr Reserved2B;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int informationClass,
        out ProcessBasicInformation information, int informationLength, out int returnLength);

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentProcessIdUnix();

    internal static int GetParentProcessId()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetParentProcessIdUnix();

        using var process = Process.GetCurrentProcess();
        var status = NtQueryInformationProcess(process.Handle, 0, out var information,
            Marshal.SizeOf<ProcessBasicInformation>(), out _);
        if (status != 0) throw new InvalidOperationException("Could not prove the child process parent identity.");
        return information.InheritedFromUniqueProcessId.ToInt32();
    }
}
'@
    [void](Invoke-External $dotnetCommand @('build', $projectPath, '-c', 'Release', '--nologo', '--verbosity', 'quiet') 120)
    $timedOut = $false
    $wrapperProcessId = 0
    $childId = 0
    $parentId = 0
    try {
        [void](Invoke-External $dotnetCommand @($helperPath, 'parent', $dotnetCommand, $helperPath, $markerPath) 5)
    } catch {
        $timedOut = $_.Exception.Message.Contains('exceeded its 5s bound')
        if (-not $timedOut) { throw }
        if ($_.Exception.Message -match '\(pid (\d+)\)') { $wrapperProcessId = [int]$Matches[1] }
    }
    if (-not $timedOut -or -not (Test-Path -LiteralPath $markerPath)) {
        throw 'The stalled direct-child wrapper did not hit its bound or record its disposable process tree.'
    }
    $identity = (Get-Content -Raw -LiteralPath $markerPath).Split(';')
    if ($identity.Count -ne 3) { throw 'The stalled process did not record its exact parent/child identity.' }
    $parentId = [int]$identity[0]
    $childId = [int]$identity[1]
    try {
        if ($wrapperProcessId -ne $parentId -or [int]$identity[2] -ne $parentId) {
            throw 'The timeout wrapper did not own the recorded direct parent/child process chain.'
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            $remaining = @(@($parentId, $childId) | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } |
                Where-Object { $_ -and -not $_.HasExited })
            if ($remaining.Count -eq 0) { return }
            Start-Sleep -Milliseconds 100
        }
    } finally {
        foreach ($ownedId in @($parentId, $childId)) {
            $owned = Get-Process -Id $ownedId -ErrorAction SilentlyContinue
            if ($owned -and -not $owned.HasExited) { Stop-Process -Id $ownedId -Force -ErrorAction SilentlyContinue }
        }
    }
    throw 'The bounded child wrapper did not terminate its verified direct child process tree.'
}

function Get-SourceFileFingerprint([string] $DataDirectory) {
    $paths = foreach ($name in @('eventstore.db', 'collection-platform.db', 'prediction-executions.db')) {
        foreach ($suffix in @('', '-wal', '-shm')) {
            $path = Join-Path $DataDirectory ($name + $suffix)
            if (Test-Path -LiteralPath $path) { $path }
        }
    }
    return @($paths | Sort-Object | ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_
        "$([IO.Path]::GetFileName($_))=$($hash.Hash)"
    }) -join "`n"
}

function Add-EvidenceEvent([string] $Event, [string] $OperationId = '', [string] $Phase = '',
    [string] $Status = '', [string] $Detail = '') {
    $entry = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        event = $Event
        operationId = $OperationId
        phase = $Phase
        status = $Status
        detail = $Detail
    }
    [IO.File]::AppendAllText($evidenceTranscript, (([pscustomobject]$entry | ConvertTo-Json -Compress) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

function Test-PendingPrewriteBlockedStatus($Status) {
    return ($null -ne $Status -and
        [string]$Status.requestKind -ceq 'PendingV2' -and
        $null -eq $Status.previewFingerprint -and
        [string]$Status.executionPhase -ceq 'Applying' -and
        [string]$Status.status -ceq 'AwaitingActions')
}

function Test-PendingPrewriteBlockedStatusCounterexamples {
    $valid = [pscustomobject]@{
        requestKind = 'PendingV2'
        previewFingerprint = $null
        executionPhase = 'Applying'
        status = 'AwaitingActions'
    }
    if (-not (Test-PendingPrewriteBlockedStatus $valid)) {
        throw 'The exact post-restart PendingV2/Applying/AwaitingActions oracle rejected its valid response.'
    }

    $invalidResponses = @(
        [pscustomobject]@{ Name = 'Running status'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Running' } },
        [pscustomobject]@{ Name = 'Interrupted status'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Interrupted' } },
        [pscustomobject]@{ Name = 'Succeeded status'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Succeeded' } },
        [pscustomobject]@{ Name = 'unknown status'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'FutureStatus' } },
        [pscustomobject]@{ Name = 'missing request kind'; Value = [pscustomobject]@{
            previewFingerprint = $null; executionPhase = 'Applying'; status = 'AwaitingActions' } },
        [pscustomobject]@{ Name = 'unknown request kind'; Value = [pscustomobject]@{
            requestKind = 'FutureKind'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'AwaitingActions' } },
        [pscustomobject]@{ Name = 'non-null fingerprint'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = ('A' * 64); executionPhase = 'Applying'; status = 'AwaitingActions' } },
        [pscustomobject]@{ Name = 'wrong phase'; Value = [pscustomobject]@{
            requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'BackedUp'; status = 'AwaitingActions' } },
        [pscustomobject]@{ Name = 'missing response'; Value = $null }
    )
    foreach ($case in $invalidResponses) {
        if (Test-PendingPrewriteBlockedStatus $case.Value) {
            throw "The post-restart prewrite oracle accepted an invalid response: $($case.Name)."
        }
    }
    Write-Output "PASS exact post-restart prewrite-status oracle: positive plus $($invalidResponses.Count) rejection cases"
}

function Test-SafelyClassifiedChildFailureStatus($Status) {
    if ($null -eq $Status -or
        [string]$Status.requestKind -cne 'PendingV2' -or
        $null -ne $Status.previewFingerprint -or
        [string]$Status.executionPhase -cne 'Applying') { return $false }
    $classified = ([string]$Status.status -ceq 'Failed' -and [string]$Status.reason -ceq 'ChildFailed') -or
        ([string]$Status.status -ceq 'Partial' -and [string]$Status.reason -ceq 'OperationInterrupted')
    return $classified -and @($Status.results | Where-Object { [string]$_.code -ceq 'ChildFailed' }).Count -eq 1
}

function Test-SafelyClassifiedChildFailureStatusCounterexamples {
    $valid = @(
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Failed'; reason = 'ChildFailed'; results = @([pscustomobject]@{ code = 'ChildFailed' }) },
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Partial'; reason = 'OperationInterrupted'; results = @([pscustomobject]@{ code = 'ChildFailed' }) })
    foreach ($case in $valid) {
        if (-not (Test-SafelyClassifiedChildFailureStatus $case)) {
            throw 'The safe child-failure oracle rejected a supported terminal classification.'
        }
    }
    foreach ($case in @(
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'AwaitingActions'; reason = 'None'; results = @() },
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Failed'; reason = 'None'; results = @([pscustomobject]@{ code = 'ChildFailed' }) },
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Applying'; status = 'Partial'; reason = 'OperationInterrupted'; results = @() },
        [pscustomobject]@{ requestKind = 'PendingV2'; previewFingerprint = $null; executionPhase = 'Verified'; status = 'Succeeded'; reason = 'None'; results = @([pscustomobject]@{ code = 'ChildFailed' }) })) {
        if (Test-SafelyClassifiedChildFailureStatus $case) {
            throw 'The safe child-failure oracle accepted an unsafe or incomplete classification.'
        }
    }
    Write-Output 'PASS safe child-failure status oracle: Failed/ChildFailed or Partial/OperationInterrupted with ChildFailed result'
}

function Get-SourceHashes {
    $relativePaths = @(
        'tests/scripts/test-horse-id-migration-supervisor.ps1',
        'src/HorseRacingPrediction.Api/IdentityMigration/HorseIdentityMigrationJournal.cs',
        'src/HorseRacingPrediction.Api/IdentityMigration/HorseIdentityMigrationPreparationService.cs',
        'src/HorseRacingPrediction.Api/IdentityMigration/HorseIdentityMigrationSupervisor.cs',
        'src/HorseRacingPrediction.Api/IdentityMigration/HorseIdentityMigrationRuntimeMode.cs',
        'src/HorseRacingPrediction.Api/IdentityMigration/TypedHorseIdentityStoreEngine.cs',
        'src/HorseRacingPrediction.Api/IdentityMigration/TypedHorseIdentityStoreWriter.cs'
    )
    $hashes = [ordered]@{}
    foreach ($relativePath in $relativePaths) {
        $path = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Cannot record the required source identity; missing file: $relativePath"
        }
        $hashes[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    }
    return $hashes
}

function Invoke-Docker([string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    Invoke-External $dockerCommand $Arguments $TimeoutSeconds
}

function Invoke-ContainerProcRead([string] $Container, [string] $Path) {
    $probe = @'
path="$1"
if [ ! -e "$path" ]; then exit 44; fi
cat -- "$path" 2>/dev/null || {
  rc=$?
  [ ! -e "$path" ] && exit 44
  exit "$rc"
}
'@
    try {
        return Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $probe, 'hrp-proc-read', $Path) 15
    }
    catch {
        if ($_.Exception.Message -match "exited 44\.") {
            throw [IO.FileNotFoundException]::new("Confirmed process disappearance while reading $Path.")
        }
        throw
    }
}

function Invoke-ContainerTaskChildrenRead([string] $Container) {
    $probe = @'
set -eu
for task in /proc/1/task/*; do
  path="$task/children"
  [ -e "$path" ] || exit 44
  if value=$(cat -- "$path" 2>/dev/null); then
    printf '%s\n' "$value"
  else
    rc=$?
    [ ! -e "$path" ] && exit 44
    exit "$rc"
  fi
done
'@
    try {
        return Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $probe, 'hrp-task-children-read') 15
    }
    catch {
        if ($_.Exception.Message -match "exited 44\.") {
            throw [IO.FileNotFoundException]::new('Confirmed process disappearance while reading PID-1 task children.')
        }
        throw
    }
}

function Test-KnownApiGetTransportInterruption([Exception] $Exception) {
    for ($current = $Exception; $null -ne $current; $current = $current.InnerException) {
        if ($current -is [Net.Http.HttpRequestException] -and
            $current.HttpRequestError -eq [Net.Http.HttpRequestError]::ResponseEnded) {
            return $true
        }
        if ($current -is [Net.Sockets.SocketException] -and
            $current.SocketErrorCode -eq [Net.Sockets.SocketError]::ConnectionRefused) {
            return $true
        }
    }
    return $false
}

function Invoke-Api([string] $BaseUrl, [string] $Path, [string] $Method = 'GET', $Body = $null,
    [string] $ApiKey, [int] $GetDeadlineSeconds = 15) {
    $headers = @{ 'X-Api-Key' = $ApiKey }
    $uri = $BaseUrl + $Path
    if ($Method -cne 'GET') {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -TimeoutSec 15
        }
        $json = $Body | ConvertTo-Json -Depth 10 -Compress
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers `
            -ContentType 'application/json' -Body $json -TimeoutSec 15
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($GetDeadlineSeconds)
    $lastTransportException = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $remainingSeconds = ($deadline - [DateTimeOffset]::UtcNow).TotalSeconds
        if ($remainingSeconds -lt 1) { break }
        $timeoutSeconds = [Math]::Max(1, [Math]::Floor($remainingSeconds))
        try {
            return Invoke-RestMethod -Method GET -Uri $uri -Headers $headers -TimeoutSec $timeoutSeconds
        }
        catch {
            if (-not (Test-KnownApiGetTransportInterruption $_.Exception)) { throw }
            $lastTransportException = $_.Exception
            if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
            Start-Sleep -Milliseconds 50
        }
    }
    if ($null -ne $lastTransportException) { throw $lastTransportException }
    throw "The GET request exceeded its bounded ${GetDeadlineSeconds}s deadline without a response."
}

function Get-PublishedPort([string] $Container) {
    $result = (Invoke-Docker @('port', $Container, '8080/tcp')).Trim()
    if ($result -notmatch '^127\.0\.0\.1:(\d+)$') { throw "The disposable API port was not loopback-bound: $result" }
    return [int]$Matches[1]
}

function Wait-Api([string] $BaseUrl, [string] $ApiKey, [int] $TimeoutSeconds = 120) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri ($BaseUrl + '/health') -TimeoutSec 2
            if ($health.status -eq 'ok') { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "The isolated API did not become healthy within ${TimeoutSeconds}s."
}

function Get-UniqueProcessIdsFromTaskChildren([string[]] $TaskChildren) {
    $processIds = [Collections.Generic.HashSet[int]]::new()
    foreach ($childrenText in $TaskChildren) {
        foreach ($childText in ($childrenText -split '\s+' | Where-Object { $_ })) {
            $childPid = 0
            if (-not [int]::TryParse($childText, [ref]$childPid) -or $childPid -le 0) {
                throw "The PID-1 task children list contained an invalid PID: $childText"
            }
            [void]$processIds.Add($childPid)
        }
    }
    return @($processIds | Sort-Object)
}

function Test-TaskChildrenParserCounterexample {
    $children = @(Get-UniqueProcessIdsFromTaskChildren @('', '40 40', '41'))
    if (($children -join ',') -ne '40,41') {
        throw "The PID-1 task parser lost worker-thread children or failed to deduplicate: $($children -join ',')"
    }
}

function Test-IsFixedCliCandidate([int] $ParentProcessId, [string] $Command, [string] $OperationId) {
    $expected = "/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $OperationId"
    return $ParentProcessId -eq 1 -and $Command.TrimEnd() -ceq $expected
}

function Test-IsOperatorApiCandidate($Child) {
    return $Child.ParentLine -match '^PPid:\s+1$' -and
        $Child.Command -match '--internal-blocked-child' -and [bool]$Child.OperatorOnly
}

function Test-IsNormalApiCandidate($Child) {
    $expected = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child'
    return $Child.ParentLine -match '^PPid:\s+1$' -and
        $Child.Command.TrimEnd() -ceq $expected -and -not [bool]$Child.OperatorOnly
}

function Test-OperatorApiCandidateCounterexamples {
    $valid = [pscustomobject]@{
        ParentLine = 'PPid: 1'
        Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-blocked-child'
        OperatorOnly = $true
    }
    if (-not (Test-IsOperatorApiCandidate $valid)) {
        throw 'The observer rejected the exact operator-only blocked child.'
    }
    foreach ($invalid in @(
        [pscustomobject]@{ ParentLine = 'PPid: 1'; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child'; OperatorOnly = $true; Reason = 'API-child masquerade' },
        [pscustomobject]@{ ParentLine = 'PPid: 1'; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-blocked-child'; OperatorOnly = $false; Reason = 'missing operator flag' },
        [pscustomobject]@{ ParentLine = 'PPid: 2'; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-blocked-child'; OperatorOnly = $true; Reason = 'wrong parent' },
        [pscustomobject]@{ ParentLine = 'PPid: 1'; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration 11111111-2222-4333-8444-555555555555'; OperatorOnly = $true; Reason = 'fixed CLI' }
    )) {
        if (Test-IsOperatorApiCandidate $invalid) {
            throw "The observer accepted the invalid operator candidate: $($invalid.Reason)."
        }
    }
    Write-Output 'PASS operator-only blocked-child predicate and counterexamples'
}

function Test-FixedCliObserverCounterexamples {
    $operationId = '11111111-2222-4333-8444-555555555555'
    $target = "/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $operationId"
    if (-not (Test-IsFixedCliCandidate 1 $target $operationId)) {
        throw 'The observer rejected the exact fixed CLI direct child.'
    }
    if (Test-IsFixedCliCandidate 0 "/bin/sh -c $target" $operationId) {
        throw 'The observer could match its own PPid-0 shell command line.'
    }
    if (Test-IsFixedCliCandidate 2 $target $operationId) {
        throw 'The observer accepted the fixed CLI under the wrong parent.'
    }
    if (Test-IsFixedCliCandidate 1 $target 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee') {
        throw 'The observer accepted a different operation ID.'
    }
    if (Test-IsFixedCliCandidate 1 '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child' $operationId) {
        throw 'The observer accepted the API service child as the fixed CLI.'
    }
    if ((Get-FixedCliObserverWaitDecision 'pending-gone') -ne 'dead') {
        throw 'A dead observer without a terminal marker was not rejected promptly.'
    }
    if ((Get-FixedCliObserverWaitDecision 'pending-alive') -ne 'pending') {
        throw 'A live observer without a terminal marker was rejected prematurely.'
    }
    if ((Get-FixedCliObserverWaitDecision 'found 40 12345') -ne 'found') {
        throw 'A completed observer result was rejected after its process exited.'
    }
}

function Resolve-ProcReadDisposition([string] $Category, [bool] $PathMissing = $false,
    [bool] $IdentityMatches = $true, [bool] $Zombie = $false, [bool] $Retryable = $false) {
    switch ($Category) {
        'ENOENT' { if (-not $PathMissing) { throw 'ENOENT was not confirmed by a missing proc path.' }; return $(if ($Retryable) { 'retry' } else { 'gone' }) }
        'ESRCH' { if (-not $PathMissing) { throw 'ESRCH was not confirmed by a missing proc path.' }; return $(if ($Retryable) { 'retry' } else { 'gone' }) }
        'PIDReuse' { if ($IdentityMatches) { throw 'PID reuse was accepted without a changed start identity.' }; return 'gone' }
        'Zombie' { if (-not $Zombie) { throw 'A process was classified as zombie without a Z/X state.' }; return 'gone' }
        'Alive' { if (-not $IdentityMatches -or $Zombie) { throw 'A live process did not retain its exact identity.' }; return 'alive' }
        default { throw "Unclassified proc read failure: $Category" }
    }
}

function Test-ProcReadRaceCounterexamples {
    foreach ($case in @(
        [pscustomobject]@{ Name = 'ENOENT between status and stat'; Category = 'ENOENT'; PathMissing = $true; Retryable = $true; Expected = 'retry' },
        [pscustomobject]@{ Name = 'ESRCH after reaping'; Category = 'ESRCH'; PathMissing = $true; Retryable = $false; Expected = 'gone' },
        [pscustomobject]@{ Name = 'PID reuse after start mismatch'; Category = 'PIDReuse'; IdentityMatches = $false; Expected = 'gone' },
        [pscustomobject]@{ Name = 'zombie process'; Category = 'Zombie'; Zombie = $true; Expected = 'gone' },
        [pscustomobject]@{ Name = 'exact live identity'; Category = 'Alive'; IdentityMatches = $true; Expected = 'alive' })) {
        $pathMissing = if ($null -eq $case.PathMissing) { $false } else { [bool]$case.PathMissing }
        $identityMatches = if ($null -eq $case.IdentityMatches) { $true } else { [bool]$case.IdentityMatches }
        $zombie = if ($null -eq $case.Zombie) { $false } else { [bool]$case.Zombie }
        $retryable = if ($null -eq $case.Retryable) { $false } else { [bool]$case.Retryable }
        $actual = Resolve-ProcReadDisposition $case.Category $pathMissing $identityMatches $zombie $retryable
        if ($actual -cne $case.Expected) { throw "The proc-read race oracle returned '$actual' for $($case.Name), expected '$($case.Expected)'." }
    }
    foreach ($case in @(
        [pscustomobject]@{ Name = 'permission denied'; Category = 'PermissionDenied' },
        [pscustomobject]@{ Name = 'transport failure'; Category = 'TransportFailure' },
        [pscustomobject]@{ Name = 'malformed stat'; Category = 'MalformedStat' },
        [pscustomobject]@{ Name = 'unknown read failure'; Category = 'Unknown' },
        [pscustomobject]@{ Name = 'unconfirmed ENOENT'; Category = 'ENOENT'; PathMissing = $false })) {
        $failedClosed = $false
        $pathMissing = if ($null -eq $case.PathMissing) { $false } else { [bool]$case.PathMissing }
        try { [void](Resolve-ProcReadDisposition $case.Category $pathMissing) } catch { $failedClosed = $true }
        if (-not $failedClosed) { throw "The proc-read race oracle failed open for $($case.Name)." }
    }
    Write-Output 'PASS proc-read race oracle: confirmed ENOENT/ESRCH retry-or-gone, PID reuse/zombie, and fail-closed error counterexamples'
}

function Test-ApiTransportCounterexamples {
    $script:apiTransportMockMode = ''
    $script:apiTransportMockCalls = 0
    function Invoke-RestMethod {
        param(
            [string] $Method,
            [string] $Uri,
            [hashtable] $Headers,
            $Body,
            [string] $ContentType,
            [int] $TimeoutSec
        )
        $script:apiTransportMockCalls++
        switch ($script:apiTransportMockMode) {
            'TransientGet' {
                if ($script:apiTransportMockCalls -eq 1) {
                    throw [Net.Http.HttpRequestException]::new(
                        [Net.Http.HttpRequestError]::ResponseEnded, 'response ended', $null, $null)
                }
                return [pscustomobject]@{ status = 'ok' }
            }
            'PersistentGet' {
                throw [Net.Sockets.SocketException]::new([int][Net.Sockets.SocketError]::ConnectionRefused)
            }
            'SemanticFailure' {
                $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::ServiceUnavailable)
                throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('503', $response)
            }
            'UnknownFailure' { throw [InvalidOperationException]::new('unknown test failure') }
            'MutationTransport' {
                throw [Net.Http.HttpRequestException]::new(
                    [Net.Http.HttpRequestError]::ResponseEnded, 'response ended', $null, $null)
            }
            default { throw "Unexpected API transport mock mode: $($script:apiTransportMockMode)" }
        }
    }
    try {
        $script:apiTransportMockMode = 'TransientGet'
        $script:apiTransportMockCalls = 0
        $transient = Invoke-Api 'http://api.test' '/status' -ApiKey 'test-key' -GetDeadlineSeconds 2
        if ($transient.status -cne 'ok' -or $script:apiTransportMockCalls -ne 2) {
            throw 'A known ResponseEnded GET interruption did not reconnect exactly once.'
        }

        $script:apiTransportMockMode = 'PersistentGet'
        $script:apiTransportMockCalls = 0
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $persistentFailed = $false
        try { [void](Invoke-Api 'http://api.test' '/status' -ApiKey 'test-key' -GetDeadlineSeconds 1) }
        catch { $persistentFailed = $true }
        $stopwatch.Stop()
        if (-not $persistentFailed -or $stopwatch.Elapsed.TotalSeconds -gt 3) {
            throw 'A persistent known GET interruption did not fail within its bounded deadline.'
        }

        foreach ($mode in @('SemanticFailure', 'UnknownFailure')) {
            $script:apiTransportMockMode = $mode
            $script:apiTransportMockCalls = 0
            $failed = $false
            try { [void](Invoke-Api 'http://api.test' '/status' -ApiKey 'test-key') }
            catch { $failed = $true }
            if (-not $failed -or $script:apiTransportMockCalls -ne 1) {
                throw "The $mode GET failure was retried or swallowed."
            }
        }

        foreach ($method in @('POST', 'PUT')) {
            $script:apiTransportMockMode = 'MutationTransport'
            $script:apiTransportMockCalls = 0
            $failed = $false
            try { [void](Invoke-Api 'http://api.test' '/mutation' $method @{} 'test-key') }
            catch { $failed = $true }
            if (-not $failed -or $script:apiTransportMockCalls -ne 1) {
                throw "$method was not dispatched exactly once on a known transport interruption."
            }
        }
    }
    finally {
        Remove-Item Function:\Invoke-RestMethod -ErrorAction SilentlyContinue
        Remove-Variable apiTransportMockMode, apiTransportMockCalls -Scope Script -ErrorAction SilentlyContinue
    }
    Write-Output 'PASS bounded GET transport reconnect and exactly-once POST/PUT/error counterexamples'
}

function Get-ExpectedVerificationStatus([string] $HostMode) {
    switch -CaseSensitive ($HostMode) {
        'ActiveWriter' { return 503 }
        'FailedHost' { return 400 }
        default { throw "No verification POST contract is defined for host mode '$HostMode'." }
    }
}

function Assert-VerificationSurfaceContract([string] $HostMode, [int] $ActualStatus, [bool] $HasForm) {
    if ($HasForm) {
        throw "The $HostMode page exposed a verification form before an eligible verified operation."
    }
    $expectedStatus = Get-ExpectedVerificationStatus $HostMode
    if ($ActualStatus -ne $expectedStatus) {
        throw "The $HostMode verification route returned $ActualStatus; expected $expectedStatus."
    }
}

function Test-AuthenticatedMigrationSurfaceCounterexamples {
    foreach ($case in @(
        [pscustomobject]@{ Name = 'active operator-only host'; Mode = 'ActiveWriter'; Status = 503 },
        [pscustomobject]@{ Name = 'failed host CSRF-first route'; Mode = 'FailedHost'; Status = 400 })) {
        Assert-VerificationSurfaceContract $case.Mode $case.Status $false
    }
    foreach ($case in @(
        [pscustomobject]@{ Name = 'active writer incorrectly returns CSRF 400'; Mode = 'ActiveWriter'; Status = 400; HasForm = $false },
        [pscustomobject]@{ Name = 'failed host incorrectly returns unavailable 503'; Mode = 'FailedHost'; Status = 503; HasForm = $false },
        [pscustomobject]@{ Name = 'verification form exposed'; Mode = 'FailedHost'; Status = 400; HasForm = $true })) {
        $rejected = $false
        try { Assert-VerificationSurfaceContract $case.Mode $case.Status $case.HasForm }
        catch { $rejected = $true }
        if (-not $rejected) {
            throw "The authenticated migration surface accepted invalid case '$($case.Name)'."
        }
    }
    $unknownModeRejected = $false
    try { [void](Get-ExpectedVerificationStatus 'Unknown') } catch { $unknownModeRejected = $true }
    if (-not $unknownModeRejected) { throw 'The verification oracle accepted an unknown host mode.' }
    Write-Output 'PASS authenticated migration surface oracle: exact active 503, failed-host CSRF-first 400, no form, and invalid-mode rejection'
}

function Get-FixedCliObserverWaitDecision([string] $ObserverState) {
    if ($ObserverState.StartsWith('error')) { return 'error' }
    if ($ObserverState -eq 'timeout') { return 'timeout' }
    if ($ObserverState.StartsWith('found')) { return 'found' }
    if ($ObserverState -eq 'starting') { return 'starting' }
    if ($ObserverState -eq 'pending-alive') { return 'pending' }
    if ($ObserverState -eq 'pending-gone' -or $ObserverState -eq 'dead') { return 'dead' }
    throw "The fixed CLI observer returned an unknown state: $ObserverState"
}

function Get-FixedCliObserverShellHelpers {
    return @'
read_proc_value() {
  path="$1"; shift
  [ -e "$path" ] || return 2
  if ! value=$(awk "$@" "$path" 2>/dev/null); then
    [ ! -e "$path" ] && return 2
    return 3
  fi
  if [ -n "$value" ]; then
    printf '%s\n' "$value"
    return 0
  fi
  [ ! -e "$path" ] && return 2
  return 3
}
read_proc_parent() { read_proc_value "$1" '/^PPid:/ {print $2}'; }
read_proc_start() { read_proc_value "$1" '{print $22}'; }
read_proc_state_and_start() { read_proc_value "$1" '{print $3 " " $22}'; }
read_proc_command() {
  path="$1"
  [ -e "$path" ] || return 2
  if ! value=$(tr '\000' ' ' < "$path" 2>/dev/null); then
    [ ! -e "$path" ] && return 2
    return 3
  fi
  [ -n "$value" ] || return 4
  printf '%s\n' "$value"
}
write_identity_error() {
  printf '%s\n' "$1" > "$marker.error"
  : > "$marker.done"
}
write_identity_error_if_absent() {
  [ -e "$marker.error" ] && return 0
  write_identity_error "$1"
}
fixed_cli_candidate_identity() {
  entry="$1"; expected="$2"; proc_root="$3"; marker="$4"
  [ -r "$entry" ] || return 1
  pid=${entry#"$proc_root/"}; pid=${pid%/cmdline}
  case "$pid" in ''|*[!0-9]*) return 1 ;; esac
  status="$proc_root/$pid/status"
  if parent=$(read_proc_parent "$status"); then :; else
    rc=$?; [ "$rc" -eq 2 ] && return 1
    write_identity_error_if_absent 'identity-read-error:parent'
    return 4
  fi
  [ "$parent" = 1 ] || return 1
  if identity=$(read_proc_state_and_start "$proc_root/$pid/stat"); then :; else
    rc=$?; [ "$rc" -eq 2 ] && return 1
    write_identity_error_if_absent 'identity-read-error:stat-before-command'
    return 4
  fi
  state=${identity%% *}; start=${identity#* }
  case "$state" in [[:alpha:]]) ;; *)
    write_identity_error_if_absent 'identity-read-error:stat-before-command'
    return 4 ;;
  esac
  case "$start" in ''|*[!0-9]*)
    write_identity_error_if_absent 'identity-read-error:stat-before-command'
    return 4 ;;
  esac
  case "$start" in *[1-9]*) ;; *)
    write_identity_error_if_absent 'identity-read-error:stat-before-command'
    return 4 ;;
  esac
  if command=$(read_proc_command "$entry"); then
    [ "$command" = "$expected " ] || return 1
    if matched_start=$(read_proc_start "$proc_root/$pid/stat"); then :; else
      rc=$?; [ "$rc" -eq 2 ] && return 1
      write_identity_error_if_absent 'identity-read-error:stat-after-command'
      return 4
    fi
    case "$matched_start" in ''|*[!0-9]*|0)
      write_identity_error_if_absent 'identity-read-error:stat-after-command'
      return 4 ;;
    esac
    case "$matched_start" in *[1-9]*) ;; *)
      write_identity_error_if_absent 'identity-read-error:stat-after-command'
      return 4 ;;
    esac
    if [ "$matched_start" != "$start" ]; then
      write_identity_error_if_absent 'identity-changed'
      return 4
    fi
  else
    rc=$?
    [ "$rc" -eq 2 ] && return 1
    if [ "$rc" -ne 4 ]; then
      write_identity_error_if_absent 'identity-read-error:cmdline'
      return 4
    fi
    if parent=$(read_proc_parent "$status"); then :; else
      rc=$?; [ "$rc" -eq 2 ] && return 1
      write_identity_error_if_absent 'identity-read-error:empty-revalidate-parent'
      return 4
    fi
    if identity=$(read_proc_state_and_start "$proc_root/$pid/stat"); then :; else
      rc=$?; [ "$rc" -eq 2 ] && return 1
      write_identity_error_if_absent 'identity-read-error:empty-revalidate-stat'
      return 4
    fi
    state2=${identity%% *}; start2=${identity#* }
    if [ "$parent" != 1 ] || [ "$start2" != "$start" ]; then
      write_identity_error_if_absent 'identity-changed'
      return 4
    fi
    case "$state2" in Z|X) ;; *)
      write_identity_error_if_absent 'identity-unavailable:cmdline-empty-live'
      return 4 ;;
    esac
    if command=$(read_proc_command "$entry"); then
      write_identity_error_if_absent 'identity-changed'
      return 4
    else
      rc=$?
      [ "$rc" -eq 2 ] && return 1
      if [ "$rc" -ne 4 ]; then
        write_identity_error_if_absent 'identity-read-error:empty-revalidate-cmdline'
        return 4
      fi
    fi
    return 1
  fi
  printf '%s %s\n' "$pid" "$start"
}
scan_fixed_cli_candidates() {
  expected="$1"; proc_root="$2"; action="$3"; marker="$4"
  for entry in "$proc_root"/[0-9]*/cmdline; do
    candidate=$(fixed_cli_candidate_identity "$entry" "$expected" "$proc_root" "$marker") || {
      rc=$?; [ "$rc" -eq 1 ] && continue; return "$rc"
    }
    pid=${candidate%% *}; start=${candidate#* }
    if [ "$action" = stop ]; then
      kill -STOP "$pid" 2>/dev/null || continue
      if stopped_start=$(read_proc_start "$proc_root/$pid/stat"); then :; else
        rc=$?
        [ "$rc" -eq 2 ] && write_identity_error 'identity-unavailable:stat-after-stop' ||
          write_identity_error 'identity-read-error:stat-after-stop'
        return 4
      fi
      if stopped_parent=$(read_proc_parent "$proc_root/$pid/status"); then :; else
        rc=$?
        [ "$rc" -eq 2 ] && write_identity_error 'identity-unavailable:parent-after-stop' ||
          write_identity_error 'identity-read-error:parent-after-stop'
        return 4
      fi
      if [ "$stopped_start" != "$start" ] || [ "$stopped_parent" != 1 ]; then
        write_identity_error 'identity-changed'
        return 4
      fi
    fi
    printf '%s %s\n' "$pid" "$start" > "$marker.found"
    : > "$marker.done"
    return 0
  done
  return 1
}
observer_liveness() {
  pid="$1"; expected="$2"; proc_root="${3:-/proc}"
  if identity=$(read_proc_state_and_start "$proc_root/$pid/stat"); then :; else
    rc=$?
    [ "$rc" -eq 2 ] && { echo gone; return 0; }
    echo error; return 4
  fi
  state=${identity%% *}
  start=${identity#* }
  case "$state" in ''|*[![:alpha:]]*) echo error; return 4 ;; esac
  case "$start" in ''|*[!0-9]*) echo error; return 4 ;; esac
  case "$state" in Z|X) echo gone ;; *) [ "$start" = "$expected" ] && echo alive || echo gone ;; esac
}
'@
}

function Test-FixedCliObserverProcReadCounterexamples([string] $Image) {
    $shellScript = (Get-FixedCliObserverShellHelpers) + "`n" + @'
set -eu
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
expected='/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration 11111111-2222-4333-8444-555555555555'
write_stat() {
  pid="$1"; state="$2"; parent="$3"; start="$4"
  printf '%s (sh) %s %s' "$pid" "$state" "$parent" > "$root/proc/$pid/stat"
  field=5
  while [ "$field" -lt 22 ]; do printf ' 0' >> "$root/proc/$pid/stat"; field=$((field + 1)); done
  printf ' %s\n' "$start" >> "$root/proc/$pid/stat"
}
mkdir -p "$root/proc/10" "$root/proc/20" "$root/proc/30"
for pid in 10 20 30; do
  printf 'Name:\tworker\nState:\tS\nPPid:\t1\n' > "$root/proc/$pid/status"
  printf '%s\0' /usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration \
    11111111-2222-4333-8444-555555555555 > "$root/proc/$pid/cmdline"
  write_stat "$pid" S 1 "$((12000 + pid))"
done
marker="$root/record"
awk() {
  if [ "$2" = "$root/proc/10/status" ]; then rm -f "$2"; return 2; fi
  command awk "$@"
}
if ! scan_fixed_cli_candidates "$expected" "$root/proc" record "$marker"; then exit 10; fi
[ -f "$marker.found" ] && [ -f "$marker.done" ] || exit 11
IFS=' ' read -r found_pid found_start < "$marker.found"
[ "$found_pid" = 20 ] && [ "$found_start" = 12020 ] || exit 12
printf '%s\n' 'PASS set-e watcher scan continues after deterministic /proc status ENOENT and writes terminal markers'

unset -f awk
rm -rf "$root/proc/10" "$root/proc/20"
marker="$root/stop"
kill() { rm -f "$root/proc/30/stat"; return 0; }
if scan_fixed_cli_candidates "$expected" "$root/proc" stop "$marker"; then exit 13; else result=$?; fi
[ "$result" -eq 4 ] || exit 14
[ -f "$marker.error" ] && [ -f "$marker.done" ] || exit 15
IFS= read -r error_value < "$marker.error"
[ "$error_value" = 'identity-unavailable:stat-after-stop' ] || exit 16
unset -f kill
printf '%s\n' 'PASS post-stop PID disappearance is reported as identity ambiguity'

mkdir -p "$root/proc/40"
write_stat 40 S 1 12345
[ "$(observer_liveness 40 12345 "$root/proc")" = alive ] || exit 17
rm -f "$root/proc/40/stat"
[ "$(observer_liveness 40 12345 "$root/proc")" = gone ] || exit 18
write_stat 40 Z 1 12345
[ "$(observer_liveness 40 12345 "$root/proc")" = gone ] || exit 19
printf '%s\n' 'PASS observer PID/start liveness rejects missing and zombie processes'
'@
    $output = Invoke-Docker @('run', '--rm', '--network', 'none', '--entrypoint', '/bin/sh', $Image, '-c', $shellScript) 30
    $generatedScripts = @(
        (Get-FixedCliObserverWatcherScript),
        (Get-FixedCliObserverStatusScript),
        (Get-FixedCliObserverExitScript))
    foreach ($generatedScript in $generatedScripts) {
        [void](Invoke-Docker @('run', '--rm', '--network', 'none', '--entrypoint', '/bin/sh', $Image,
            '-n', '-c', $generatedScript, 'hrp-observer-syntax') 30)
    }
    $liveScript = (Get-FixedCliObserverShellHelpers) + "`n" + @'
set -eu
sleep 30 &
child=$!
start=$(read_proc_start "/proc/$child/stat")
[ "$(observer_liveness "$child" "$start" /proc)" = alive ] || exit 21
kill "$child"
wait "$child" 2>/dev/null || true
[ "$(observer_liveness "$child" "$start" /proc)" = gone ] || exit 22
printf '%s\n' 'PASS live observer process transitions from exact PID/start alive to reaped/gone'
'@
    $liveOutput = Invoke-Docker @('run', '--rm', '--network', 'none', '--entrypoint', '/bin/sh', $Image,
        '-c', $liveScript, 'hrp-observer-live-test') 30
    foreach ($expected in @(
        'PASS set-e watcher scan continues after deterministic /proc status ENOENT and writes terminal markers',
        'PASS post-stop PID disappearance is reported as identity ambiguity',
        'PASS observer PID/start liveness rejects missing and zombie processes')) {
        if (-not $output.Contains($expected)) { throw "The observer shell counterexample did not report: $expected" }
    }
    if (-not $liveOutput.Contains('PASS live observer process transitions from exact PID/start alive to reaped/gone')) {
        throw 'The live observer process liveness/reap counterexample did not pass.'
    }
}

function Test-FixedCliObserverWrapperSmokeCounterexamples {
    $bashPath = if ($IsWindows) { Join-Path $env:ProgramFiles 'Git\bin\bash.exe' } else { '/bin/bash' }
    if (-not (Test-Path -LiteralPath $bashPath -PathType Leaf)) {
        throw "The fixed CLI observer wrapper smoke requires Bash at '$bashPath'."
    }

    $watcherScript = (Get-FixedCliObserverWatcherScript).
        Replace('watcher_start=$(awk ''{print $22}'' "/proc/$$/stat")', 'watcher_start=12345').
        Replace('expected="/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $operation"',
            'scan_fixed_cli_candidates() { return 7; }' + "`n" +
            'expected="/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $operation"')
    if (-not $watcherScript.Contains('watcher_start=12345') -or -not $watcherScript.Contains('scan_fixed_cli_candidates() { return 7; }')) {
        throw 'The fixed CLI observer wrapper smoke could not install its deterministic watcher seams.'
    }
    $watcherScriptBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($watcherScript))
    $statusScriptBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(
        (Get-FixedCliObserverStatusScript)))
    $shellScript = (Get-FixedCliObserverShellHelpers) + "`n" + @'
set -eu
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
expected='/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration 11111111-2222-4333-8444-555555555555'
write_stat() {
  pid="$1"; state="$2"; parent="$3"; start="$4"
  printf '%s (sh) %s %s' "$pid" "$state" "$parent" > "$root/proc/$pid/stat"
  field=5
  while [ "$field" -lt 22 ]; do printf ' 0' >> "$root/proc/$pid/stat"; field=$((field + 1)); done
  printf ' %s\n' "$start" >> "$root/proc/$pid/stat"
}
write_candidate() {
  pid="$1"; start="$2"
  mkdir -p "$root/proc/$pid"
  printf 'Name:\tworker\nState:\tS\nPPid:\t1\n' > "$root/proc/$pid/status"
  printf '%s\0' /usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration \
    11111111-2222-4333-8444-555555555555 > "$root/proc/$pid/cmdline"
  write_stat "$pid" S 1 "$start"
}

mkdir -p "$root/proc"
write_candidate 10 12010
write_candidate 20 12020
awk() {
  if [ "$2" = "$root/proc/10/status" ]; then rm -f "$2"; return 0; fi
  command awk "$@"
}
marker="$root/race"
if ! scan_fixed_cli_candidates "$expected" "$root/proc" record "$marker"; then exit 31; fi
[ ! -e "$root/proc/10/status" ] || exit 32
[ -f "$marker.found" ] && [ -f "$marker.done" ] || exit 33
IFS=' ' read -r found_pid found_start < "$marker.found"
[ "$found_pid:$found_start" = '20:12020' ] || exit 34
printf '%s\n' 'PASS wrapper smoke: empty proc read followed by disappearance is gone and scan continues'

unset -f awk
rm -rf "$root/proc/10" "$root/proc/20"
write_candidate 30 12030
awk() {
  if [ "$2" = "$root/proc/30/status" ]; then return 0; fi
  command awk "$@"
}
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/stable"; then exit 35; else stable_status=$?; fi
[ "$stable_status" -eq 4 ] && [ -e "$root/proc/30/status" ] || exit 36
printf '%s\n' 'PASS wrapper smoke: stable empty proc read remains a fail-closed malformed identity'

unset -f awk
awk() {
  if [ "$2" = "$root/proc/30/status" ]; then return 2; fi
  command awk "$@"
}
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/read-error"; then exit 37; else read_error_status=$?; fi
[ "$read_error_status" -eq 4 ] && [ -e "$root/proc/30/status" ] || exit 38
printf '%s\n' 'PASS wrapper smoke: unreadable existing proc file remains a fail-closed read error'

unset -f awk
for zombie_state in Z X; do
  rm -rf "$root/proc/30" "$root/proc/40"
  write_candidate 30 12030
  : > "$root/proc/30/cmdline"
  write_stat 30 "$zombie_state" 1 12030
  write_candidate 40 12040
  if ! scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/stable-$zombie_state"; then exit 43; fi
  IFS=' ' read -r found_pid found_start < "$root/stable-$zombie_state.found"
  [ "$found_pid:$found_start" = '40:12040' ] || exit 44
done
printf '%s\n' 'PASS wrapper smoke: stable zombie with confirmed empty cmdline is skipped'

rm -rf "$root/proc/30" "$root/proc/40"
write_candidate 30 12030
: > "$root/proc/30/cmdline"
write_candidate 40 12040
tr() {
  if [ -e "$root/race-to-zombie" ]; then
    rm -f "$root/race-to-zombie"
    write_stat 30 Z 1 12030
  fi
  command tr "$@"
}
: > "$root/race-to-zombie"
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/race-zombie"; then :; else
  race_status=$?; printf 'race-zombie-failed:%s:%s\n' "$race_status" "$(cat "$root/race-zombie.error" 2>/dev/null || true)"; exit 45
fi
IFS=' ' read -r found_pid found_start < "$root/race-zombie.found"
[ "$found_pid:$found_start" = '40:12040' ] || exit 46
unset -f tr
printf '%s\n' 'PASS wrapper smoke: S-to-Z transition with same PID/start and repeated empty cmdline is skipped'

rm -rf "$root/proc/30" "$root/proc/40"
write_candidate 30 12030
: > "$root/proc/30/cmdline"
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/live-empty"; then exit 47; else live_status=$?; fi
[ "$live_status" -eq 4 ] && [ "$(cat "$root/live-empty.error")" = 'identity-unavailable:cmdline-empty-live' ] || exit 48
printf '%s\n' 'PASS wrapper smoke: live empty cmdline remains fail-closed'

for scenario in start-change parent-change; do
  write_candidate 30 12030
  : > "$root/proc/30/cmdline"
  if [ "$scenario" = start-change ]; then
    tr() {
      if [ -e "$root/change-start" ]; then
        rm -f "$root/change-start"
        write_stat 30 Z 1 12031
      fi
      command tr "$@"
    }
    : > "$root/change-start"
  else
    tr() {
      if [ -e "$root/change-parent" ]; then
        rm -f "$root/change-parent"
        printf 'Name:\tworker\nState:\tZ\nPPid:\t2\n' > "$root/proc/30/status"
        write_stat 30 Z 2 12030
      fi
      command tr "$@"
    }
    : > "$root/change-parent"
  fi
  if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/$scenario"; then exit 49; else change_status=$?; fi
  [ "$change_status" -eq 4 ] && [ "$(cat "$root/$scenario.error")" = 'identity-changed' ] || exit 50
  unset -f tr
  rm -rf "$root/proc/30"
done
printf '%s\n' 'PASS wrapper smoke: changed PID start or parent during empty-cmdline revalidation fails closed'

write_candidate 30 12030
awk() {
  if [ "$2" = "$root/proc/30/stat" ]; then return 0; fi
  command awk "$@"
}
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/malformed-stat"; then exit 51; else malformed_status=$?; fi
[ "$malformed_status" -eq 4 ] && [ "$(cat "$root/malformed-stat.error")" = 'identity-read-error:stat-before-command' ] || exit 52
unset -f awk
printf '%s\n' 'PASS wrapper smoke: malformed candidate stat fails with a stage-specific marker'

tr() { return 1; }
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/cmdline-read-error"; then exit 53; else cmdline_error_status=$?; fi
[ "$cmdline_error_status" -eq 4 ] && [ "$(cat "$root/cmdline-read-error.error")" = 'identity-read-error:cmdline' ] || exit 54
printf 'identity-read-error:prior-stage\n' > "$root/preserved.error"
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/preserved"; then exit 55; else preserved_status=$?; fi
[ "$preserved_status" -eq 4 ] && [ "$(cat "$root/preserved.error")" = 'identity-read-error:prior-stage' ] || exit 56
unset -f tr
printf '%s\n' 'PASS wrapper smoke: cmdline I/O errors fail closed and preserve an earlier specific marker'

write_candidate 30 12030
tr() {
  if [ -e "$root/reuse-start" ]; then
    rm -f "$root/reuse-start"
    write_stat 30 S 1 12031
  fi
  command tr "$@"
}
: > "$root/reuse-start"
if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/reuse-start-result"; then exit 57; else reuse_status=$?; fi
[ "$reuse_status" -eq 4 ] && [ "$(cat "$root/reuse-start-result.error")" = 'identity-changed' ] || exit 58
unset -f tr
printf '%s\n' 'PASS wrapper smoke: matching cmdline with changed post-read start is rejected'

write_candidate 30 12030
tr() {
  rm -f "$root/proc/30/stat"
  command tr "$@"
}
if fixed_cli_candidate_identity "$root/proc/30/cmdline" "$expected" "$root/proc" "$root/post-command-gone"; then
  exit 59
else
  post_command_gone_status=$?
fi
[ "$post_command_gone_status" -eq 1 ] && [ ! -e "$root/post-command-gone.error" ] || exit 60
unset -f tr
printf '%s\n' 'PASS wrapper smoke: confirmed post-command stat disappearance is classified gone'

for post_stat_case in permission malformed; do
  rm -f "$root/post-command-stat-read"
  write_candidate 30 12030
  tr() {
    : > "$root/post-command-stat-read"
    command tr "$@"
  }
  awk() {
    if [ -e "$root/post-command-stat-read" ] && [ "$2" = "$root/proc/30/stat" ]; then
      [ "$post_stat_case" = malformed ] && return 0
      return 2
    fi
    command awk "$@"
  }
  if scan_fixed_cli_candidates "$expected" "$root/proc" record "$root/post-stat-$post_stat_case"; then exit 61; else post_stat_status=$?; fi
  [ "$post_stat_status" -eq 4 ] && [ "$(cat "$root/post-stat-$post_stat_case.error" 2>/dev/null || true)" = 'identity-read-error:stat-after-command' ] || {
    printf 'post-stat-case-failed:%s:%s:%s\n' "$post_stat_case" "$post_stat_status" "$(cat "$root/post-stat-$post_stat_case.error" 2>/dev/null || true)"
    exit 62
  }
  unset -f tr awk
done
printf '%s\n' 'PASS wrapper smoke: unreadable or malformed post-command stat fails closed with a stage marker'

for invalid_stat_case in multichar-state zero-start; do
  write_candidate 30 12030
  if [ "$invalid_stat_case" = multichar-state ]; then
    write_stat 30 SS 1 12030
  else
    write_stat 30 S 1 0
  fi
  if fixed_cli_candidate_identity "$root/proc/30/cmdline" "$expected" "$root/proc" "$root/invalid-$invalid_stat_case"; then
    exit 63
  else
    invalid_stat_status=$?
  fi
  [ "$invalid_stat_status" -eq 4 ] && [ "$(cat "$root/invalid-$invalid_stat_case.error")" = 'identity-read-error:stat-before-command' ] || exit 64
done
printf '%s\n' 'PASS wrapper smoke: multi-character states and zero start times are rejected'

printf '%s' "$HRP_OBSERVER_WATCHER_BASE64" | base64 -d > "$root/watcher.sh"
printf '%s' "$HRP_OBSERVER_STATUS_BASE64" | base64 -d > "$root/status.sh"
set +e
/bin/sh "$root/watcher.sh" 11111111-2222-4333-8444-555555555555 record "$root/unknown"
watcher_status=$?
set -e
[ "$watcher_status" -eq 7 ] || exit 39
[ -f "$root/unknown.error" ] && [ -f "$root/unknown.done" ] || exit 40
[ "$(cat "$root/unknown.error")" = 'identity-read-error:scan' ] || exit 41
status=$( /bin/sh "$root/status.sh" "$root/unknown" )
[ "$status" = "$(printf 'error\nidentity-read-error:scan')" ] || exit 42
printf '%s\n' 'PASS wrapper smoke: unexpected watcher scan exit is preserved with durable error marker'
'@
    $shellScriptPath = Join-Path $env:TEMP 'hrp-f29-wrapper-smoke.sh'
    Set-Content -LiteralPath $shellScriptPath -Value $shellScript
    $output = Invoke-External $bashPath @($shellScriptPath) 30 @{
        HRP_OBSERVER_WATCHER_BASE64 = $watcherScriptBase64
        HRP_OBSERVER_STATUS_BASE64 = $statusScriptBase64
    }
    foreach ($expected in @(
        'PASS wrapper smoke: empty proc read followed by disappearance is gone and scan continues',
        'PASS wrapper smoke: stable empty proc read remains a fail-closed malformed identity',
        'PASS wrapper smoke: unreadable existing proc file remains a fail-closed read error',
        'PASS wrapper smoke: stable zombie with confirmed empty cmdline is skipped',
        'PASS wrapper smoke: S-to-Z transition with same PID/start and repeated empty cmdline is skipped',
        'PASS wrapper smoke: live empty cmdline remains fail-closed',
        'PASS wrapper smoke: changed PID start or parent during empty-cmdline revalidation fails closed',
        'PASS wrapper smoke: malformed candidate stat fails with a stage-specific marker',
        'PASS wrapper smoke: cmdline I/O errors fail closed and preserve an earlier specific marker',
        'PASS wrapper smoke: matching cmdline with changed post-read start is rejected',
        'PASS wrapper smoke: confirmed post-command stat disappearance is classified gone',
        'PASS wrapper smoke: unreadable or malformed post-command stat fails closed with a stage marker',
        'PASS wrapper smoke: multi-character states and zero start times are rejected',
        'PASS wrapper smoke: unexpected watcher scan exit is preserved with durable error marker')) {
        if (-not $output.Contains($expected)) { throw "The fixed CLI observer wrapper smoke omitted: $expected`n$output" }
    }
    Write-Output 'PASS fixed CLI observer wrapper smoke: command identity revalidation, zombie races, fail-closed reads, and durable errors'
}

function Get-FixedCliObserverWatcherScript {
    $watcherScript = @'
set -eu
operation="$1"
action="$2"
marker="$3"
watcher_start=$(awk '{print $22}' "/proc/$$/stat")
printf '%s %s\n' "$$" "$watcher_start" > "$marker.pid"
expected="/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $operation"
deadline=$(($(date +%s) + 1200))
while [ "$(date +%s)" -lt "$deadline" ]; do
  if scan_fixed_cli_candidates "$expected" /proc "$action" "$marker"; then exit 0; else scan_status=$?; fi
  if [ "$scan_status" -ne 1 ]; then
    [ -f "$marker.error" ] || write_identity_error 'identity-read-error:scan'
    exit "$scan_status"
  fi
  sleep 0.02
done
: > "$marker.timeout"
: > "$marker.done"
exit 9
'@
    return $watcherScript.Replace('set -eu', (Get-FixedCliObserverShellHelpers) + "`nset -eu")
}

function Get-FixedCliObserverStatusScript {
    return (Get-FixedCliObserverShellHelpers) + "`n" + @'
if [ -f "$1.error" ]; then echo error; cat "$1.error"
elif [ -f "$1.timeout" ]; then echo timeout
elif [ -f "$1.found" ] && [ -f "$1.done" ]; then echo found; cat "$1.found"
elif [ ! -r "$1.pid" ]; then echo starting
else
  read -r watcher_pid watcher_start < "$1.pid" || { echo dead; exit 0; }
  case "$watcher_pid:$watcher_start" in *[!0-9:]*|:*) echo dead; exit 0 ;; esac
  liveness=$(observer_liveness "$watcher_pid" "$watcher_start" /proc)
  printf 'pending-%s\n' "$liveness"
fi
'@
}

function Get-FixedCliObserverExitScript {
    return (Get-FixedCliObserverShellHelpers) + "`n" + @'
observer_liveness "$1" "$2" /proc
'@
}

function Start-FixedCliObserver([string] $Container, [string] $OperationId, [ValidateSet('record', 'stop')] [string] $Action) {
    $marker = "/tmp/hrp-horse-id-observer-$OperationId-$Action"
    $watcherScript = Get-FixedCliObserverWatcherScript
    [void](Invoke-Docker @('exec', '-d', $Container, '/bin/sh', '-c', $watcherScript,
        'hrp-fixed-cli-observer', $OperationId, $Action, $marker) 30)
    return [pscustomobject]@{ Marker = $marker; Action = $Action }
}

function Wait-FixedCliObserver([string] $Container, $Observer, [int] $TimeoutSeconds = 1250) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $startedAt = [DateTimeOffset]::UtcNow
    $result = $null
    $statusScript = Get-FixedCliObserverStatusScript
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $state = Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $statusScript,
            'hrp-observer-status', $Observer.Marker) 15
        $decisionText = $state.Trim()
        if ($decisionText.StartsWith('error')) { throw "The fixed CLI observer lost target identity: $decisionText" }
        if ($decisionText -eq 'timeout') { throw 'The bounded fixed CLI observer reached its 1200-second deadline.' }
        $decision = Get-FixedCliObserverWaitDecision $decisionText
        if ($decision -eq 'dead') { throw 'The fixed CLI observer exited without a terminal marker.' }
        if ($decision -eq 'starting' -and ([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds -gt 10) {
            throw 'The fixed CLI observer did not publish its process identity.'
        }
        if ($decision -eq 'found') {
            $parts = @($decisionText -split '\s+')
            if ($parts.Count -ne 3 -or $parts[0] -ne 'found') { throw "The fixed CLI observer returned malformed identity data: $state" }
            $result = [pscustomobject]@{ ProcessId = [int]$parts[1]; StartIdentity = $parts[2] }
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $result) { throw "The fixed CLI observer did not finish within ${TimeoutSeconds}s." }

    $pidInfo = Invoke-Docker @('exec', $Container, '/bin/cat', "$($Observer.Marker).pid") 15
    $pidParts = $pidInfo.Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($pidParts.Count -ne 2) { throw 'The fixed CLI observer did not save its process identity.' }
    $watcherPid = [int]$pidParts[0]
    $watcherStart = $pidParts[1]
    $exitDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $exitScript = Get-FixedCliObserverExitScript
    while ([DateTimeOffset]::UtcNow -lt $exitDeadline) {
        $watcherState = Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $exitScript,
            'hrp-observer-exit', [string]$watcherPid, $watcherStart) 15
        if ($watcherState.Trim() -eq 'gone') { return $result }
        Start-Sleep -Milliseconds 100
    }
    throw 'The completed fixed CLI observer did not exit within its reap bound.'
}

function Get-ContainerProcessSnapshot([string] $Container) {
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        try {
            $pid1Command = (Invoke-ContainerProcRead $Container '/proc/1/cmdline').Replace([char]0, ' ').Trim()
            $taskChildrenText = Invoke-ContainerTaskChildrenRead $Container
            $children = @(Get-UniqueProcessIdsFromTaskChildren @($taskChildrenText -split "`n"))
            $childCommands = @()
            foreach ($childPid in $children) {
                $command = (Invoke-ContainerProcRead $Container "/proc/$childPid/cmdline").Replace([char]0, ' ').Trim()
                $status = Invoke-ContainerProcRead $Container "/proc/$childPid/status"
                $parentLine = ((@($status -split "`n" | Where-Object { $_.StartsWith('PPid:') }) | Select-Object -First 1).Trim())
                $stat = Invoke-ContainerProcRead $Container "/proc/$childPid/stat"
                if ($stat -notmatch '^\d+ \(.*\) (?<fields>.*)$') { throw "Could not parse start identity for child PID $childPid." }
                $statFields = @($Matches.fields -split '\s+')
                if ($statFields.Count -le 19) { throw "Child PID $childPid had incomplete process identity data." }
                if ($statFields[0] -in @('Z', 'X')) {
                    throw [IO.FileNotFoundException]::new("Process PID $childPid is a zombie/exited child, not a live writer.")
                }
                $environment = (Invoke-ContainerProcRead $Container "/proc/$childPid/environ").Replace([char]0, "`n")
                $childCommands += [pscustomobject]@{
                    ProcessId = $childPid
                    Command = $command
                    ParentLine = $parentLine
                    State = $statFields[0]
                    StartIdentity = $statFields[19]
                    OperatorOnly = @($environment -split "`n" |
                            Where-Object { $_.TrimEnd("`r") -ceq 'HRP_HORSE_IDENTITY_OPERATOR_ONLY=true' }).Count -gt 0
                }
            }
            return [pscustomobject]@{ Pid1Command = $pid1Command; Children = $childCommands }
        }
        catch {
            if ($_.Exception -isnot [IO.FileNotFoundException] -or $attempt -eq 5) { throw }
            Start-Sleep -Milliseconds 25
        }
    }
    throw 'The bounded PID-1 process snapshot could not obtain a stable child identity.'
}

function Assert-Pid1ApiShape([string] $Container, [string] $ExpectedChildFlag) {
    $snapshot = Get-ContainerProcessSnapshot $Container
    return Assert-Pid1ApiSnapshot $snapshot $ExpectedChildFlag
}

function Get-Pid1ChildMode([string] $Command) {
    if ($Command -match '--internal-api-child') { return 'api-before-or-after' }
    if ($Command -match '/app/HorseRacingPrediction\.Api\.dll horse-id-migration [0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$') {
        return 'fixed-cli'
    }
    return $null
}

function Assert-Pid1ApiSnapshot($snapshot, [string] $ExpectedChildFlag) {
    if ($snapshot.Pid1Command -notmatch 'HorseRacingPrediction\.Api\.dll supervise') {
        throw "The production image is not running the frozen supervisor as PID 1: $($snapshot.Pid1Command)"
    }
    if ($snapshot.Pid1Command -match '(?:^|\s)--initapp(?:\s|$)') {
        throw "The production image used an init application instead of the frozen supervisor as PID 1: $($snapshot.Pid1Command)"
    }
    if ($snapshot.Children.Count -ne 1) {
        throw "PID 1 must own exactly one live service child; found $($snapshot.Children.Count)."
    }
    if (($snapshot.Children[0].ParentLine -notmatch '^PPid:\s+1$') -or
        ($snapshot.Children[0].Command -notmatch [regex]::Escape($ExpectedChildFlag))) {
        throw "The only child is not the expected direct PID-1 child: $($snapshot.Children[0] | ConvertTo-Json -Compress)"
    }
    return $snapshot.Children[0]
}

function Assert-Pid1MigrationWorkSnapshot($snapshot, [string] $OperationId, $ExpectedFixedCli) {
    if ($snapshot.Pid1Command -notmatch 'HorseRacingPrediction\.Api\.dll supervise' -or
        $snapshot.Pid1Command -match '(?:^|\s)--initapp(?:\s|$)') {
        throw "The migration work root is not the frozen supervisor without --initapp: $($snapshot.Pid1Command)"
    }
    if ($snapshot.Children.Count -ne 2) {
        throw "PID 1 must own exactly one fixed CLI and one operator child during held migration work; found $($snapshot.Children.Count)."
    }
    $fixed = @($snapshot.Children | Where-Object {
            Test-IsFixedCliCandidate 1 $_.Command $OperationId })
    $operator = @($snapshot.Children | Where-Object { Test-IsOperatorApiCandidate $_ })
    if ($fixed.Count -ne 1 -or $operator.Count -ne 1) {
        throw "PID 1 did not own exactly one fixed CLI and one operator-only API child: $($snapshot.Children | ConvertTo-Json -Compress)"
    }
    if ($fixed[0].ParentLine -notmatch '^PPid:\s+1$') {
        throw 'The fixed CLI was not a direct PID-1 child.'
    }
    if ($fixed[0].ProcessId -ne $ExpectedFixedCli.ProcessId -or
        $fixed[0].StartIdentity -cne $ExpectedFixedCli.StartIdentity) {
        throw 'The fixed CLI PID/start identity changed while the operator child was replaced.'
    }
    if ($fixed[0].ProcessId -eq $operator[0].ProcessId) {
        throw 'The fixed CLI and operator child did not have distinct PID/start identities.'
    }
    return [pscustomobject]@{ Snapshot = $snapshot; FixedCli = $fixed[0]; Operator = $operator[0] }
}

function Assert-VerifiedNormalApiChildTransition($status, $snapshot, $ExpectedChild) {
    if ($status.status -cne 'Succeeded' -or $status.executionPhase -cne 'Verified') {
        throw "The normal API child appeared before the migration reached Verified: status=$($status.status), phase=$($status.executionPhase)."
    }
    if ($snapshot.Children.Count -ne 1) {
        throw "The verified normal API transition did not have exactly one service child: found $($snapshot.Children.Count)."
    }
    $child = $snapshot.Children[0]
    if ($child.ParentLine -notmatch '^PPid:\s+1$' -or
        $child.Command -notmatch '--internal-api-child' -or [bool]$child.OperatorOnly) {
        throw "The verified normal API transition had an unsafe child shape: $($child | ConvertTo-Json -Compress)"
    }
    if ($child.Command -cne $ExpectedChild.Command -or
        $child.ProcessId -ne $ExpectedChild.ProcessId -or
        $child.StartIdentity -cne $ExpectedChild.StartIdentity) {
        throw 'The verified normal API transition changed the child PID, start identity, or command.'
    }
    return $child
}

function Get-Pid1MigrationWorkTransition($snapshot, [string] $OperationId, $ExpectedFixedCli) {
    if ($snapshot.Pid1Command -notmatch 'HorseRacingPrediction\.Api\.dll supervise' -or
        $snapshot.Pid1Command -match '(?:^|\s)--initapp(?:\s|$)') {
        throw "The pending migration snapshot has an unsafe PID-1 root: $($snapshot.Pid1Command)"
    }
    foreach ($child in $snapshot.Children) {
        if ($child.ProcessId -le 0 -or $child.StartIdentity -notmatch '^\d+$' -or
            $child.ParentLine -notmatch '^PPid:\s+1$' -or $child.State -in @('Z', 'X')) {
            throw "The pending migration snapshot has malformed or non-direct child identity: $($child | ConvertTo-Json -Compress)"
        }
    }
    if ($snapshot.Children.Count -eq 0) {
        return [pscustomobject]@{ Mode = 'ReapingTransition'; Work = $null; Child = $null }
    }
    if ($snapshot.Children.Count -eq 1) {
        $child = $snapshot.Children[0]
        if (Test-IsOperatorApiCandidate $child) {
            return [pscustomobject]@{ Mode = 'OperatorOnlyTransition'; Work = $null; Child = $child }
        }
        if ((Test-IsFixedCliCandidate 1 $child.Command $OperationId) -and
            $child.ProcessId -eq $ExpectedFixedCli.ProcessId -and
            $child.StartIdentity -ceq $ExpectedFixedCli.StartIdentity) {
            return [pscustomobject]@{ Mode = 'FixedCliOnlyTransition'; Work = $null; Child = $child }
        }
        if (Test-IsNormalApiCandidate $child) {
            return [pscustomobject]@{ Mode = 'NormalApiTransition'; Work = $null; Child = $child }
        }
        throw "The pending migration snapshot exposed an unknown sole child shape: $($child | ConvertTo-Json -Compress)"
    }
    if ($snapshot.Children.Count -eq 2) {
        $work = Assert-Pid1MigrationWorkSnapshot $snapshot $OperationId $ExpectedFixedCli
        return [pscustomobject]@{ Mode = 'ActiveWork'; Work = $work; Child = $null }
    }
    throw "The pending migration snapshot exposed duplicate or unexpected children: $($snapshot.Children | ConvertTo-Json -Compress)"
}

function Test-Pid1MigrationWorkTransitionCounterexamples {
    $root = 'dotnet HorseRacingPrediction.Api.dll supervise'
    $operationId = '11111111-2222-4333-8444-555555555555'
    $cli = [pscustomobject]@{
        ProcessId = 40; Command = "/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration $operationId"
        ParentLine = 'PPid: 1'; State = 'T'; StartIdentity = '12345'; OperatorOnly = $false
    }
    $normal = [pscustomobject]@{
        ProcessId = 41; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child'
        ParentLine = 'PPid: 1'; State = 'S'; StartIdentity = '12346'; OperatorOnly = $false
    }
    $operator = [pscustomobject]@{
        ProcessId = 42; Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-blocked-child'
        ParentLine = 'PPid: 1'; State = 'S'; StartIdentity = '12347'; OperatorOnly = $true
    }
    $originalSnapshotFunction = (Get-Item Function:\Get-ContainerProcessSnapshot).ScriptBlock
    foreach ($case in @(
        [pscustomobject]@{ Name = 'empty reaping'; Children = @(); ExpectedMode = 'ReapingTransition' },
        [pscustomobject]@{ Name = 'normal API'; Children = @($normal); ExpectedMode = 'NormalApiTransition' },
        [pscustomobject]@{ Name = 'operator only'; Children = @($operator); ExpectedMode = 'OperatorOnlyTransition' },
        [pscustomobject]@{ Name = 'fixed CLI only'; Children = @($cli); ExpectedMode = 'FixedCliOnlyTransition' },
        [pscustomobject]@{ Name = 'fixed CLI with operator'; Children = @($cli, $operator); ExpectedMode = 'ActiveWork' })) {
        $snapshot = [pscustomobject]@{ Pid1Command = $root; Children = $case.Children }
        $transition = Get-Pid1MigrationWorkTransition $snapshot $operationId $cli
        if ($transition.Mode -cne $case.ExpectedMode) {
            throw "The migration work transition classified $($case.Name) as '$($transition.Mode)', expected '$($case.ExpectedMode)'."
        }
    }
    foreach ($invalid in @(
        [pscustomobject]@{ Name = 'CLI plus normal API'; Children = @($cli, $normal) },
        [pscustomobject]@{ Name = 'duplicate CLI'; Children = @($cli, $cli) },
        [pscustomobject]@{ Name = 'duplicate operator'; Children = @($operator, $operator) },
        [pscustomobject]@{ Name = 'wrong parent'; Children = @([pscustomobject]@{ ProcessId = 40; Command = $cli.Command; ParentLine = 'PPid: 2'; State = 'T'; StartIdentity = '12345'; OperatorOnly = $false }) },
        [pscustomobject]@{ Name = 'malformed start identity'; Children = @([pscustomobject]@{ ProcessId = 41; Command = $normal.Command; ParentLine = 'PPid: 1'; State = 'S'; StartIdentity = 'not-a-start'; OperatorOnly = $false }) },
        [pscustomobject]@{ Name = 'CLI PID reuse'; Children = @([pscustomobject]@{ ProcessId = 99; Command = $cli.Command; ParentLine = 'PPid: 1'; State = 'T'; StartIdentity = '99999'; OperatorOnly = $false }) },
        [pscustomobject]@{ Name = 'unknown child'; Children = @([pscustomobject]@{ ProcessId = 43; Command = '/usr/bin/other-service'; ParentLine = 'PPid: 1'; State = 'S'; StartIdentity = '12348'; OperatorOnly = $false }) })) {
        $rejected = $false
        try { [void](Get-Pid1MigrationWorkTransition ([pscustomobject]@{ Pid1Command = $root; Children = $invalid.Children }) $operationId $cli) }
        catch { $rejected = $true }
        if (-not $rejected) { throw "The pending migration transition accepted $($invalid.Name)." }
    }

    $badRootRejected = $false
    try { [void](Get-Pid1MigrationWorkTransition ([pscustomobject]@{ Pid1Command = 'dotnet Api.dll --initapp'; Children = @() }) $operationId $cli) }
    catch { $badRootRejected = $true }
    if (-not $badRootRejected) { throw 'The migration transition accepted an unsafe PID-1 root.' }

    foreach ($case in @(
        [pscustomobject]@{ Name = 'permission denied'; Error = [UnauthorizedAccessException]::new('permission denied') },
        [pscustomobject]@{ Name = 'transport failure'; Error = [IO.IOException]::new('transport failed') },
        [pscustomobject]@{ Name = 'malformed process snapshot'; Error = [FormatException]::new('malformed snapshot') },
        [pscustomobject]@{ Name = 'unknown process error'; Error = [InvalidOperationException]::new('unknown process failure') })) {
        $script:migrationSnapshotMockError = $case.Error
        function Get-ContainerProcessSnapshot([string] $Container) { throw $script:migrationSnapshotMockError }
        try {
            $failedClosed = $false
            try { [void](Wait-ForPid1MigrationWork 'mock' $operationId $cli 1) }
            catch { $failedClosed = $_.Exception.Message -ceq $case.Error.Message }
            if (-not $failedClosed) { throw "The wait helper swallowed or rewrote $($case.Name)." }
        }
        finally {
            Set-Item Function:\Get-ContainerProcessSnapshot -Value $originalSnapshotFunction
            Remove-Variable migrationSnapshotMockError -Scope Script -ErrorAction SilentlyContinue
        }
    }
    Write-Output 'PASS PID-1 work transition oracle: exact pending states, unsafe child/root rejection, and fail-closed permission/transport/malformed/unknown errors'
}

function Get-ContainerProcessStartIdentity([string] $Container, [int] $ProcessId) {
    try {
        $stat = Invoke-ContainerProcRead $Container "/proc/$ProcessId/stat"
        if ($stat -notmatch '^\d+ \(.*\) (?<fields>.*)$') {
            throw "Could not parse start identity for process PID $ProcessId."
        }
        $fields = @($Matches.fields -split '\s+')
        if ($fields.Count -le 19) { throw "Process PID $ProcessId had incomplete identity data." }
        if ($fields[0] -in @('Z', 'X')) { return $null }
        return $fields[19]
    }
    catch {
        if ($_.Exception -is [IO.FileNotFoundException]) { return $null }
        throw
    }
}

function Wait-ForContainerProcessReaped([string] $Container, [int] $ProcessId, [string] $StartIdentity,
    [int] $TimeoutSeconds = 30) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $current = Get-ContainerProcessStartIdentity $Container $ProcessId
        if ($null -eq $current -or $current -cne $StartIdentity) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Owned process $ProcessId/$StartIdentity was not individually reaped within ${TimeoutSeconds}s."
}

function Wait-ForPid1MigrationWork([string] $Container, [string] $OperationId, $ExpectedFixedCli,
    [int] $TimeoutSeconds = 30) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $snapshot = Get-ContainerProcessSnapshot $Container
            $transition = Get-Pid1MigrationWorkTransition $snapshot $OperationId $ExpectedFixedCli
            if ($transition.Mode -ceq 'ActiveWork') { return $transition.Work }
            $lastError = $transition.Mode
            Start-Sleep -Milliseconds 250
        }
        catch [IO.FileNotFoundException] {
            $lastError = $_
            Start-Sleep -Milliseconds 250
        }
    }
    $lastDescription = if ($lastError -is [System.Management.Automation.ErrorRecord]) { $lastError.Exception.Message } else { [string]$lastError }
    throw "PID-1 migration work shape did not stabilize within ${TimeoutSeconds}s: $lastDescription"
}

function Resume-FixedCli([string] $Container, $ExpectedFixedCli) {
    $resumeScript = @'
set -eu
pid="$1"
expected_start="$2"
read_start() { awk '{print $22}' "/proc/$1/stat" 2>/dev/null; }
read_parent() { awk '/^PPid:/ {print $2}' "/proc/$1/status" 2>/dev/null; }
before_start=$(read_start "$pid")
before_parent=$(read_parent "$pid")
[ "$before_start" = "$expected_start" ] && [ "$before_parent" = 1 ]
kill -CONT "$pid"
after_start=$(read_start "$pid")
after_parent=$(read_parent "$pid")
[ "$after_start" = "$expected_start" ] && [ "$after_parent" = 1 ]
'@
    [void](Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $resumeScript,
        'hrp-fixed-cli-resume', [string]$ExpectedFixedCli.ProcessId, [string]$ExpectedFixedCli.StartIdentity) 15)
}

function Assert-AuthenticatedMigrationPage([string] $BaseUrl, [string] $ApiKey, [string] $OperationId,
    [string] $Observation, [ValidateSet('Normal', 'ActiveWriter', 'FailedHost')] [string] $HostMode = 'Normal') {
    $returnUrl = "/settings/horse-id-migration?operationId=$OperationId"
    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $login = Invoke-WebRequest -Uri ($BaseUrl + '/login') -WebSession $session -TimeoutSec 15
    if ($login.StatusCode -ne 200) { throw "The $Observation login page was not available: $($login.StatusCode)" }
    $loginResponse = Invoke-WebRequest -Method Post -Uri ($BaseUrl + '/login') -WebSession $session `
        -Body @{ username = 'user'; password = $ApiKey; ReturnUrl = $returnUrl } -TimeoutSec 15
    if ($loginResponse.StatusCode -notin @(200, 302)) {
        throw "The $Observation operator login did not complete: $($loginResponse.StatusCode)"
    }
    $page = Invoke-WebRequest -Uri ($BaseUrl + $returnUrl) -WebSession $session -TimeoutSec 15
    if ($page.StatusCode -ne 200) { throw "The authenticated $Observation migration page was unavailable: $($page.StatusCode)" }
    $html = [string]$page.Content
    if ($html -notmatch [regex]::Escape($OperationId)) {
        throw "The authenticated $Observation migration page did not preserve the operation identity."
    }
    $verificationAction = "/settings/horse-id-migration/$OperationId/verify"
    $hasVerificationForm = $html -match [regex]::Escape($verificationAction)
    if ($HostMode -ne 'Normal') {
        $headers = @{ 'X-Api-Key' = $ApiKey }
        $businessApi = Invoke-WebRequest -Uri ($BaseUrl + '/api/races/not-available') -Headers $headers `
            -WebSession $session -SkipHttpErrorCheck -TimeoutSec 15
        if ($businessApi.StatusCode -ne 503) {
            throw "The $HostMode $Observation surface exposed a business API: $($businessApi.StatusCode)"
        }
        $execute = Invoke-WebRequest -Method Post -Uri ($BaseUrl + "/api/v2/admin/horse-id-migration-operations/$OperationId/execute") `
            -Headers $headers -WebSession $session -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck -TimeoutSec 15
        if ($execute.StatusCode -ne 503) {
            throw "The $HostMode $Observation surface exposed migration apply: $($execute.StatusCode)"
        }
        $verify = Invoke-WebRequest -Method Post -Uri ($BaseUrl + "/settings/horse-id-migration/$OperationId/verify") `
            -Headers $headers -WebSession $session -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck -TimeoutSec 15
        Assert-VerificationSurfaceContract $HostMode $verify.StatusCode $hasVerificationForm
    }
    elseif ($hasVerificationForm) {
        throw "The authenticated $Observation migration page exposed a verification form before an eligible verified operation."
    }
}

function Test-Pid1ApiSnapshotCounterexamples {
    $base = [pscustomobject]@{
        Pid1Command = 'dotnet HorseRacingPrediction.Api.dll supervise'
        Children = @([pscustomobject]@{
            ProcessId = 40
            Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child'
            ParentLine = 'PPid: 1'
            State = 'S'
            StartIdentity = '12345'
        })
    }
    [void](Assert-Pid1ApiSnapshot $base '--internal-api-child')
    if ((Get-Pid1ChildMode $base.Children[0].Command) -ne 'api-before-or-after') {
        throw 'The PID-1 command classifier rejected the expected internal API child.'
    }
    $fixedCliCommand = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration 9c8c9dc5-65ff-445f-a888-afc1988bbb69'
    if ((Get-Pid1ChildMode $fixedCliCommand) -ne 'fixed-cli') {
        throw 'The PID-1 command classifier rejected the fixed migration CLI with its actual operation-ID argv.'
    }
    foreach ($unexpectedCommand in @(
        '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration-operation 9c8c9dc5-65ff-445f-a888-afc1988bbb69',
        '/usr/share/dotnet/dotnet /app/Other.Api.dll horse-id-migration 9c8c9dc5-65ff-445f-a888-afc1988bbb69',
        '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll horse-id-migration invalid-id extra-arg'
    )) {
        if ($null -ne (Get-Pid1ChildMode $unexpectedCommand)) {
            throw "The PID-1 command classifier accepted an unexpected child: $unexpectedCommand"
        }
    }
    foreach ($invalid in @(
        [pscustomobject]@{ Snapshot = [pscustomobject]@{ Pid1Command = $base.Pid1Command; Children = @([pscustomobject]@{
            ProcessId = 40; Command = $base.Children[0].Command; ParentLine = 'PPid: 0'; State = 'S'; StartIdentity = '12345'
        }) }; Reason = 'wrong parent' },
        [pscustomobject]@{ Snapshot = [pscustomobject]@{ Pid1Command = $base.Pid1Command; Children = @(
            $base.Children[0], [pscustomobject]@{ ProcessId = 41; Command = '/usr/bin/other-service'; ParentLine = 'PPid: 1'; State = 'S'; StartIdentity = '12346' }
        ) }; Reason = 'extra live service' }
    )) {
        $rejected = $false
        try { [void](Assert-Pid1ApiSnapshot $invalid.Snapshot '--internal-api-child') }
        catch { $rejected = $true }
        if (-not $rejected) { throw "The PID-1 API-shape assertion accepted the $($invalid.Reason) counterexample." }
    }
}

function Test-NormalApiChildTransitionCounterexamples {
    $candidate = [pscustomobject]@{
        ProcessId = 40
        Command = '/usr/share/dotnet/dotnet /app/HorseRacingPrediction.Api.dll --internal-api-child'
        ParentLine = 'PPid: 1'
        State = 'S'
        StartIdentity = '12345'
        OperatorOnly = $false
    }
    $verified = [pscustomobject]@{ status = 'Succeeded'; executionPhase = 'Verified' }
    $freshSnapshot = [pscustomobject]@{ Pid1Command = 'dotnet HorseRacingPrediction.Api.dll supervise'; Children = @($candidate) }
    [void](Assert-VerifiedNormalApiChildTransition $verified $freshSnapshot $candidate)

    $rejected = $false
    try { [void](Assert-VerifiedNormalApiChildTransition ([pscustomobject]@{ status = 'InProgress'; executionPhase = 'Applying' }) $freshSnapshot $candidate) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'The stale pre-Verified status was accepted for a normal API child.' }

    foreach ($invalidChild in @(
        [pscustomobject]@{ ProcessId = 41; Command = $candidate.Command; ParentLine = 'PPid: 1'; StartIdentity = '12345'; OperatorOnly = $false; Reason = 'changed PID' },
        [pscustomobject]@{ ProcessId = 40; Command = $candidate.Command; ParentLine = 'PPid: 1'; StartIdentity = '12346'; OperatorOnly = $false; Reason = 'changed start identity' },
        [pscustomobject]@{ ProcessId = 40; Command = $candidate.Command; ParentLine = 'PPid: 2'; StartIdentity = '12345'; OperatorOnly = $false; Reason = 'wrong parent' },
        [pscustomobject]@{ ProcessId = 40; Command = $candidate.Command; ParentLine = 'PPid: 1'; StartIdentity = '12345'; OperatorOnly = $true; Reason = 'operator-only child' }
    )) {
        $invalidSnapshot = [pscustomobject]@{ Pid1Command = $freshSnapshot.Pid1Command; Children = @($invalidChild) }
        $rejected = $false
        try { [void](Assert-VerifiedNormalApiChildTransition $verified $invalidSnapshot $candidate) }
        catch { $rejected = $true }
        if (-not $rejected) { throw "The verified normal API transition accepted the $($invalidChild.Reason) counterexample." }
    }

    $duplicateSnapshot = [pscustomobject]@{ Pid1Command = $freshSnapshot.Pid1Command; Children = @($candidate, $candidate) }
    $rejected = $false
    try { [void](Assert-VerifiedNormalApiChildTransition $verified $duplicateSnapshot $candidate) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'The verified normal API transition accepted duplicate children.' }
    Write-Output 'PASS verified normal API transition refresh, identity, parent, operator, and duplicate counterexamples'
}

function Test-PreparationWindowObservation([DateTimeOffset] $ObservedAt, [DateTimeOffset] $Deadline,
    [string] $Phase, [bool] $ApiChildMatches, [bool] $ProcessSnapshotFailed) {
    if ($ObservedAt -ge $Deadline) { return $true }
    if ($Phase -notin @('Preparing', 'Draining')) {
        throw "Execution left preparation before the full collection-pause window: $Phase"
    }
    if ($ProcessSnapshotFailed) { throw 'The API process snapshot failed before the full collection-pause window.' }
    if (-not $ApiChildMatches) { throw 'The API must remain the only PID-1 service child before the pause window elapses.' }
    return $false
}

function Test-PreparationWindowCounterexamples {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(1)
    if (Test-PreparationWindowObservation ($deadline.AddMilliseconds(-1)) $deadline 'Draining' $true $false) {
        throw 'The drain-window decision ended before its deadline.'
    }
    foreach ($observation in @(
        [pscustomobject]@{ At = $deadline.AddMilliseconds(-1); Phase = 'StoppingApi'; ApiChildMatches = $false; Failed = $false },
        [pscustomobject]@{ At = $deadline.AddMilliseconds(-1); Phase = 'Draining'; ApiChildMatches = $false; Failed = $false },
        [pscustomobject]@{ At = $deadline.AddMilliseconds(-1); Phase = 'Draining'; ApiChildMatches = $true; Failed = $true }
    )) {
        $rejected = $false
        try { [void](Test-PreparationWindowObservation $observation.At $deadline $observation.Phase $observation.ApiChildMatches $observation.Failed) }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'The pre-deadline drain counterexample was accepted.' }
    }
    foreach ($observation in @(
        [pscustomobject]@{ Phase = 'StoppingApi'; ApiChildMatches = $false; Failed = $false },
        [pscustomobject]@{ Phase = 'Draining'; ApiChildMatches = $false; Failed = $false },
        [pscustomobject]@{ Phase = 'Draining'; ApiChildMatches = $false; Failed = $true }
    )) {
        if (-not (Test-PreparationWindowObservation $deadline.AddMilliseconds(1) $deadline $observation.Phase $observation.ApiChildMatches $observation.Failed)) {
            throw 'The post-deadline drain boundary was not accepted.'
        }
    }
}

function Wait-ForMigration([string] $BaseUrl, [string] $ApiKey, [string] $OperationId,
    [int] $TimeoutMinutes = 40) {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($TimeoutMinutes)
    $statusPath = "/api/v2/admin/horse-id-migration-operations/$OperationId"
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $status = Invoke-Api $BaseUrl $statusPath -ApiKey $ApiKey
            if ($status.status -eq 'Succeeded' -and $status.executionPhase -eq 'Verified') { return $status }
            if ($status.status -in @('Partial', 'Failed', 'Unknown')) {
                throw "Migration stopped safely at status '$($status.status)' phase '$($status.executionPhase)'."
            }
        } catch {
            if ($_.Exception.Message -like 'Migration stopped safely*') { throw }
        }
        Start-Sleep -Seconds 1
    }
    throw "The bounded supervised migration did not reach Verified within ${TimeoutMinutes} minutes."
}

function Wait-ForPreparationDrain([string] $BaseUrl, [string] $ApiKey, [string] $OperationId,
    [string] $Container, [string] $PauseTimestampPath, [int] $ApiProcessId, [bool] $AllowAlreadyElapsed = $false) {
    $pauseStartedAt = [DateTimeOffset]::Parse((Get-Content -Raw -LiteralPath $PauseTimestampPath).Trim()).ToUniversalTime()
    $deadline = $pauseStartedAt.AddSeconds(900)
    $sawDraining = $false
    $statusPath = "/api/v2/admin/horse-id-migration-operations/$OperationId"
    while ($true) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        try {
            $status = Invoke-Api $BaseUrl $statusPath -ApiKey $ApiKey
        }
        catch {
            if (Test-PreparationWindowObservation ([DateTimeOffset]::UtcNow) $deadline 'Draining' $false $true) { break }
            throw
        }
        if ($status.executionPhase -eq 'Draining') { $sawDraining = $true }
        if (Test-PreparationWindowObservation ([DateTimeOffset]::UtcNow) $deadline $status.executionPhase $true $false) { break }
        try {
            $snapshot = Get-ContainerProcessSnapshot $Container
        }
        catch {
            if (Test-PreparationWindowObservation ([DateTimeOffset]::UtcNow) $deadline $status.executionPhase $false $true) { break }
            throw
        }
        $apiChildMatches = $snapshot.Children.Count -eq 1 -and
            $snapshot.Children[0].ProcessId -eq $ApiProcessId -and
            $snapshot.Children[0].Command -match '--internal-api-child'
        if (Test-PreparationWindowObservation ([DateTimeOffset]::UtcNow) $deadline $status.executionPhase $apiChildMatches $false) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $sawDraining -and -not $AllowAlreadyElapsed) {
        throw 'Execute did not enter the persisted Draining phase during its full pause window.'
    }
}

function Wait-ForStoppedCliAfterBackup([string] $Container, [string] $DataDirectory,
    [string] $OperationId, $ObserverResult, [int] $TimeoutSeconds = 300) {
    $backupDirectory = Join-Path $DataDirectory "backups/horse-id-migration-$($OperationId.Replace('-', ''))"
    $receiptPath = Join-Path $backupDirectory 'receipt.json'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $snapshot = Get-ContainerProcessSnapshot $Container
        try {
            $shape = Assert-Pid1MigrationWorkSnapshot $snapshot $OperationId $ObserverResult
            $child = $shape.FixedCli
            if ($child.State -in @('T', 't') -and (Test-Path -LiteralPath $receiptPath)) {
                return $child
            }
        }
        catch {
            if ($snapshot.Children.Count -gt 2) {
                throw 'The supervisor exposed more than the fixed CLI and operator direct children during the crash boundary.'
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'The owned fixed CLI was not observed stopped under PID 1 after API exit and complete backup.'
}

function Stage-Operation([string] $BaseUrl, [string] $ApiKey, [string] $RootName) {
    $manifest = @{ entries = @(@{
        oldHorseId = $oldHorseId
        jraIdentity = $jraIdentity
        hypothesisAccepted = $true
    }) }
    $review = Invoke-Api $BaseUrl '/api/v2/admin/horse-id-migration-previews?kind=TargetReviewV2' 'POST' $manifest $ApiKey
    if (-not $review.canSavePending -or @($review.blockers).Count -ne 0) {
        $blockers = @($review.blockers) -join ','
        throw "The isolated seeded migration fixture failed point review for $RootName (canSavePending=$($review.canSavePending), blockers=$blockers)."
    }
    $requestKey = [guid]::NewGuid().ToString('D')
    $staged = Invoke-Api $BaseUrl '/api/v2/admin/horse-id-migration-operations' 'POST' `
        @{ requestKind = 'PendingV2'; manifest = $manifest; requestKey = $requestKey; previewFingerprint = $null } $ApiKey
    $operationId = [string]$staged.operationId
    if ([string]$staged.requestKind -ne 'PendingV2' -or $null -ne $staged.previewFingerprint) {
        throw "The $RootName stage response did not preserve PendingV2 with a null preview fingerprint."
    }
    $readback = Invoke-Api $BaseUrl "/api/v2/admin/horse-id-migration-operations/$operationId" -ApiKey $ApiKey
    if ([string]$readback.requestKind -ne 'PendingV2' -or $null -ne $readback.previewFingerprint) {
        throw "The $RootName journal readback did not preserve PendingV2 with a null preview fingerprint."
    }
    Add-EvidenceEvent -Event 'pending-stage-readback' -OperationId $operationId -Phase ([string]$readback.executionPhase) `
        -Status ([string]$readback.status) `
        -Detail "root=$RootName;requestKind=PendingV2;previewFingerprint=null;review=target-only;fullCensus=false"
    return [pscustomobject]@{ OperationId = $operationId; RequestKey = $requestKey }
}

function Reset-DisposablePauseWindow([string] $BaseUrl, [string] $ApiKey, [string] $PauseTimestampPath) {
    $before = Invoke-Api $BaseUrl '/api/v2/admin/collection/pipeline-state' -ApiKey $ApiKey
    if (-not $before.pipeline.isPaused -or -not $before.pipeline.updatedAt) {
        throw 'The disposable collection pipeline must already be paused before refreshing its drain window.'
    }
    [void](Invoke-Api $BaseUrl '/api/v2/admin/collection/pipeline' 'PUT' `
        @{ pipeline = @{ paused = $true; reason = 'isolated horse identity supervisor lifecycle proof' } } $ApiKey)
    $state = Invoke-Api $BaseUrl '/api/v2/admin/collection/pipeline-state' -ApiKey $ApiKey
    if (-not $state.pipeline.isPaused -or -not $state.pipeline.updatedAt) {
        throw 'The disposable collection pipeline did not persist its paused state.'
    }
    if ([DateTimeOffset]::Parse([string]$state.pipeline.updatedAt) -eq
        [DateTimeOffset]::Parse([string]$before.pipeline.updatedAt)) {
        throw 'The explicit pause mutation did not refresh the persisted pause timestamp.'
    }
    Set-Content -LiteralPath $PauseTimestampPath -Value ([DateTimeOffset]::Parse([string]$state.pipeline.updatedAt).ToUniversalTime().ToString('O')) -NoNewline
}

function Assert-AgedDisposablePauseWindow([string] $BaseUrl, [string] $ApiKey, [string] $PauseTimestampPath) {
    $state = Invoke-Api $BaseUrl '/api/v2/admin/collection/pipeline-state' -ApiKey $ApiKey
    if (-not $state.pipeline.isPaused -or -not $state.pipeline.updatedAt) {
        throw 'The disposable pipeline must remain paused with a persisted timestamp for the fast lifecycle preflight.'
    }
    $fixtureTimestamp = [DateTimeOffset]::Parse((Get-Content -Raw -LiteralPath $PauseTimestampPath).Trim()).ToUniversalTime()
    $persistedTimestamp = [DateTimeOffset]::Parse([string]$state.pipeline.updatedAt).ToUniversalTime()
    if ([Math]::Abs(($persistedTimestamp - $fixtureTimestamp).TotalSeconds) -ge 1) {
        throw 'API bootstrap changed the persisted pause timestamp from the exported fast fixture.'
    }
    if (([DateTimeOffset]::UtcNow - $persistedTimestamp).TotalSeconds -le 900) {
        throw 'The persisted disposable pause timestamp is not older than the full 900-second drain window.'
    }
}

function Assert-DisposablePath([string] $Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = $tempParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to mutate a path outside this script's temporary test root: $resolved"
    }
}

function Wait-ForContainerRestart([string] $Container, [int] $ExpectedRestarts, [int] $TimeoutSeconds = 90) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $json = Invoke-Docker @('inspect', $Container, '--format', '{{json .State}}|{{.RestartCount}}')
        $parts = $json.Trim().Split('|', 2)
        $state = $parts[0] | ConvertFrom-Json
        $restarts = [int]$parts[1]
        if ($state.Status -eq 'running' -and $restarts -ge $ExpectedRestarts) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "The disposable coordinator did not restart into its bounded minimal-host state."
}

try {
    $rootEnvironmentFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Filter '.env*' -Force -Name -ErrorAction SilentlyContinue)
    if ($rootEnvironmentFiles.Count -gt 0) {
        throw 'Refusing a Docker build while repository-root .env files may enter the build context.'
    }
    [void](New-Item -ItemType Directory -Path $tempRoot)
    Assert-DisposablePath $tempRoot
    [void](New-Item -ItemType Directory -Path $evidenceRoot)
    [void](New-Item -ItemType Directory -Path $happyData)
    [void](New-Item -ItemType Directory -Path $crashData)
    $gitPath = (Get-Command git -ErrorAction Stop).Source
    $gitRevision = (Invoke-External $gitPath @('rev-parse', 'HEAD') 30).Trim()
    $sourceHashes = Get-SourceHashes
    Add-EvidenceEvent -Event 'run-start' -Detail ("mode=$harnessMode;revision=$gitRevision;sourceHashes=" +
        (ConvertTo-Json -InputObject $sourceHashes -Compress))
    Test-TaskChildrenParserCounterexample
    Test-FixedCliObserverCounterexamples
    Test-Pid1ApiSnapshotCounterexamples
    Test-NormalApiChildTransitionCounterexamples
    Test-Pid1MigrationWorkTransitionCounterexamples
    Test-PreparationWindowCounterexamples
    Test-PendingPrewriteBlockedStatusCounterexamples
    Test-SafelyClassifiedChildFailureStatusCounterexamples
    Test-OperatorApiCandidateCounterexamples
    Test-ProcReadRaceCounterexamples
    Test-ApiTransportCounterexamples
    Test-AuthenticatedMigrationSurfaceCounterexamples
    Test-FixedCliObserverWrapperSmokeCounterexamples
    Test-ExternalProcessWrapper
    if ($WrapperSmokeOnly) {
        Write-Output 'PASS bounded process wrapper stdout/stderr, EOF, timeout, and owned-tree cleanup smoke'
        $harnessSucceeded = $true
        return
    }

    Invoke-Docker @('build', '--file', (Join-Path $repositoryRoot 'Dockerfile'), '--tag', $image, $repositoryRoot) 1800 | Out-Null
    $imageIdentity = (Invoke-Docker @('image', 'inspect', '--format', '{{.Id}}', $image)).Trim()
    if ($imageIdentity -notmatch '^sha256:[0-9a-f]{64}$') { throw 'The production test image did not expose a valid immutable image identity.' }
    Add-EvidenceEvent -Event 'image-built' -Detail "imageId=$imageIdentity"
    Test-FixedCliObserverProcReadCounterexamples $image
    if ($ObserverSmokeOnly) {
        Write-Output 'PASS fixed CLI observer proc-read churn, post-stop ambiguity, and exact PID/start liveness counterexamples'
        $harnessSucceeded = $true
        return
    }

    $fixtureArguments = @(
        'test', $testProject, '-c', 'Release', '--no-restore', '--filter', $fixtureMethod,
        '--logger', 'console;verbosity=minimal')
    $fixtureOutput = Invoke-External -FilePath $dotnetCommand -Arguments $fixtureArguments -TimeoutSeconds 300 `
        -Environment @{
            HRP_HORSE_IDENTITY_SUPERVISOR_FIXTURE_DIR = $happyData
            HRP_HORSE_IDENTITY_SUPERVISOR_FIXTURE_PAUSE_AGED = $(if ($FastLifecycleSmokeOnly) { '1' } else { '' })
        }
    if (-not $fixtureOutput.Contains('ExportProductionContainerFixtureWhenRequested')) {
        # VSTest may suppress names for a one-test success; filesystem evidence below is authoritative.
    }
    Copy-Item -LiteralPath (Join-Path $happyData 'eventstore.db') -Destination (Join-Path $crashData 'eventstore.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'collection-platform.db') -Destination (Join-Path $crashData 'collection-platform.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'prediction-executions.db') -Destination (Join-Path $crashData 'prediction-executions.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'pause-start.txt') -Destination (Join-Path $crashData 'pause-start.txt')

    $apiKey = 'local-supervisor-' + [guid]::NewGuid().ToString('N')
    $sensitiveValues.Add($apiKey)
    $published = Invoke-Docker @(
        'run', '-d', '--name', $happyContainer, '--label', "hrp.test.run=$runId", '--restart=no',
        '--publish', '127.0.0.1::8080',
        '--env', 'ASPNETCORE_ENVIRONMENT=Production',
        '--env', 'AWS_REGION=ap-northeast-1',
        '--env', 'ASPNETCORE_URLS=http://+:8080',
        '--env', 'ConnectionStrings__EventStore=Data Source=/data/eventstore.db',
        '--env', 'CollectionPlatform__StateDirectory=/data',
        '--env', 'CollectionPlatform__DatabaseFileName=collection-platform.db',
        '--env', 'PredictionScheduling__StateDirectory=/data',
        '--env', 'PredictionScheduling__StoreFileName=prediction-executions.db',
        '--env', 'CollectionQueue__Provider=Sqs',
        '--env', 'CollectionQueue__Enabled=false',
        '--env', 'CollectionMonitoring__Enabled=false',
        '--env', 'JobFailureNotifications__Enabled=false',
        '--env', "HORSE_RACING_API_KEY=$apiKey",
        '--volume', "${happyData}:/data:rw", $image)
    $happyPort = Get-PublishedPort $happyContainer
    $happyUrl = "http://$($PublishedHost):$happyPort"
    Wait-Api $happyUrl $apiKey
    if ($FastLifecycleSmokeOnly) {
        Assert-AgedDisposablePauseWindow $happyUrl $apiKey (Join-Path $happyData 'pause-start.txt')
    }
    $initialChild = Assert-Pid1ApiShape $happyContainer '--internal-api-child'
    $happyAdmission = Stage-Operation $happyUrl $apiKey 'happy'
    $happyOperationId = $happyAdmission.OperationId
    Assert-AuthenticatedMigrationPage $happyUrl $apiKey $happyOperationId 'pre-work'
    Add-EvidenceEvent -Event 'happy-execute-requested' -OperationId $happyOperationId -Phase 'IntentRecorded' `
        -Status 'InProgress' -Detail 'requestKind=PendingV2'
    if (-not $FastLifecycleSmokeOnly) {
        Reset-DisposablePauseWindow $happyUrl $apiKey (Join-Path $happyData 'pause-start.txt')
    }
    $happyObserver = Start-FixedCliObserver $happyContainer $happyOperationId 'stop'
    [void](Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId/execute" 'POST' @{} $apiKey)
    Wait-ForPreparationDrain $happyUrl $apiKey $happyOperationId $happyContainer `
        (Join-Path $happyData 'pause-start.txt') $initialChild.ProcessId ([bool]$FastLifecycleSmokeOnly)
    $observerTimeoutSeconds = if ($FastLifecycleSmokeOnly) { 180 } else { 1250 }
    $happyCli = Wait-FixedCliObserver $happyContainer $happyObserver $observerTimeoutSeconds
    $happyCliHeld = $true
    $happyWorkShape = Wait-ForPid1MigrationWork $happyContainer $happyOperationId $happyCli
    Assert-AuthenticatedMigrationPage $happyUrl $apiKey $happyOperationId 'during-work-before-operator-restart' 'ActiveWriter'
    $operatorBefore = $happyWorkShape.Operator
    $statusBeforeOperatorRestart = Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId" -ApiKey $apiKey
    [void](Invoke-Docker @('exec', $happyContainer, '/bin/kill', '-KILL', [string]$operatorBefore.ProcessId))
    Wait-ForContainerProcessReaped $happyContainer $operatorBefore.ProcessId $operatorBefore.StartIdentity
    $restartedWorkShape = Wait-ForPid1MigrationWork $happyContainer $happyOperationId $happyCli
    if ($restartedWorkShape.Operator.StartIdentity -ceq $operatorBefore.StartIdentity) {
        throw 'The operator restart reused the killed process start identity.'
    }
    $statusAfterOperatorRestart = Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId" -ApiKey $apiKey
    if ([string]$statusAfterOperatorRestart.operationId -cne [string]$statusBeforeOperatorRestart.operationId -or
        [string]$statusAfterOperatorRestart.requestKind -cne [string]$statusBeforeOperatorRestart.requestKind) {
        throw 'The read-only operator restart changed the active migration intent.'
    }
    Assert-AuthenticatedMigrationPage $happyUrl $apiKey $happyOperationId 'during-work-after-operator-restart' 'ActiveWriter'
    Add-EvidenceEvent -Event 'operator-restarted-read-only' -OperationId $happyOperationId `
        -Phase ([string]$statusAfterOperatorRestart.executionPhase) -Status ([string]$statusAfterOperatorRestart.status) `
        -Detail "fixedCli=$($happyCli.ProcessId)/$($happyCli.StartIdentity);oldOperator=$($operatorBefore.ProcessId)/$($operatorBefore.StartIdentity);newOperator=$($restartedWorkShape.Operator.ProcessId)/$($restartedWorkShape.Operator.StartIdentity)"

    Resume-FixedCli $happyContainer $happyCli
    $happyCliHeld = $false
    Add-EvidenceEvent -Event 'happy-fixed-cli-resumed' -OperationId $happyOperationId `
        -Phase ([string]$statusAfterOperatorRestart.executionPhase) -Status ([string]$statusAfterOperatorRestart.status) `
        -Detail "fixedCli=$($happyCli.ProcessId)/$($happyCli.StartIdentity);resume=SIGCONT;parent=1"

    $happyDeadline = [DateTimeOffset]::UtcNow.AddMinutes($(if ($FastLifecycleSmokeOnly) { 4 } else { 40 }))
    $observedChildModes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($happyCli.ProcessId -le 0 -or $happyCli.StartIdentity -notmatch '^\d+$') {
        throw 'The happy-path observer did not record the exact fixed CLI process identity.'
    }
    [void]$observedChildModes.Add('fixed-cli')
    while ([DateTimeOffset]::UtcNow -lt $happyDeadline) {
        $status = Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId" -ApiKey $apiKey
        if ($status.status -eq 'Succeeded' -and $status.executionPhase -eq 'Verified') { break }
        if ($status.status -in @('Partial', 'Failed', 'Unknown')) {
            throw "Migration stopped safely at status '$($status.status)' phase '$($status.executionPhase)'."
        }
        $snapshot = Get-ContainerProcessSnapshot $happyContainer
        $transition = Get-Pid1MigrationWorkTransition $snapshot $happyOperationId $happyCli
        switch -CaseSensitive ($transition.Mode) {
            'ActiveWork' { [void]$observedChildModes.Add('fixed-cli') }
            'OperatorOnlyTransition' { [void]$observedChildModes.Add('operator-only') }
            'ReapingTransition' { [void]$observedChildModes.Add('reaping-transition') }
            'FixedCliOnlyTransition' { [void]$observedChildModes.Add('fixed-cli') }
            'NormalApiTransition' {
                $freshStatus = Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId" -ApiKey $apiKey
                $freshSnapshot = Get-ContainerProcessSnapshot $happyContainer
                [void](Assert-VerifiedNormalApiChildTransition $freshStatus $freshSnapshot $transition.Child)
                $status = $freshStatus
                break
            }
            default { throw "The happy lifecycle exposed an unclassified process transition: $($transition.Mode)." }
        }
        if ($transition.Mode -ceq 'NormalApiTransition') { break }
        Start-Sleep -Milliseconds 250
    }
    if ($status.status -ne 'Succeeded' -or $status.executionPhase -ne 'Verified') {
        throw "The production-image happy path did not complete as Verified within its bound."
    }
    if ([string]$status.requestKind -ne 'PendingV2' -or $null -ne $status.previewFingerprint) {
        throw 'The successful PendingV2 lifecycle lost its request kind or acquired a preview fingerprint.'
    }
    Add-EvidenceEvent -Event 'happy-verified' -OperationId $happyOperationId -Phase ([string]$status.executionPhase) `
        -Status ([string]$status.status) -Detail 'requestKind=PendingV2;previewFingerprint=null'
    if (-not $observedChildModes.Contains('fixed-cli')) {
        throw 'The production-image path did not expose the supervised fixed CLI under PID 1.'
    }
    Wait-ForContainerProcessReaped $happyContainer $happyCli.ProcessId $happyCli.StartIdentity
    Wait-ForContainerProcessReaped $happyContainer $restartedWorkShape.Operator.ProcessId $restartedWorkShape.Operator.StartIdentity
    $finalChild = Assert-Pid1ApiShape $happyContainer '--internal-api-child'
    if ($finalChild.OperatorOnly) {
        throw 'The final happy-path API child remained operator-only after Verified.'
    }
    Wait-ForContainerProcessReaped $happyContainer $initialChild.ProcessId $initialChild.StartIdentity
    if ($finalChild.ProcessId -eq $initialChild.ProcessId) {
        throw 'The original API child was not exited/reaped before the paused API restart.'
    }
    $happyBackupDirectory = Join-Path $happyData "backups/horse-id-migration-$($happyOperationId.Replace('-', ''))"
    $happyReceiptPath = Join-Path $happyBackupDirectory 'receipt.json'
    if (-not (Test-Path -LiteralPath $happyReceiptPath)) {
        throw 'The complete managed backup package is absent after the successful process handoff.'
    }
    Add-EvidenceEvent -Event 'happy-backup' -OperationId $happyOperationId -Phase ([string]$status.executionPhase) `
        -Status 'Present' -Detail ("receiptSha256=" + (Get-FileHash -Algorithm SHA256 -LiteralPath $happyReceiptPath).Hash)

    Invoke-Docker @('rm', '--force', $happyContainer) | Out-Null

    $crashKey = 'local-supervisor-crash-' + [guid]::NewGuid().ToString('N')
    $sensitiveValues.Add($crashKey)
    [void](Invoke-Docker @(
        'run', '-d', '--name', $crashContainer, '--label', "hrp.test.run=$runId", '--restart', 'on-failure:1',
        '--publish', '127.0.0.1::8080',
        '--env', 'ASPNETCORE_ENVIRONMENT=Production',
        '--env', 'AWS_REGION=ap-northeast-1',
        '--env', 'ASPNETCORE_URLS=http://+:8080',
        '--env', 'ConnectionStrings__EventStore=Data Source=/data/eventstore.db',
        '--env', 'CollectionPlatform__StateDirectory=/data',
        '--env', 'CollectionPlatform__DatabaseFileName=collection-platform.db',
        '--env', 'PredictionScheduling__StateDirectory=/data',
        '--env', 'PredictionScheduling__StoreFileName=prediction-executions.db',
        '--env', 'CollectionQueue__Provider=Sqs',
        '--env', 'CollectionQueue__Enabled=false',
        '--env', 'CollectionMonitoring__Enabled=false',
        '--env', 'JobFailureNotifications__Enabled=false',
        '--env', "HORSE_RACING_API_KEY=$crashKey",
        '--volume', "${crashData}:/data:rw", $image))
    $crashPort = Get-PublishedPort $crashContainer
    $crashUrl = "http://$($PublishedHost):$crashPort"
    Wait-Api $crashUrl $crashKey
    if ($FastLifecycleSmokeOnly) {
        Assert-AgedDisposablePauseWindow $crashUrl $crashKey (Join-Path $crashData 'pause-start.txt')
    }
    $crashAdmission = Stage-Operation $crashUrl $crashKey 'crash'
    $crashOperationId = $crashAdmission.OperationId
    Assert-AuthenticatedMigrationPage $crashUrl $crashKey $crashOperationId 'crash-pre-work'
    Add-EvidenceEvent -Event 'crash-execute-requested' -OperationId $crashOperationId -Phase 'IntentRecorded' `
        -Status 'InProgress' -Detail 'requestKind=PendingV2'
    if (-not $FastLifecycleSmokeOnly) {
        Reset-DisposablePauseWindow $crashUrl $crashKey (Join-Path $crashData 'pause-start.txt')
    }
    $crashInitialChild = Assert-Pid1ApiShape $crashContainer '--internal-api-child'
    $crashObserver = Start-FixedCliObserver $crashContainer $crashOperationId 'stop'
    [void](Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId/execute" 'POST' @{} $crashKey)
    Wait-ForPreparationDrain $crashUrl $crashKey $crashOperationId $crashContainer `
        (Join-Path $crashData 'pause-start.txt') $crashInitialChild.ProcessId ([bool]$FastLifecycleSmokeOnly)
    $crashCli = Wait-FixedCliObserver $crashContainer $crashObserver $observerTimeoutSeconds
    $stoppedCliTimeoutSeconds = if ($FastLifecycleSmokeOnly) { 180 } else { 300 }
    $stoppedCli = Wait-ForStoppedCliAfterBackup $crashContainer $crashData $crashOperationId $crashCli `
        $stoppedCliTimeoutSeconds
    $crashSourceFingerprint = Get-SourceFileFingerprint $crashData
    $crashReceiptPath = Join-Path $crashData "backups/horse-id-migration-$($crashOperationId.Replace('-', ''))/receipt.json"
    if (-not (Test-Path -LiteralPath $crashReceiptPath)) { throw 'The crash boundary has no managed backup receipt.' }
    $crashReceiptHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $crashReceiptPath).Hash
    Add-EvidenceEvent -Event 'crash-child-stopped-after-backup' -OperationId $crashOperationId `
        -Status 'Stopped' -Detail "receiptSha256=$crashReceiptHash;sourceFingerprint=$crashSourceFingerprint"
    Assert-AuthenticatedMigrationPage $crashUrl $crashKey $crashOperationId 'crash-during-work' 'ActiveWriter'
    $beforeKill = Get-ContainerProcessSnapshot $crashContainer
    $beforeKillShape = Assert-Pid1MigrationWorkSnapshot $beforeKill $crashOperationId $stoppedCli
    if ($beforeKillShape.FixedCli.State -notin @('T', 't')) {
        throw 'The exact observed fixed CLI was not still stopped immediately before termination.'
    }
    $crashContainerIdentity = (Invoke-Docker @('inspect', $crashContainer, '--format', '{{.Id}}')).Trim()
    $crashRootStartIdentity = Get-ContainerProcessStartIdentity $crashContainer 1
    if ([string]::IsNullOrWhiteSpace($crashRootStartIdentity)) {
        throw 'The crash fixture did not expose a stable PID-1 start identity before the CLI fault.'
    }
    $crashRestartCountBeforeKill = [int](Invoke-Docker @('inspect', $crashContainer, '--format', '{{.RestartCount}}')).Trim()
    Invoke-Docker @('exec', $crashContainer, '/bin/kill', '-KILL', [string]$crashCli.ProcessId) | Out-Null
    Wait-ForContainerProcessReaped $crashContainer $crashCli.ProcessId $crashCli.StartIdentity
    $crashPort = Get-PublishedPort $crashContainer
    $crashUrl = "http://$($PublishedHost):$crashPort"
    Wait-Api $crashUrl $crashKey
    $afterFaultState = (Invoke-Docker @('inspect', $crashContainer, '--format', '{{.Id}}|{{.State.Status}}|{{.RestartCount}}')).Trim().Split('|')
    if ($afterFaultState[0] -cne $crashContainerIdentity -or $afterFaultState[1] -cne 'running' -or
        [int]$afterFaultState[2] -ne $crashRestartCountBeforeKill) {
        throw "The CLI fault unexpectedly restarted or replaced the owned container: $($afterFaultState -join '|')."
    }
    if ((Get-ContainerProcessStartIdentity $crashContainer 1) -cne $crashRootStartIdentity) {
        throw 'The CLI fault unexpectedly replaced the PID-1 supervisor before the explicit container restart.'
    }
    Wait-ForContainerProcessReaped $crashContainer $crashInitialChild.ProcessId $crashInitialChild.StartIdentity
    $blockedChildBeforeRestart = Assert-Pid1ApiShape $crashContainer '--internal-blocked-child'
    $blockedStatus = Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId" -ApiKey $crashKey
    # Child failure classification is persisted before restart. Depending on whether the writer lease
    # was acquired before SIGSTOP/KILL, the safe terminal public state is Failed/ChildFailed or
    # Partial/OperationInterrupted, with the ChildFailed result retained and no replay permission.
    if (-not (Test-SafelyClassifiedChildFailureStatus $blockedStatus)) {
        throw "After the fixed child crash, restart must expose a safe Applying child-failure classification; observed status='$($blockedStatus.status)' reason='$($blockedStatus.reason)' phase='$($blockedStatus.executionPhase)'."
    }
    $restartedReceiptHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $crashReceiptPath).Hash
    if ($restartedReceiptHash -cne $crashReceiptHash) {
        throw 'The managed backup receipt changed across the fixed CLI fault and blocked restart.'
    }
    Assert-AuthenticatedMigrationPage $crashUrl $crashKey $crashOperationId 'crash-after-cli-fault-before-restart' 'FailedHost'
    Add-EvidenceEvent -Event 'crash-cli-fault-blocked' -OperationId $crashOperationId `
        -Phase ([string]$blockedStatus.executionPhase) -Status ([string]$blockedStatus.status) `
        -Detail ("sameContainer=true;samePid1Start=true;automaticRestart=false;restartCount=$crashRestartCountBeforeKill;blockedChild=$($blockedChildBeforeRestart.ProcessId)/$($blockedChildBeforeRestart.StartIdentity);receiptSha256=$crashReceiptHash")

    Invoke-Docker @('update', '--restart=no', $crashContainer) | Out-Null
    Invoke-Docker @('restart', $crashContainer) 60 | Out-Null
    $crashPort = Get-PublishedPort $crashContainer
    $crashUrl = "http://$($PublishedHost):$crashPort"
    Wait-Api $crashUrl $crashKey
    $afterExplicitRestart = (Invoke-Docker @('inspect', $crashContainer, '--format', '{{.Id}}|{{.State.Status}}|{{.RestartCount}}')).Trim().Split('|')
    if ($afterExplicitRestart[0] -cne $crashContainerIdentity -or $afterExplicitRestart[1] -cne 'running') {
        throw "The explicit fixture restart did not preserve the owned container: $($afterExplicitRestart -join '|')."
    }
    $newRootStartIdentity = Get-ContainerProcessStartIdentity $crashContainer 1
    if ([string]::IsNullOrWhiteSpace($newRootStartIdentity) -or $newRootStartIdentity -ceq $crashRootStartIdentity) {
        throw 'The explicit fixture restart did not create a new PID-1 supervisor identity.'
    }
    Wait-ForContainerProcessReaped $crashContainer $blockedChildBeforeRestart.ProcessId $blockedChildBeforeRestart.StartIdentity
    $blockedChild = Assert-Pid1ApiShape $crashContainer '--internal-blocked-child'
    $blockedStatus = Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId" -ApiKey $crashKey
    if (-not (Test-SafelyClassifiedChildFailureStatus $blockedStatus)) {
        throw "After explicit restart, the child-failure classification changed: status='$($blockedStatus.status)' reason='$($blockedStatus.reason)' phase='$($blockedStatus.executionPhase)'."
    }
    $explicitReceiptHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $crashReceiptPath).Hash
    if ($explicitReceiptHash -cne $crashReceiptHash) {
        throw 'The managed backup receipt changed across the explicit container restart.'
    }
    Assert-AuthenticatedMigrationPage $crashUrl $crashKey $crashOperationId 'crash-after-explicit-restart' 'FailedHost'
    Add-EvidenceEvent -Event 'crash-restarted-blocked' -OperationId $crashOperationId `
        -Phase ([string]$blockedStatus.executionPhase) -Status ([string]$blockedStatus.status) `
        -Detail ("requestKind=PendingV2;previewFingerprint=null;blockedChild=$($blockedChild.ProcessId)/$($blockedChild.StartIdentity);postRestartObserved=$($blockedStatus.status)/$($blockedStatus.reason);childFailedResult=true;automaticRestart=false;explicitRestart=true;restartCountBefore=$crashRestartCountBeforeKill;restartCountAfter=$($afterExplicitRestart[2]);newPid1=$newRootStartIdentity;receiptSha256=$explicitReceiptHash")
    $gatePaths = @(
        (Join-Path $crashData '.eventstore.db.horse-identity-migration.maintenance'),
        (Join-Path $crashData '.collection-platform.db.horse-identity-migration.maintenance'),
        (Join-Path $crashData '.prediction-executions.db.horse-identity-migration.maintenance'))
    if (@($gatePaths | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -ne 0) {
        throw 'Crash restart cleared or lost a maintenance gate before explicit verification.'
    }
    Add-EvidenceEvent -Event 'crash-maintenance-gates' -OperationId $crashOperationId `
        -Phase ([string]$blockedStatus.executionPhase) -Status 'Present' -Detail 'gateCount=3'
    if ((Get-SourceFileFingerprint $crashData) -cne $crashSourceFingerprint) {
        throw 'The killed fixed child changed a durable source or SQLite WAL/SHM file before blocked restart.'
    }
    Start-Sleep -Seconds 5
    $again = Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId" -ApiKey $crashKey
    if (-not (Test-SafelyClassifiedChildFailureStatus $again)) {
        throw 'The blocked restart changed or replayed the safely classified Applying child-failure operation.'
    }
    if ((Get-SourceFileFingerprint $crashData) -cne $crashSourceFingerprint) {
        throw 'The blocked restart mutated source or SQLite WAL/SHM files after the owned CLI was stopped.'
    }

    Invoke-Docker @('update', '--restart=no', $crashContainer) | Out-Null
    Invoke-Docker @('kill', '--signal', 'TERM', $crashContainer) | Out-Null
    $termExitCode = (Invoke-Docker @('wait', $crashContainer) 60).Trim()
    $termState = (Invoke-Docker @('inspect', $crashContainer, '--format', '{{.State.Status}}|{{.State.ExitCode}}')).Trim()
    if ($termExitCode -cne '143' -or $termState -cne 'exited|143') {
        throw "The disposable PID-1 supervisor did not report the expected graceful TERM exit (wait='$termExitCode', state='$termState')."
    }
    Add-EvidenceEvent -Event 'crash-terminated' -OperationId $crashOperationId -Phase 'Applying' `
        -Status 'Exited' -Detail "waitExit=$termExitCode;state=$termState;sourceFingerprint=$crashSourceFingerprint"
    $harnessSucceeded = $true
    if ($FastLifecycleSmokeOnly) {
        Write-Output 'PASS aged-pause FAST lifecycle preflight: production image PID-1/API/fixed-CLI handoff, backup/postcheck, killed-child blocked restart, and bounded cleanup; real 900-second timing remains unproven'
    } else {
        Write-Output 'PASS real production image PID-1/API/fixed-CLI handoff, 900-second pause, backup/postcheck, killed-child blocked restart, and bounded cleanup'
    }
}
finally {
    if ($happyCliHeld) {
        try { Resume-FixedCli $happyContainer $happyCli } catch { }
        $happyCliHeld = $false
    }
    if ($harnessSucceeded) {
        foreach ($container in @($happyContainer, $crashContainer)) {
            try { Invoke-Docker @('rm', '--force', $container) | Out-Null } catch { }
        }
        try { Invoke-Docker @('image', 'rm', '--force', $image) | Out-Null } catch { }
        Assert-DisposablePath $tempRoot
        foreach ($disposableDataPath in @($happyData, $crashData, (Join-Path $tempRoot 'process-tree-probe'),
            (Join-Path $tempRoot 'stalled-child.pid'))) {
            Assert-DisposablePath $disposableDataPath
            if (Test-Path -LiteralPath $disposableDataPath) {
                Remove-Item -LiteralPath $disposableDataPath -Recurse -Force
            }
        }
        Add-EvidenceEvent -Event 'run-complete' -Status 'Passed' `
            -Detail "mode=$harnessMode;revision=$gitRevision;imageId=$imageIdentity"
        Write-Output "PRESERVED_SUCCESS_EVIDENCE root=$tempRoot transcript=$evidenceTranscript"
    }
    else {
        if (Test-Path -LiteralPath $tempRoot) {
            $failureEvidence = Join-Path $tempRoot 'failure-evidence'
            [void](New-Item -ItemType Directory -Path $failureEvidence -Force)
            foreach ($container in @($happyContainer, $crashContainer)) {
                try {
                    Invoke-Docker @('update', '--restart=no', $container) | Out-Null
                    $logs = Invoke-Docker @('logs', '--tail', '200', $container) 30
                    foreach ($sensitiveValue in $sensitiveValues) {
                        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
                            $logs = $logs.Replace($sensitiveValue, '[REDACTED]')
                        }
                    }
                    [IO.File]::WriteAllText((Join-Path $failureEvidence "$container.log"), $logs)
                    $state = Invoke-Docker @('inspect', $container, '--format', '{{.State.Status}}|{{.State.ExitCode}}|{{.RestartCount}}')
                    [IO.File]::WriteAllText((Join-Path $failureEvidence "$container.state"), $state)
                }
                catch { }
            }
        }
        Write-Output "PRESERVED_FAILURE_EVIDENCE root=$tempRoot image=$image happyContainer=$happyContainer crashContainer=$crashContainer"
    }
}
