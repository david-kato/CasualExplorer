param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Confirm-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        $failures.Add($Message)
    }
}

$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$sampleRoot = Join-Path $projectRootPath 'samples\TestData'
$projectPath = Join-Path $projectRootPath 'src\CasualExplorer.App\CasualExplorer.App.csproj'
$catalogPath = Join-Path $projectRootPath 'src\CasualExplorer.App\Services\ReadOnlyFileCatalog.cs'
$sourceRoot = Join-Path $projectRootPath 'src\CasualExplorer.App'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$sampleContent = @($project.Project.ItemGroup.Content) | Where-Object {
    $_.Include -eq '..\..\samples\TestData\**\*'
}

Confirm-True ($sampleContent.Count -eq 1) 'The project must include only the fixed samples/TestData content rule.'

if ($sampleContent.Count -eq 1) {
    Confirm-True ($sampleContent.Link -eq 'Samples\TestData\%(RecursiveDir)%(Filename)%(Extension)') 'The bundled sample output path is not fixed.'
    Confirm-True ($sampleContent.CopyToOutputDirectory -eq 'PreserveNewest') 'The sample files are not copied to the build output.'
    Confirm-True ($sampleContent.CopyToPublishDirectory -eq 'PreserveNewest') 'The sample files are not copied to the publish output.'
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8
Confirm-True ($catalog.Contains('Path.GetFullPath(AppContext.BaseDirectory)')) 'The catalog does not anchor the sample path to the application directory.'
Confirm-True ($catalog.Contains('Path.Combine(baseDirectory, "Samples", "TestData")')) 'The catalog does not use the fixed bundled sample path.'
Confirm-True ($catalog.Contains('Path.GetRelativePath(baseDirectory, sampleFolder)')) 'The catalog does not verify that the sample path stays inside the application directory.'
Confirm-True ($catalog.Contains('SearchOption.TopDirectoryOnly')) 'The catalog must not recurse outside the fixed sample folder.'

$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Include *.cs,*.xaml)
$sourceText = ($sourceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join [Environment]::NewLine
$dangerPatterns = @(
    'File.Move(',
    'File.Copy(',
    'File.Delete(',
    'File.WriteAll',
    'Directory.Move(',
    'Directory.Delete(',
    'Directory.CreateDirectory(',
    'FileStream('
)

foreach ($pattern in $dangerPatterns) {
    Confirm-True (-not $sourceText.Contains($pattern)) "A write-capable API was found: $pattern"
}

if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $outputRootPath = (Resolve-Path -LiteralPath $OutputRoot).Path
    $outputSampleRoot = Join-Path $outputRootPath 'Samples\TestData'

    Confirm-True (Test-Path -LiteralPath $outputSampleRoot -PathType Container) 'The bundled sample folder is missing from the build output.'

    if (Test-Path -LiteralPath $outputSampleRoot -PathType Container) {
        $sourceFilesByName = @{}
        foreach ($file in Get-ChildItem -LiteralPath $sampleRoot -File -Recurse) {
            $relative = $file.FullName.Substring($sampleRoot.Length).TrimStart('\')
            $sourceFilesByName[$relative] = $file
        }

        $outputFilesByName = @{}
        foreach ($file in Get-ChildItem -LiteralPath $outputSampleRoot -File -Recurse) {
            $relative = $file.FullName.Substring($outputSampleRoot.Length).TrimStart('\')
            $outputFilesByName[$relative] = $file
        }

        Confirm-True ($sourceFilesByName.Count -eq 35) 'The source dummy data count must remain 35.'
        Confirm-True ($outputFilesByName.Count -eq $sourceFilesByName.Count) 'The build output dummy data count does not match the source.'

        foreach ($relative in $sourceFilesByName.Keys) {
            Confirm-True ($outputFilesByName.ContainsKey($relative)) "A bundled sample is missing: $relative"
            if ($outputFilesByName.ContainsKey($relative)) {
                $sourceHash = (Get-FileHash -LiteralPath $sourceFilesByName[$relative].FullName -Algorithm SHA256).Hash
                $outputHash = (Get-FileHash -LiteralPath $outputFilesByName[$relative].FullName -Algorithm SHA256).Hash
                Confirm-True ($sourceHash -eq $outputHash) "A bundled sample differs from its source: $relative"
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'CasualExplorer step 4 safety boundary: FAILED'
    $failures | ForEach-Object { Write-Host "- $_" }
    exit 1
}

Write-Host 'CasualExplorer step 4 safety boundary: PASSED'
Write-Host '- Fixed bundled path: Samples\TestData'
Write-Host '- Source dummy files: 35'
Write-Host '- Write-capable APIs: not found'

if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
    Write-Host '- Build output samples: copied and hash-matched'
}
