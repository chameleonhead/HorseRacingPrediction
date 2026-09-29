param(
    [string] $TerraformPath = "terraform"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$moduleRoot = Join-Path $repositoryRoot "infra/collector-lambda"
$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("collection-dashboard-shape-" + [guid]::NewGuid().ToString("N"))
$terraform = (Get-Command $TerraformPath -ErrorAction Stop).Source

function Invoke-Terraform {
    param(
        [string[]] $Arguments,
        [string] $InputText = ""
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $terraform
    $startInfo.WorkingDirectory = $scratchRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $providerCache = Join-Path $moduleRoot ".terraform/providers"
    if (Test-Path -LiteralPath $providerCache) {
        $startInfo.Environment["TF_PLUGIN_CACHE_DIR"] = $providerCache
    }
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void] $process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if ($InputText) { $process.StandardInput.WriteLine($InputText) }
    $process.StandardInput.Close()
    $process.WaitForExit()
    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $stdoutTask.GetAwaiter().GetResult()
        Error = $stderrTask.GetAwaiter().GetResult()
    }
}

try {
    [void] (New-Item -ItemType Directory -Path $scratchRoot)
    foreach ($name in @("main.tf", "observability.tf", "outputs.tf", "variables.tf", ".terraform.lock.hcl")) {
        Copy-Item -LiteralPath (Join-Path $moduleRoot $name) -Destination $scratchRoot
    }

    $versions = Get-Content -LiteralPath (Join-Path $moduleRoot "versions.tf") -Raw
    $versions = [regex]::Replace($versions, '(?m)^\s*backend\s+"s3"\s*\{\s*\}\s*$', "")
    Set-Content -LiteralPath (Join-Path $scratchRoot "versions.tf") -Value $versions -NoNewline

    $observability = Get-Content -LiteralPath (Join-Path $moduleRoot "observability.tf") -Raw
    $widgetPattern = '(?s)title\s*=\s*"Acquire and completion throughput by lane and definition".*?metrics\s*=\s*local\.collection_dispatch_dashboard_metrics'
    if ($observability -notmatch $widgetPattern) {
        throw "The target widget must consume the structurally tested metric-row local."
    }
    $targetWidget = [regex]::Match($observability, '(?s)title\s*=\s*"Acquire and completion throughput by lane and definition"(?<body>.*?)(?:\n\s*}\s*,)')
    if (-not $targetWidget.Success -or $targetWidget.Groups["body"].Value -match 'flatten\s*\(') {
        throw "The target widget must not recursively flatten its metric rows."
    }

    $init = Invoke-Terraform -Arguments @("init", "-backend=false", "-input=false", "-lockfile=readonly")
    if ($init.ExitCode -ne 0) { throw "Terraform isolated initialization failed (exit $($init.ExitCode))." }

    $expression = '[length(local.collection_dispatch_dashboard_metrics), length(setproduct(local.collection_dispatch_lanes, local.collection_dispatch_definitions)) * 4, length([for row in local.collection_dispatch_dashboard_metrics : row if can(row[0]) && can(row[1]) && contains(["eligible_ready_rows", "oldest_eligible_age_seconds", "acquire_success_by_lane_definition_total", "terminal_task_completion_by_lane_definition_total"], tostring(row[1])) && length(row) == 6])]'
    $console = Invoke-Terraform -Arguments @("console", "-var=aws_region=us-east-1", "-var=api_base_url=https://example.invalid") -InputText $expression
    if ($console.ExitCode -ne 0 -or $console.Output -notmatch '(?s)^\s*\[\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,?\s*\]\s*$') {
        throw "Terraform could not evaluate the dispatch metric-row structure (exit $($console.ExitCode), sanitized-count-result=$($console.Output.Trim()))."
    }
    $counts = [regex]::Match($console.Output, '(?s)^\s*\[\s*(?<actual>\d+)\s*,\s*(?<expected>\d+)\s*,\s*(?<valid>\d+)\s*,?\s*\]\s*$')
    if (-not $counts.Success -or $counts.Groups["actual"].Value -ne $counts.Groups["expected"].Value -or $counts.Groups["actual"].Value -ne $counts.Groups["valid"].Value) {
        throw "Terraform found a metric-row cardinality or array-shape mismatch (actual=$($counts.Groups['actual'].Value), expected=$($counts.Groups['expected'].Value), valid=$($counts.Groups['valid'].Value))."
    }

    Write-Output "PASS: rendered dispatch dashboard metrics are metric-row arrays with the expected cardinality."
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force
    }
}
