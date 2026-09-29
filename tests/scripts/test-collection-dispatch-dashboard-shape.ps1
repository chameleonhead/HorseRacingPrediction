param(
    [string] $TerraformPath = "terraform"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$moduleRoot = Join-Path $repositoryRoot "infra/collector-lambda"
$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("collection-dashboard-shape-" + [guid]::NewGuid().ToString("N"))
$terraform = (Get-Command $TerraformPath -ErrorAction Stop).Source
$processTimeoutMilliseconds = 120000

function Invoke-ChildProcess {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [string] $InputText = "",
        [int] $TimeoutMilliseconds = $processTimeoutMilliseconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void] $process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if ($InputText) { $process.StandardInput.WriteLine($InputText) }
    $process.StandardInput.Close()

    if (-not $process.WaitForExit($TimeoutMilliseconds)) {
        try {
            $process.Kill($true)
        }
        catch {
            if (-not $process.HasExited) {
                throw "Timed-out child process tree could not be terminated."
            }
        }
        if (-not $process.WaitForExit(5000)) {
            throw "Timed-out child process did not exit after process-tree termination."
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            TimedOut = $true
            Output = ""
            Error = ""
        }
    }

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        TimedOut = $false
        Output = $stdoutTask.GetAwaiter().GetResult()
        Error = $stderrTask.GetAwaiter().GetResult()
    }
}

function Get-RequiredSourceMatch {
    param(
        [string] $Source,
        [string] $Pattern,
        [string] $Description
    )

    $matches = [regex]::Matches($Source, $Pattern)
    if ($matches.Count -ne 1) {
        throw "Could not uniquely extract $Description from the Terraform source."
    }
    $matches[0]
}

try {
    [void] (New-Item -ItemType Directory -Path $scratchRoot)
    $observability = Get-Content -LiteralPath (Join-Path $moduleRoot "observability.tf") -Raw
    $namespace = Get-RequiredSourceMatch -Source $observability -Pattern '(?m)^\s*collection_dispatch_namespace\s*=\s*(?<expr>"[^"\r\n]+")\s*$' -Description "collection dispatch namespace local"
    $lanes = Get-RequiredSourceMatch -Source $observability -Pattern '(?m)^\s*collection_dispatch_lanes\s*=\s*(?<expr>\[[^\r\n]+\])\s*$' -Description "collection dispatch lanes local"
    $definitions = Get-RequiredSourceMatch -Source $observability -Pattern '(?m)^\s*collection_dispatch_definitions\s*=\s*(?<expr>concat\(var\.collection_dispatch_definition_labels,\s*\["OTHER"\]\))\s*$' -Description "collection dispatch definitions local"
    $metrics = Get-RequiredSourceMatch -Source $observability -Pattern '(?ms)^\s*collection_dispatch_dashboard_metrics\s*=\s*(?<expr>concat\(\[\s*for pair in setproduct\(local\.collection_dispatch_lanes,\s*local\.collection_dispatch_definitions\).*?\]\s*\.\.\.\))\s*$' -Description "collection dispatch dashboard metrics local"
    $labelVariable = Get-RequiredSourceMatch -Source $observability -Pattern '(?ms)variable\s+"collection_dispatch_definition_labels"\s*\{(?<body>.*?)^\}' -Description "collection dispatch label variable block"
    $labelDefault = Get-RequiredSourceMatch -Source $labelVariable.Groups["body"].Value -Pattern '(?m)^\s*default\s*=\s*(?<expr>\[[^\]]*\])\s*$' -Description "collection dispatch label default"

    $targetWidget = [regex]::Match($observability, '(?s)title\s*=\s*"Acquire and completion throughput by lane and definition"(?<body>.*?)(?:\n\s*}\s*,)')
    if (-not $targetWidget.Success -or
        $targetWidget.Groups["body"].Value -notmatch 'metrics\s*=\s*local\.collection_dispatch_dashboard_metrics\b' -or
        $targetWidget.Groups["body"].Value -match 'flatten\s*\(') {
        throw "The throughput widget must consume the structurally tested metric-row local without recursive flattening."
    }

    $minimalConfig = @"
variable "collection_dispatch_definition_labels" {
  type    = list(string)
  default = $($labelDefault.Groups["expr"].Value)
}

locals {
  collection_dispatch_namespace         = $($namespace.Groups["expr"].Value)
  collection_dispatch_lanes             = $($lanes.Groups["expr"].Value)
  collection_dispatch_definitions       = $($definitions.Groups["expr"].Value)
  collection_dispatch_dashboard_metrics = $($metrics.Groups["expr"].Value)
}
"@
    Set-Content -LiteralPath (Join-Path $scratchRoot "main.tf") -Value $minimalConfig -NoNewline

    $shell = (Get-Command pwsh -ErrorAction Stop).Source
    $stalledChild = Invoke-ChildProcess -FilePath $shell -Arguments @("-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15") -WorkingDirectory $scratchRoot -TimeoutMilliseconds 1000
    if (-not $stalledChild.TimedOut -or $stalledChild.ExitCode -eq 0) {
        throw "The process timeout counterexample did not terminate as a nonzero timed-out child."
    }

    $expression = '[length(local.collection_dispatch_dashboard_metrics), length(setproduct(local.collection_dispatch_lanes, local.collection_dispatch_definitions)) * 4, length([for row in local.collection_dispatch_dashboard_metrics : row if can(row[0]) && can(row[1]) && contains(["eligible_ready_rows", "oldest_eligible_age_seconds", "acquire_success_by_lane_definition_total", "terminal_task_completion_by_lane_definition_total"], tostring(row[1])) && length(row) == 6])]'
    $console = Invoke-ChildProcess -FilePath $terraform -Arguments @("console") -WorkingDirectory $scratchRoot -InputText $expression
    if ($console.TimedOut) {
        throw "Provider-free Terraform console timed out after $([int]($processTimeoutMilliseconds / 1000)) seconds."
    }
    if ($console.ExitCode -ne 0 -or $console.Output -notmatch '(?s)^\s*\[\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,?\s*\]\s*$') {
        throw "Provider-free Terraform console could not evaluate the dispatch metric-row structure (exit $($console.ExitCode))."
    }
    $counts = [regex]::Match($console.Output, '(?s)^\s*\[\s*(?<actual>\d+)\s*,\s*(?<expected>\d+)\s*,\s*(?<valid>\d+)\s*,?\s*\]\s*$')
    if (-not $counts.Success -or $counts.Groups["actual"].Value -ne $counts.Groups["expected"].Value -or $counts.Groups["actual"].Value -ne $counts.Groups["valid"].Value) {
        throw "Terraform found a metric-row cardinality or array-shape mismatch (actual=$($counts.Groups['actual'].Value), expected=$($counts.Groups['expected'].Value), valid=$($counts.Groups['valid'].Value))."
    }

    Write-Output "PASS: provider-free metric-row structure is valid (actual/expected/valid=$($counts.Groups['actual'].Value)/$($counts.Groups['expected'].Value)/$($counts.Groups['valid'].Value)); stalled-child timeout counterexample passed."
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force
    }
}
