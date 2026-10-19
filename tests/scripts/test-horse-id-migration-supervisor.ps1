param(
    [string] $Docker = 'docker',
    [string] $Dotnet = 'dotnet',
    [switch] $WrapperSmokeOnly,
    [switch] $ObserverSmokeOnly
)
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
$testProject = Join-Path $repositoryRoot 'tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj'
$fixtureMethod = 'FullyQualifiedName~ExportProductionContainerFixtureWhenRequested'
$oldHorseId = 'horse-11111111-1111-4111-8111-111111111111'
$jraIdentity = 'jra-horse:2024100828'
$maxOutputCharacters = 64 * 1024

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

    internal static int GetParentProcessId()
    {
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

function Invoke-Docker([string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    Invoke-External $dockerCommand $Arguments $TimeoutSeconds
}

function Invoke-Api([string] $BaseUrl, [string] $Path, [string] $Method = 'GET', $Body = $null,
    [string] $ApiKey) {
    $headers = @{ 'X-Api-Key' = $ApiKey }
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri ($BaseUrl + $Path) -Headers $headers -TimeoutSec 15
    }
    $json = $Body | ConvertTo-Json -Depth 10 -Compress
    return Invoke-RestMethod -Method $Method -Uri ($BaseUrl + $Path) -Headers $headers `
        -ContentType 'application/json' -Body $json -TimeoutSec 15
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
read_proc_parent() { awk '/^PPid:/ {print $2}' "$1" 2>/dev/null; }
read_proc_start() { awk '{print $22}' "$1" 2>/dev/null; }
read_proc_state_and_start() { awk '{print $3 " " $22}' "$1" 2>/dev/null; }
write_identity_error() {
  printf '%s\n' "$1" > "$marker.error"
  : > "$marker.done"
}
fixed_cli_candidate_identity() {
  entry="$1"; expected="$2"; proc_root="$3"
  [ -r "$entry" ] || return 1
  pid=${entry#"$proc_root/"}; pid=${pid%/cmdline}
  case "$pid" in ''|*[!0-9]*) return 1 ;; esac
  status="$proc_root/$pid/status"
  [ -r "$status" ] || return 1
  if ! parent=$(read_proc_parent "$status"); then return 1; fi
  [ "$parent" = 1 ] || return 1
  command=$(tr '\000' ' ' < "$entry" 2>/dev/null || true)
  [ "$command" = "$expected " ] || return 1
  if ! start=$(read_proc_start "$proc_root/$pid/stat"); then return 1; fi
  case "$start" in ''|*[!0-9]*) return 1 ;; esac
  printf '%s %s\n' "$pid" "$start"
}
scan_fixed_cli_candidates() {
  expected="$1"; proc_root="$2"; action="$3"; marker="$4"
  for entry in "$proc_root"/[0-9]*/cmdline; do
    if ! candidate=$(fixed_cli_candidate_identity "$entry" "$expected" "$proc_root"); then continue; fi
    pid=${candidate%% *}; start=${candidate#* }
    if [ "$action" = stop ]; then
      kill -STOP "$pid" 2>/dev/null || continue
      if ! stopped_start=$(read_proc_start "$proc_root/$pid/stat"); then
        write_identity_error 'identity-unavailable:stat-after-stop'
        return 4
      fi
      if ! stopped_parent=$(read_proc_parent "$proc_root/$pid/status"); then
        write_identity_error 'identity-unavailable:parent-after-stop'
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
  if ! identity=$(read_proc_state_and_start "$proc_root/$pid/stat"); then echo gone; return 0; fi
  state=${identity%% *}
  start=${identity#* }
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
    foreach ($expected in @(
        'PASS set-e watcher scan continues after deterministic /proc status ENOENT and writes terminal markers',
        'PASS post-stop PID disappearance is reported as identity ambiguity',
        'PASS observer PID/start liveness rejects missing and zombie processes')) {
        if (-not $output.Contains($expected)) { throw "The observer shell counterexample did not report: $expected" }
    }
}

function Start-FixedCliObserver([string] $Container, [string] $OperationId, [ValidateSet('record', 'stop')] [string] $Action) {
    $marker = "/tmp/hrp-horse-id-observer-$OperationId-$Action"
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
  [ "$scan_status" -eq 1 ] || exit "$scan_status"
  sleep 0.02
done
: > "$marker.timeout"
: > "$marker.done"
exit 9
'@
    $watcherScript = $watcherScript.Replace('set -eu', (Get-FixedCliObserverShellHelpers) + "`nset -eu")
    [void](Invoke-Docker @('exec', '-d', $Container, '/bin/sh', '-c', $watcherScript,
        'hrp-fixed-cli-observer', $OperationId, $Action, $marker) 30)
    return [pscustomobject]@{ Marker = $marker; Action = $Action }
}

function Wait-FixedCliObserver([string] $Container, $Observer, [int] $TimeoutSeconds = 1250) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $startedAt = [DateTimeOffset]::UtcNow
    $result = $null
    $statusScript = @'
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
    $statusScript = (Get-FixedCliObserverShellHelpers) + "`n" + $statusScript
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
    $exitScript = (Get-FixedCliObserverShellHelpers) + @'
observer_liveness "$1" "$2" /proc
'@
    while ([DateTimeOffset]::UtcNow -lt $exitDeadline) {
        $watcherState = Invoke-Docker @('exec', $Container, '/bin/sh', '-c', $exitScript,
            'hrp-observer-exit', [string]$watcherPid, $watcherStart) 15
        if ($watcherState.Trim() -eq 'gone') { return $result }
        Start-Sleep -Milliseconds 100
    }
    throw 'The completed fixed CLI observer did not exit within its reap bound.'
}

function Get-ContainerProcessSnapshot([string] $Container) {
    $pid1Command = (Invoke-Docker @('exec', $Container, '/bin/cat', '/proc/1/cmdline')).Replace([char]0, ' ').Trim()
    $taskChildrenText = Invoke-Docker @('exec', $Container, '/bin/sh', '-c', 'for f in /proc/1/task/*/children; do cat "$f"; printf "\n"; done')
    $children = @(Get-UniqueProcessIdsFromTaskChildren @($taskChildrenText -split "`n"))
    $childCommands = @()
    foreach ($childPid in $children) {
        $command = (Invoke-Docker @('exec', $Container, '/bin/cat', "/proc/$childPid/cmdline")).Replace([char]0, ' ').Trim()
        $status = Invoke-Docker @('exec', $Container, '/bin/cat', "/proc/$childPid/status")
        $parentLine = ((@($status -split "`n" | Where-Object { $_.StartsWith('PPid:') }) | Select-Object -First 1).Trim())
        $stat = Invoke-Docker @('exec', $Container, '/bin/cat', "/proc/$childPid/stat")
        if ($stat -notmatch '^\d+ \(.*\) (?<fields>.*)$') { throw "Could not parse start identity for child PID $childPid." }
        $statFields = @($Matches.fields -split '\s+')
        if ($statFields.Count -le 19) { throw "Child PID $childPid had incomplete process identity data." }
        $childCommands += [pscustomobject]@{
            ProcessId = $childPid
            Command = $command
            ParentLine = $parentLine
            State = $statFields[0]
            StartIdentity = $statFields[19]
        }
    }
    return [pscustomobject]@{ Pid1Command = $pid1Command; Children = $childCommands }
}

function Assert-Pid1ApiShape([string] $Container, [string] $ExpectedChildFlag) {
    $snapshot = Get-ContainerProcessSnapshot $Container
    return Assert-Pid1ApiSnapshot $snapshot $ExpectedChildFlag
}

function Assert-Pid1ApiSnapshot($snapshot, [string] $ExpectedChildFlag) {
    if ($snapshot.Pid1Command -notmatch 'HorseRacingPrediction\.Api\.dll supervise') {
        throw "The production image is not running the frozen supervisor as PID 1: $($snapshot.Pid1Command)"
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
    [string] $Container, [string] $PauseTimestampPath, [int] $ApiProcessId) {
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
    if (-not $sawDraining) { throw 'Execute did not enter the persisted Draining phase during its full pause window.' }
}

function Wait-ForStoppedCliAfterBackup([string] $Container, [string] $DataDirectory,
    [string] $OperationId, $ObserverResult, [int] $TimeoutSeconds = 300) {
    $backupDirectory = Join-Path $DataDirectory "backups/horse-id-migration-$($OperationId.Replace('-', ''))"
    $receiptPath = Join-Path $backupDirectory 'receipt.json'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $snapshot = Get-ContainerProcessSnapshot $Container
        if ($snapshot.Children.Count -gt 1) {
            throw 'The supervisor exposed overlapping direct service children during the crash boundary.'
        }
        if ($snapshot.Children.Count -eq 1) {
            $child = $snapshot.Children[0]
            $isExpectedStoppedCli = $child.ProcessId -eq $ObserverResult.ProcessId
                -and $child.StartIdentity -eq $ObserverResult.StartIdentity
                -and $child.State -in @('T', 't')
                -and $child.ParentLine -match '^PPid:\s+1$'
                -and (Test-IsFixedCliCandidate 1 $child.Command.Trim() $OperationId)
            if ($isExpectedStoppedCli -and (Test-Path -LiteralPath $receiptPath)) {
                return $child
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'The owned fixed CLI was not observed stopped under PID 1 after API exit and complete backup.'
}

function Stage-Operation([string] $BaseUrl, [string] $ApiKey) {
    $manifest = @{ entries = @(@{
        oldHorseId = $oldHorseId
        jraIdentity = $jraIdentity
        hypothesisAccepted = $true
    }) }
    $preview = Invoke-Api $BaseUrl '/api/v2/admin/horse-id-migration-previews' 'POST' $manifest $ApiKey
    if (-not $preview.canStage -or -not $preview.fingerprint) {
        throw "The isolated seeded migration fixture was not stageable: $($preview | ConvertTo-Json -Depth 8 -Compress)"
    }
    $staged = Invoke-Api $BaseUrl '/api/v2/admin/horse-id-migration-operations' 'POST' `
        @{ manifest = $manifest; previewFingerprint = $preview.fingerprint } $ApiKey
    return [string]$staged.operationId
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
    [void](New-Item -ItemType Directory -Path $happyData)
    [void](New-Item -ItemType Directory -Path $crashData)
    Test-TaskChildrenParserCounterexample
    Test-FixedCliObserverCounterexamples
    Test-Pid1ApiSnapshotCounterexamples
    Test-PreparationWindowCounterexamples
    Test-ExternalProcessWrapper
    if ($WrapperSmokeOnly) {
        Write-Output 'PASS bounded process wrapper stdout/stderr, EOF, timeout, and owned-tree cleanup smoke'
        return
    }

    Invoke-Docker @('build', '--file', (Join-Path $repositoryRoot 'Dockerfile'), '--tag', $image, $repositoryRoot) 1800 | Out-Null
    Test-FixedCliObserverProcReadCounterexamples $image
    if ($ObserverSmokeOnly) {
        Write-Output 'PASS fixed CLI observer proc-read churn, post-stop ambiguity, and exact PID/start liveness counterexamples'
        return
    }

    $fixtureArguments = @(
        'test', $testProject, '-c', 'Release', '--no-restore', '--filter', $fixtureMethod,
        '--logger', 'console;verbosity=minimal')
    $fixtureOutput = Invoke-External -FilePath $dotnetCommand -Arguments $fixtureArguments -TimeoutSeconds 300 `
        -Environment @{ HRP_HORSE_IDENTITY_SUPERVISOR_FIXTURE_DIR = $happyData }
    if (-not $fixtureOutput.Contains('ExportProductionContainerFixtureWhenRequested')) {
        # VSTest may suppress names for a one-test success; filesystem evidence below is authoritative.
    }
    Copy-Item -LiteralPath (Join-Path $happyData 'eventstore.db') -Destination (Join-Path $crashData 'eventstore.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'collection-platform.db') -Destination (Join-Path $crashData 'collection-platform.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'prediction-executions.db') -Destination (Join-Path $crashData 'prediction-executions.db')
    Copy-Item -LiteralPath (Join-Path $happyData 'pause-start.txt') -Destination (Join-Path $crashData 'pause-start.txt')

    $apiKey = 'local-supervisor-' + [guid]::NewGuid().ToString('N')
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
    $happyUrl = "http://127.0.0.1:$happyPort"
    Wait-Api $happyUrl $apiKey
    $initialChild = Assert-Pid1ApiShape $happyContainer '--internal-api-child'
    $happyOperationId = Stage-Operation $happyUrl $apiKey
    Reset-DisposablePauseWindow $happyUrl $apiKey (Join-Path $happyData 'pause-start.txt')
    [void](Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId/execute" 'POST' @{} $apiKey)
    $happyObserver = Start-FixedCliObserver $happyContainer $happyOperationId 'record'
    Wait-ForPreparationDrain $happyUrl $apiKey $happyOperationId $happyContainer `
        (Join-Path $happyData 'pause-start.txt') $initialChild.ProcessId
    $happyCli = Wait-FixedCliObserver $happyContainer $happyObserver

    $happyDeadline = [DateTimeOffset]::UtcNow.AddMinutes(40)
    $observedChildModes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($happyCli.ProcessId -le 0 -or $happyCli.StartIdentity -notmatch '^\d+$') {
        throw 'The happy-path observer did not record the exact fixed CLI process identity.'
    }
    [void]$observedChildModes.Add('fixed-cli')
    while ([DateTimeOffset]::UtcNow -lt $happyDeadline) {
        $snapshot = Get-ContainerProcessSnapshot $happyContainer
        if ($snapshot.Children.Count -gt 1) { throw 'More than one service child was live under PID 1.' }
        if ($snapshot.Children.Count -eq 1) {
            $command = $snapshot.Children[0].Command
            if ($command -match '--internal-api-child') { [void]$observedChildModes.Add('api-before-or-after') }
            elseif ($command -match 'horse-id-migration-operation') { [void]$observedChildModes.Add('fixed-cli') }
            else { throw "PID 1 spawned an unexpected child command: $command" }
        }
        try {
            $status = Invoke-Api $happyUrl "/api/v2/admin/horse-id-migration-operations/$happyOperationId" -ApiKey $apiKey
        } catch {
            Start-Sleep -Milliseconds 250
            continue
        }
        if ($status.status -eq 'Succeeded' -and $status.executionPhase -eq 'Verified') { break }
        if ($status.status -in @('Partial', 'Failed', 'Unknown')) {
            throw "Migration stopped safely at status '$($status.status)' phase '$($status.executionPhase)'."
        }
        Start-Sleep -Milliseconds 250
    }
    if ($status.status -ne 'Succeeded' -or $status.executionPhase -ne 'Verified') {
        throw "The production-image happy path did not complete as Verified within its bound."
    }
    if (-not $observedChildModes.Contains('fixed-cli')) {
        throw 'The production-image path did not expose the supervised fixed CLI as the sole child of PID 1.'
    }
    $finalChild = Assert-Pid1ApiShape $happyContainer '--internal-api-child'
    if ($finalChild.ProcessId -eq $initialChild.ProcessId) {
        throw 'The original API child was not exited/reaped before the paused API restart.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $happyData "backups/horse-id-migration-$($happyOperationId.Replace('-', ''))"))) {
        throw 'The complete managed backup package is absent after the successful process handoff.'
    }

    Invoke-Docker @('rm', '--force', $happyContainer) | Out-Null

    $crashKey = 'local-supervisor-crash-' + [guid]::NewGuid().ToString('N')
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
    $crashUrl = "http://127.0.0.1:$crashPort"
    Wait-Api $crashUrl $crashKey
    $crashOperationId = Stage-Operation $crashUrl $crashKey
    Reset-DisposablePauseWindow $crashUrl $crashKey (Join-Path $crashData 'pause-start.txt')
    [void](Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId/execute" 'POST' @{} $crashKey)
    $crashInitialChild = Assert-Pid1ApiShape $crashContainer '--internal-api-child'
    $crashObserver = Start-FixedCliObserver $crashContainer $crashOperationId 'stop'
    Wait-ForPreparationDrain $crashUrl $crashKey $crashOperationId $crashContainer `
        (Join-Path $crashData 'pause-start.txt') $crashInitialChild.ProcessId
    $crashCli = Wait-FixedCliObserver $crashContainer $crashObserver
    $stoppedCli = Wait-ForStoppedCliAfterBackup $crashContainer $crashData $crashOperationId $crashCli
    $crashSourceFingerprint = Get-SourceFileFingerprint $crashData
    $beforeKill = Get-ContainerProcessSnapshot $crashContainer
    if (($beforeKill.Children.Count -ne 1) -or
        ($beforeKill.Children[0].ProcessId -ne $stoppedCli.ProcessId) -or
        ($beforeKill.Children[0].StartIdentity -ne $stoppedCli.StartIdentity) -or
        ($beforeKill.Children[0].State -notin @('T', 't'))) {
        throw 'The exact observed fixed CLI was not still stopped immediately before termination.'
    }
    Invoke-Docker @('exec', $crashContainer, '/bin/kill', '-KILL', [string]$crashCli.ProcessId) | Out-Null
    Wait-ForContainerRestart $crashContainer 1
    Wait-Api $crashUrl $crashKey
    $blockedChild = Assert-Pid1ApiShape $crashContainer '--internal-blocked-child'
    $blockedStatus = Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId" -ApiKey $crashKey
    if ($blockedStatus.executionPhase -ne 'Applying' -or $blockedStatus.status -eq 'Succeeded') {
        throw 'After the fixed child crash, restart must remain journal-only at Applying without replay.'
    }
    $gatePaths = @(
        (Join-Path $crashData '.eventstore.db.horse-identity-migration.maintenance'),
        (Join-Path $crashData '.collection-platform.db.horse-identity-migration.maintenance'),
        (Join-Path $crashData '.prediction-executions.db.horse-identity-migration.maintenance'))
    if (@($gatePaths | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -ne 0) {
        throw 'Crash restart cleared or lost a maintenance gate before explicit verification.'
    }
    if ((Get-SourceFileFingerprint $crashData) -cne $crashSourceFingerprint) {
        throw 'The killed fixed child changed a durable source or SQLite WAL/SHM file before blocked restart.'
    }
    Start-Sleep -Seconds 5
    $again = Invoke-Api $crashUrl "/api/v2/admin/horse-id-migration-operations/$crashOperationId" -ApiKey $crashKey
    if ($again.executionPhase -ne 'Applying' -or $again.status -eq 'Succeeded') {
        throw 'The blocked restart attempted to replay or advance an interrupted Applying operation.'
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
    Write-Output 'PASS real production image PID-1/API/fixed-CLI handoff, 900-second pause, backup/postcheck, killed-child blocked restart, and bounded cleanup'
}
finally {
    foreach ($container in @($happyContainer, $crashContainer)) {
        try { Invoke-Docker @('rm', '--force', $container) | Out-Null } catch { }
    }
    try { Invoke-Docker @('image', 'rm', '--force', $image) | Out-Null } catch { }
    Assert-DisposablePath $tempRoot
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
