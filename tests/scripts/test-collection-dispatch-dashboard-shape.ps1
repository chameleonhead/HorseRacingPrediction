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

    $canonicalNamespace = "HorseRacingPrediction/CollectionDispatch"
    $canonicalLanes = @("Realtime", "Normal", "Background")
    $canonicalDefinitions = @("race-discovery", "race-detail", "race-odds", "OTHER")
    $canonicalMetricNames = @(
        "eligible_ready_rows",
        "oldest_eligible_age_seconds",
        "acquire_success_by_lane_definition_total",
        "terminal_task_completion_by_lane_definition_total"
    )
    if ($namespace.Groups["expr"].Value -cne '"' + $canonicalNamespace + '"' -or
        $lanes.Groups["expr"].Value -cne '["Realtime", "Normal", "Background"]' -or
        $labelDefault.Groups["expr"].Value -cne '["race-discovery", "race-detail", "race-odds"]') {
        throw "The source namespace, lane, or registered-definition defaults differ from the frozen structural contract."
    }

    $expectedRows = [System.Collections.Generic.List[object]]::new()
    foreach ($lane in $canonicalLanes) {
        foreach ($definition in $canonicalDefinitions) {
            foreach ($metricName in $canonicalMetricNames) {
                $expectedRows.Add(@($canonicalNamespace, $metricName, "Lane", $lane, "Definition", $definition))
            }
        }
    }
    $expectedRowsJson = ConvertTo-Json -InputObject @($expectedRows) -Depth 5 -Compress

$minimalConfig = @"
variable "collection_dispatch_definition_labels" {
  type    = list(string)
  default = ["race-discovery", "race-detail", "race-odds"]
}

locals {
  collection_dispatch_namespace         = "$canonicalNamespace"
  collection_dispatch_lanes             = $([Convert]::ToString((ConvertTo-Json -InputObject @($canonicalLanes) -Compress)))
  collection_dispatch_definitions       = $([Convert]::ToString((ConvertTo-Json -InputObject @($canonicalDefinitions) -Compress)))
  collection_dispatch_definition_labels = ["race-discovery", "race-detail", "race-odds"]
  collection_dispatch_dashboard_metrics = $($metrics.Groups["expr"].Value)
  expected_metric_rows                 = $expectedRowsJson
  wrong_dimension_expected_rows = concat(
    [concat(slice(local.expected_metric_rows[0], 0, 2), ["WrongDimension"], slice(local.expected_metric_rows[0], 3, 6))],
    slice(local.expected_metric_rows, 1, length(local.expected_metric_rows))
  )
  duplicate_omitted_actual_rows = concat(
    slice(local.collection_dispatch_dashboard_metrics, 0, 47),
    [local.collection_dispatch_dashboard_metrics[0]]
  )
}
"@
    Set-Content -LiteralPath (Join-Path $scratchRoot "main.tf") -Value $minimalConfig -NoNewline

    $shell = (Get-Command pwsh -ErrorAction Stop).Source
    $stalledChild = Invoke-ChildProcess -FilePath $shell -Arguments @("-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15") -WorkingDirectory $scratchRoot -TimeoutMilliseconds 1000
    if (-not $stalledChild.TimedOut -or $stalledChild.ExitCode -eq 0) {
        throw "The process timeout counterexample did not terminate as a nonzero timed-out child."
    }

    $expression = '[length(local.collection_dispatch_dashboard_metrics), length(local.expected_metric_rows), length(toset([for row in local.collection_dispatch_dashboard_metrics : jsonencode(row)])), length(toset([for row in local.expected_metric_rows : jsonencode(row)])), length(setintersection(toset([for row in local.collection_dispatch_dashboard_metrics : jsonencode(row)]), toset([for row in local.expected_metric_rows : jsonencode(row)])))]'
    $console = Invoke-ChildProcess -FilePath $terraform -Arguments @("console") -WorkingDirectory $scratchRoot -InputText $expression
    if ($console.TimedOut) {
        throw "Provider-free Terraform console timed out after $([int]($processTimeoutMilliseconds / 1000)) seconds."
    }
    if ($console.ExitCode -ne 0 -or $console.Output -notmatch '(?s)^\s*\[\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,?\s*\]\s*$') {
        throw "Provider-free Terraform console could not evaluate the exact dispatch metric-row set (exit $($console.ExitCode))."
    }
    $counts = [regex]::Match($console.Output, '(?s)^\s*\[\s*(?<actual>\d+)\s*,\s*(?<expected>\d+)\s*,\s*(?<uniqueActual>\d+)\s*,\s*(?<uniqueExpected>\d+)\s*,\s*(?<intersection>\d+)\s*,?\s*\]\s*$')
    if (-not $counts.Success -or $counts.Groups["actual"].Value -ne "48" -or
        $counts.Groups["expected"].Value -ne "48" -or $counts.Groups["uniqueActual"].Value -ne "48" -or
        $counts.Groups["uniqueExpected"].Value -ne "48" -or $counts.Groups["intersection"].Value -ne "48") {
        throw "Terraform found an exact metric-row set mismatch (actual=$($counts.Groups['actual'].Value), expected=$($counts.Groups['expected'].Value), unique-actual=$($counts.Groups['uniqueActual'].Value), unique-expected=$($counts.Groups['uniqueExpected'].Value), intersection=$($counts.Groups['intersection'].Value))."
    }

    $wrongDimensionExpression = $expression -replace 'local\.expected_metric_rows', 'local.wrong_dimension_expected_rows'
    $wrongDimension = Invoke-ChildProcess -FilePath $terraform -Arguments @("console") -WorkingDirectory $scratchRoot -InputText $wrongDimensionExpression
    if ($wrongDimension.TimedOut -or $wrongDimension.ExitCode -ne 0 -or $wrongDimension.Output -notmatch '(?s)^\s*\[\s*48\s*,\s*48\s*,\s*48\s*,\s*48\s*,\s*47\s*,?\s*\]\s*$') {
        throw "The mutated dimension-name counterexample did not fail exact-set equality as expected."
    }

    $duplicateOmitExpression = $expression -replace 'local\.collection_dispatch_dashboard_metrics', 'local.duplicate_omitted_actual_rows'
    $duplicateOmit = Invoke-ChildProcess -FilePath $terraform -Arguments @("console") -WorkingDirectory $scratchRoot -InputText $duplicateOmitExpression
    if ($duplicateOmit.TimedOut -or $duplicateOmit.ExitCode -ne 0 -or $duplicateOmit.Output -notmatch '(?s)^\s*\[\s*48\s*,\s*48\s*,\s*47\s*,\s*48\s*,\s*47\s*,?\s*\]\s*$') {
        throw "The duplicate/omitted-row counterexample did not fail uniqueness and exact-set equality as expected."
    }

    Write-Output "PASS: provider-free exact metric-row set matches 48 canonical arrays; dimension and duplicate/omission counterexamples rejected; stalled-child timeout counterexample passed."
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force
    }
}
