param(
    [string]$ExePath,
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent $testRoot
$resultRoot = Join-Path $testRoot 'results'
$outputRoot = Join-Path $testRoot 'out\performance-v1'
New-Item -ItemType Directory -Force -Path $resultRoot, $outputRoot | Out-Null

$exe = if ([String]::IsNullOrWhiteSpace($ExePath)) {
    Get-ChildItem -LiteralPath (Join-Path $appRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
} else {
    Get-Item -LiteralPath $ExePath
}
if (-not $exe) { throw 'Build the application before running the benchmark.' }

$guidePng = Get-ChildItem -LiteralPath (Join-Path $appRoot 'dist') -Filter '*.png' -File -Recurse | Sort-Object FullName | Select-Object -First 1
if (-not $guidePng) { throw 'The 2480x3508 guide PNG was not found.' }

$benchmarkJpeg = Join-Path $testRoot 'fixtures\benchmark-a4-2480x3508.jpg'
if (-not (Test-Path -LiteralPath $benchmarkJpeg)) {
    Add-Type -AssemblyName System.Drawing
    $source = [Drawing.Image]::FromFile($guidePng.FullName)
    try {
        $encoder = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
        $parameters = New-Object Drawing.Imaging.EncoderParameters(1)
        $parameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality, [long]92)
        try { $source.Save($benchmarkJpeg, $encoder, $parameters) }
        finally { $parameters.Dispose() }
    }
    finally { $source.Dispose() }
}

$assembly = [Reflection.Assembly]::LoadFrom($exe.FullName)
function Get-AppType([string]$name) { return $assembly.GetType(('LocalImageToPdf.' + $name), $true) }

$snapshotType = Get-AppType 'ImageSnapshot'
$optionsType = Get-AppType 'ExportOptions'
$watermarkType = Get-AppType 'WatermarkOptions'
$paperType = Get-AppType 'PaperSizeKind'
$orientationType = Get-AppType 'PageOrientation'
$qualityType = Get-AppType 'QualityPreset'
$modeType = Get-AppType 'ExportMode'
$watermarkModeType = Get-AppType 'WatermarkMode'
$targetModeType = Get-AppType 'OutputTargetMode'
$exporterType = Get-AppType 'PdfExporter'
$legacyExporterType = Get-AppType 'LegacyPdfExporter'
$listType = [Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($snapshotType)

function New-Options {
    $watermark = [Activator]::CreateInstance($watermarkType)
    $null = $watermarkType.GetProperty('Mode').SetValue($watermark, [Enum]::Parse($watermarkModeType, 'None'), $null)
    $options = [Activator]::CreateInstance($optionsType)
    $null = $optionsType.GetProperty('PaperSize').SetValue($options, [Enum]::Parse($paperType, 'A4'), $null)
    $null = $optionsType.GetProperty('Orientation').SetValue($options, [Enum]::Parse($orientationType, 'Portrait'), $null)
    $null = $optionsType.GetProperty('AutoRotate').SetValue($options, $true, $null)
    $null = $optionsType.GetProperty('MarginMm').SetValue($options, 10, $null)
    $null = $optionsType.GetProperty('Quality').SetValue($options, [Enum]::Parse($qualityType, 'SmartFast'), $null)
    $null = $optionsType.GetProperty('Mode').SetValue($options, [Enum]::Parse($modeType, 'Merge'), $null)
    $null = $optionsType.GetProperty('BaseName').SetValue($options, 'performance-v1', $null)
    $null = $optionsType.GetProperty('Watermark').SetValue($options, $watermark, $null)
    $null = $optionsType.GetProperty('TargetMode').SetValue($options, [Enum]::Parse($targetModeType, 'File'), $null)
    return $options
}

function New-Snapshots([string[]]$paths) {
    $snapshots = [Activator]::CreateInstance($listType)
    $index = 0
    foreach ($path in $paths) {
        $snapshot = [Activator]::CreateInstance($snapshotType)
        $null = $snapshotType.GetProperty('Path').SetValue($snapshot, $path, $null)
        $null = $snapshotType.GetProperty('ManualRotation').SetValue($snapshot, 0, $null)
        $null = $snapshotType.GetProperty('OutputName').SetValue($snapshot, ('page-{0:D3}' -f (++$index)), $null)
        $listType.GetMethod('Add').Invoke($snapshots, @($snapshot)) | Out-Null
    }
    return ,$snapshots
}

function Invoke-Export($type, [string]$path, $snapshots) {
    $method = $type.GetMethod('ExportMerged', [Reflection.BindingFlags]'Public,NonPublic,Static')
    $arguments = New-Object object[] 5
    $arguments.SetValue($path, 0)
    $arguments.SetValue($snapshots, 1)
    $arguments.SetValue((New-Options), 2)
    $arguments.SetValue($null, 3)
    $arguments.SetValue([Threading.CancellationToken]::None, 4)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $null = $method.Invoke($null, $arguments)
    $null = $watch.Stop()
    return [double]$watch.Elapsed.TotalSeconds
}

function Get-Median([double[]]$values) {
    $ordered = @($values | Sort-Object)
    if (($ordered.Count % 2) -eq 1) { return $ordered[[int]($ordered.Count / 2)] }
    return ($ordered[$ordered.Count / 2 - 1] + $ordered[$ordered.Count / 2]) / 2.0
}

function Remove-TestOutput([string]$path) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

$jpegPath = (Get-Item -LiteralPath $benchmarkJpeg).FullName
$pngPath = $guidePng.FullName
$threePagePaths = @($jpegPath, $jpegPath, $pngPath)
$threePages = New-Snapshots $threePagePaths
$threeNewTimes = @()
$threeLegacyTimes = @()

# Warm both implementations before taking three medians.
$warmNew = Join-Path $outputRoot 'warm-new.pdf'
$warmLegacy = Join-Path $outputRoot 'warm-legacy.pdf'
Remove-TestOutput $warmNew
Remove-TestOutput $warmLegacy
Invoke-Export $exporterType $warmNew $threePages | Out-Null
Invoke-Export $legacyExporterType $warmLegacy $threePages | Out-Null
for ($run = 1; $run -le $Runs; $run++) {
    $threeNewPath = Join-Path $outputRoot ('three-new-{0}.pdf' -f $run)
    $threeLegacyPath = Join-Path $outputRoot ('three-legacy-{0}.pdf' -f $run)
    Remove-TestOutput $threeNewPath
    Remove-TestOutput $threeLegacyPath
    $threeNewTimes += [double](Invoke-Export $exporterType $threeNewPath $threePages)
    $threeLegacyTimes += [double](Invoke-Export $legacyExporterType $threeLegacyPath $threePages)
}

$paths300 = New-Object System.Collections.Generic.List[string]
for ($index = 0; $index -lt 300; $index++) {
    # 240 JPEG + 60 PNG, interleaved to exercise bounded preparation and ordered writing.
    $paths300.Add($(if ((($index + 1) % 5) -eq 0) { $pngPath } else { $jpegPath }))
}
$pages300 = New-Snapshots $paths300.ToArray()
$warm300 = Join-Path $outputRoot 'warm-300.pdf'
Remove-TestOutput $warm300
Invoke-Export $exporterType $warm300 (New-Snapshots $paths300.GetRange(0, 5).ToArray()) | Out-Null
$times300 = @()
$output300 = $null
for ($run = 1; $run -le $Runs; $run++) {
    $output300 = Join-Path $outputRoot ('smart-300-pages-{0}.pdf' -f $run)
    Remove-TestOutput $output300
    $times300 += [double](Invoke-Export $exporterType $output300 $pages300)
}

$process = [Diagnostics.Process]::GetCurrentProcess()
$outputFile = Get-Item -LiteralPath $output300
$hash = (Get-FileHash -LiteralPath $output300 -Algorithm SHA256).Hash.ToLowerInvariant()
$medianNew = Get-Median $threeNewTimes
$medianLegacy = Get-Median $threeLegacyTimes
$result = [ordered]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $exe.FullName
    dataset = [ordered]@{
        pages = 300
        jpegPages = 240
        pngPages = 60
        sourceDimensions = '2480x3508'
        paper = 'A4 portrait'
        quality = 'SmartFast'
    }
    threePageMedianSeconds = [Math]::Round($medianNew, 3)
    legacyThreePageMedianSeconds = [Math]::Round($medianLegacy, 3)
    measuredSpeedup = [Math]::Round(($medianLegacy / [Math]::Max($medianNew, 0.001)), 2)
    runSeconds300 = @($times300 | ForEach-Object { [Math]::Round($_, 3) })
    medianSeconds300 = [Math]::Round((Get-Median $times300), 3)
    peakWorkingSetMB = [Math]::Round($process.PeakWorkingSet64 / 1MB, 1)
    outputBytes = $outputFile.Length
    outputSha256 = $hash
}

$resultPath = Join-Path $resultRoot 'performance-v1.json'
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
$result | ConvertTo-Json -Depth 5
Write-Host ('Saved: ' + $resultPath)
