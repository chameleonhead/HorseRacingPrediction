$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

function Test-PlaywrightVersionAlignment {
    param(
        [Parameter(Mandatory)] [string[]] $ProjectContents,
        [Parameter(Mandatory)] [string] $DockerfileContent
    )

    $packageVersions = @($ProjectContents | ForEach-Object {
        $project = [xml]$_
        @($project.Project.ItemGroup.PackageReference) |
            Where-Object { $_.Include -eq 'Microsoft.Playwright' } |
            ForEach-Object { [string]$_.Version }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

    if ($packageVersions.Count -eq 0) {
        throw 'No production Microsoft.Playwright package reference was found.'
    }
    if ($packageVersions.Count -ne 1) {
        throw "Production Microsoft.Playwright package versions differ: $($packageVersions -join ', ')"
    }

    $imageMatch = [regex]::Match(
        $DockerfileContent,
        '(?m)^FROM\s+mcr\.microsoft\.com/playwright/dotnet:v(?<version>\d+\.\d+\.\d+)-noble\s*$')
    if (-not $imageMatch.Success) {
        throw 'Collector Dockerfile must use an exact Playwright noble image version.'
    }

    $packageVersion = $packageVersions[0]
    $imageVersion = $imageMatch.Groups['version'].Value
    if ($imageVersion -ne $packageVersion) {
        throw "Playwright package $packageVersion requires container image v$packageVersion-noble; found v$imageVersion-noble."
    }
}

$projectContents = @(Get-ChildItem (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.csproj' |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName })
$dockerfileContent = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Dockerfile.collector-lambda')
Test-PlaywrightVersionAlignment -ProjectContents $projectContents -DockerfileContent $dockerfileContent

$fixtureProject163 = '<Project><ItemGroup><PackageReference Include="Microsoft.Playwright" Version="1.63.0" /></ItemGroup></Project>'
$fixtureProject162 = '<Project><ItemGroup><PackageReference Include="Microsoft.Playwright" Version="1.62.0" /></ItemGroup></Project>'
$fixtureDocker163 = 'FROM mcr.microsoft.com/playwright/dotnet:v1.63.0-noble'
$fixtureDocker162 = 'FROM mcr.microsoft.com/playwright/dotnet:v1.62.0-noble'

Test-PlaywrightVersionAlignment -ProjectContents @($fixtureProject163) -DockerfileContent $fixtureDocker163

foreach ($negative in @(
    @{ Name = 'stale-image'; Projects = @($fixtureProject163); Dockerfile = $fixtureDocker162 },
    @{ Name = 'package-divergence'; Projects = @($fixtureProject163, $fixtureProject162); Dockerfile = $fixtureDocker163 },
    @{ Name = 'floating-image'; Projects = @($fixtureProject163); Dockerfile = 'FROM mcr.microsoft.com/playwright/dotnet:noble' }
)) {
    try {
        Test-PlaywrightVersionAlignment -ProjectContents $negative.Projects -DockerfileContent $negative.Dockerfile
        throw "Negative case unexpectedly passed: $($negative.Name)"
    }
    catch {
        if ($_.Exception.Message -like 'Negative case unexpectedly passed:*') { throw }
    }
}

foreach ($workflowPath in @(
    '.github/workflows/app-ci.yml',
    '.github/workflows/app-deploy.yml'
)) {
    $workflow = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $workflowPath)
    if ($workflow -notmatch '(?m)^\s*run: ./tests/scripts/test-playwright-container-version\.ps1\s*$') {
        throw "$workflowPath must run the Playwright container version guard."
    }
}

Write-Output 'PASS Playwright package and collector image versions are aligned.'
