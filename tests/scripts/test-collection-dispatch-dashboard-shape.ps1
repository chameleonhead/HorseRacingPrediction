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
        [int] $TimeoutMilliseconds = $processTimeoutMilliseconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $false
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void] $process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
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

    $testConfigTemplate = @"
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
  tested_metric_rows                   = __TESTED_ROWS_EXPRESSION__
}
"@

    $testFile = @'
run "dashboard_metric_row_contract" {
  command = plan

  assert {
    condition     = length(local.tested_metric_rows) == 48
    error_message = "METRIC_ROW_COUNT_ASSERTION_FAILED"
  }

  assert {
    condition = (
      length(toset([for row in local.tested_metric_rows : jsonencode(row)])) == 48
      && length(toset([for row in local.expected_metric_rows : jsonencode(row)])) == 48
      && length(setintersection(
        toset([for row in local.tested_metric_rows : jsonencode(row)]),
        toset([for row in local.expected_metric_rows : jsonencode(row)])
      )) == 48
    )
    error_message = "METRIC_ROW_EXACT_SET_ASSERTION_FAILED"
  }
}
'@

    function Invoke-ContractTestCase {
        param(
            [string] $Name,
            [string] $TestedRowsExpression,
            [bool] $ExpectSuccess
        )

        $caseDirectory = Join-Path $scratchRoot $Name
        [void] (New-Item -ItemType Directory -Path $caseDirectory)
        $configuration = $testConfigTemplate.Replace("__TESTED_ROWS_EXPRESSION__", $TestedRowsExpression)
        Set-Content -LiteralPath (Join-Path $caseDirectory "main.tf") -Value $configuration -NoNewline
        Set-Content -LiteralPath (Join-Path $caseDirectory "dashboard_metric_rows.tftest.hcl") -Value $testFile -NoNewline

        $result = Invoke-ChildProcess -FilePath $terraform -Arguments @("test", "-no-color") -WorkingDirectory $caseDirectory
        if ($result.TimedOut) {
            throw "Provider-free Terraform test case '$Name' timed out after $([int]($processTimeoutMilliseconds / 1000)) seconds."
        }
        if ($ExpectSuccess) {
            if ($result.ExitCode -ne 0) {
                throw "Provider-free Terraform positive contract test failed (exit $($result.ExitCode)): $($result.Error)"
            }
        }
        elseif ($result.ExitCode -eq 0 -or ($result.Output + $result.Error) -notmatch 'METRIC_ROW_EXACT_SET_ASSERTION_FAILED') {
            throw "Provider-free Terraform negative contract test '$Name' did not fail at the expected exact-set assertion."
        }
    }

    $shell = (Get-Command pwsh -ErrorAction Stop).Source
    $stalledChild = Invoke-ChildProcess -FilePath $shell -Arguments @("-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15") -WorkingDirectory $scratchRoot -TimeoutMilliseconds 1000
    if (-not $stalledChild.TimedOut -or $stalledChild.ExitCode -eq 0) {
        throw "The process timeout counterexample did not terminate as a nonzero timed-out child."
    }

    Invoke-ContractTestCase -Name "positive" -TestedRowsExpression 'local.collection_dispatch_dashboard_metrics' -ExpectSuccess $true
    Invoke-ContractTestCase -Name "wrong-dimension" -TestedRowsExpression @'
concat(
  [concat(
    slice(local.collection_dispatch_dashboard_metrics[0], 0, 2),
    ["WrongDimension"],
    slice(local.collection_dispatch_dashboard_metrics[0], 3, 6)
  )],
  slice(local.collection_dispatch_dashboard_metrics, 1, length(local.collection_dispatch_dashboard_metrics))
)
'@ -ExpectSuccess $false
    Invoke-ContractTestCase -Name "duplicate-omission" -TestedRowsExpression @'
concat(
  slice(local.collection_dispatch_dashboard_metrics, 0, 47),
  [local.collection_dispatch_dashboard_metrics[0]]
)
'@ -ExpectSuccess $false

    Write-Output "PASS: provider-free Terraform tests accept the canonical 48 metric arrays, reject mutated-dimension and duplicate/omission counterexamples, and the stalled-child timeout counterexample passed."
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force
    }
}
