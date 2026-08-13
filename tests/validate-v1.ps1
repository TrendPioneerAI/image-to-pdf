param(
    [int]$PerformanceRepeats = 10,
    [string]$ExePath
)
$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent $testRoot
$fixtureRoot = Join-Path $testRoot 'fixtures'
$outRoot = Join-Path $testRoot 'out\v1-validation'
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$exe = if ([String]::IsNullOrWhiteSpace($ExePath)) {
    Get-ChildItem -LiteralPath (Join-Path $appRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
} else {
    Get-Item -LiteralPath $ExePath
}
if (-not $exe) { throw 'Build the application before running validation.' }
$assembly = [Reflection.Assembly]::LoadFrom($exe.FullName)

function Get-AppType([string]$name) {
    return $assembly.GetType(('LocalImageToPdf.' + $name), $true)
}

$snapshotType = Get-AppType 'ImageSnapshot'
$optionsType = Get-AppType 'ExportOptions'
$watermarkType = Get-AppType 'WatermarkOptions'
$exporterType = Get-AppType 'PdfExporter'
$paperType = Get-AppType 'PaperSizeKind'
$orientationType = Get-AppType 'PageOrientation'
$qualityType = Get-AppType 'QualityPreset'
$modeType = Get-AppType 'ExportMode'
$watermarkModeType = Get-AppType 'WatermarkMode'
$watermarkLayoutType = Get-AppType 'WatermarkLayout'
$targetModeType = Get-AppType 'OutputTargetMode'
$listType = [Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($snapshotType)

function New-Watermark([string]$mode, [string]$text = '', [int]$opacity = 18, [int]$angle = 45, [string]$layout = 'Tile') {
    $watermark = [Activator]::CreateInstance($watermarkType)
    $watermarkType.GetProperty('Mode').SetValue($watermark, [Enum]::Parse($watermarkModeType, $mode), $null)
    $watermarkType.GetProperty('Text').SetValue($watermark, $text, $null)
    $watermarkType.GetProperty('OpacityPercent').SetValue($watermark, $opacity, $null)
    $watermarkType.GetProperty('AngleDegrees').SetValue($watermark, $angle, $null)
    $watermarkType.GetProperty('Layout').SetValue($watermark, [Enum]::Parse($watermarkLayoutType, $layout), $null)
    return $watermark
}

function New-Options([string]$quality, $watermark, [string]$orientation = 'Portrait') {
    $options = [Activator]::CreateInstance($optionsType)
    $optionsType.GetProperty('PaperSize').SetValue($options, [Enum]::Parse($paperType, 'A4'), $null)
    $optionsType.GetProperty('Orientation').SetValue($options, [Enum]::Parse($orientationType, $orientation), $null)
    $optionsType.GetProperty('AutoRotate').SetValue($options, $false, $null)
    $optionsType.GetProperty('MarginMm').SetValue($options, 10, $null)
    $optionsType.GetProperty('Quality').SetValue($options, [Enum]::Parse($qualityType, $quality), $null)
    $optionsType.GetProperty('Mode').SetValue($options, [Enum]::Parse($modeType, 'Merge'), $null)
    $optionsType.GetProperty('BaseName').SetValue($options, 'validation', $null)
    $optionsType.GetProperty('Watermark').SetValue($options, $watermark, $null)
    $optionsType.GetProperty('TargetMode').SetValue($options, [Enum]::Parse($targetModeType, 'File'), $null)
    return $options
}

function Export-Merged([string]$path, [System.IO.FileInfo[]]$files, $options, [int]$repeat = 1) {
    $snapshots = [Activator]::CreateInstance($listType)
    for ($round = 0; $round -lt $repeat; $round++) {
        foreach ($file in $files) {
            $snapshot = [Activator]::CreateInstance($snapshotType)
            $snapshotType.GetProperty('Path').SetValue($snapshot, $file.FullName, $null)
            $snapshotType.GetProperty('ManualRotation').SetValue($snapshot, 0, $null)
            $snapshotType.GetProperty('OutputName').SetValue($snapshot, $file.BaseName, $null)
            $listType.GetMethod('Add').Invoke($snapshots, @($snapshot))
        }
    }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    $token = New-Object System.Threading.CancellationToken
    $method = $exporterType.GetMethod('ExportMerged', [Reflection.BindingFlags] 'Public,NonPublic,Static')
    $invokeArgs = New-Object object[] 5
    $invokeArgs.SetValue($path, 0)
    $invokeArgs.SetValue($snapshots, 1)
    $invokeArgs.SetValue($options, 2)
    $invokeArgs.SetValue($null, 3)
    $invokeArgs.SetValue($token, 4)
    $method.Invoke($null, $invokeArgs)
}

function Get-FirstDctBytes([string]$pdfPath) {
    $bytes = [IO.File]::ReadAllBytes($pdfPath)
    $encoding = [Text.Encoding]::GetEncoding(28591)
    $text = $encoding.GetString($bytes)
    $match = [regex]::Match($text, '/Filter /DCTDecode /Length (?<length>\d+) >>\nstream\n')
    if (-not $match.Success) { throw "No DCT stream found in $pdfPath" }
    $length = [int]$match.Groups['length'].Value
    $result = New-Object byte[] $length
    [Array]::Copy($bytes, $match.Index + $match.Length, $result, 0, $length)
    return $result
}

function Get-Sha([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

$jpg = Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.jpg' -File | Sort-Object Name | Select-Object -First 1
$png = Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.png' -File | Sort-Object Name | Select-Object -First 1
if (-not $jpg -or -not $png) { throw 'Run tests\make-fixtures.ps1 first.' }

$none = New-Watermark 'None'
$smartPath = Join-Path $outRoot 'smart-direct.pdf'
Export-Merged $smartPath @($jpg) (New-Options 'SmartFast' $none)
$sourceHash = Get-Sha ([IO.File]::ReadAllBytes($jpg.FullName))
$embeddedHash = Get-Sha (Get-FirstDctBytes $smartPath)
if ($sourceHash -ne $embeddedHash) { throw 'SmartFast JPEG was recompressed instead of direct embedded.' }
$smartText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($smartPath))
if ($smartText.Contains('/Wm ')) { throw 'No-watermark export unexpectedly contains watermark resources.' }

$defaultText = ([char]0x4ec5).ToString() + ([char]0x4f9b) + ([char]0x53c2) + ([char]0x8003)
$defaultWatermark = New-Watermark 'Default' $defaultText 18 45 'Tile'
$watermarkPath = Join-Path $outRoot 'watermark-default.pdf'
Export-Merged $watermarkPath @($jpg) (New-Options 'SmartFast' $defaultWatermark)
$watermarkEmbeddedHash = Get-Sha (Get-FirstDctBytes $watermarkPath)
if ($sourceHash -ne $watermarkEmbeddedHash) { throw 'Adding a watermark recompressed the direct JPEG.' }
$watermarkText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($watermarkPath))
if (-not $watermarkText.Contains('/SMask ') -or -not $watermarkText.Contains('/Wm ')) { throw 'Watermark PDF overlay resources are missing.' }

$customText = ([char]0x4e2d).ToString() + ([char]0x6587) + ' ABC 123 #' + ([char]0x9a8c) + ([char]0x6536)
$customWatermark = New-Watermark 'Custom' $customText 35 -45 'BottomRight'
$customPath = Join-Path $outRoot 'watermark-custom.pdf'
Export-Merged $customPath @($png) (New-Options 'SmartFast' $customWatermark)

$pngFastPath = Join-Path $outRoot 'png-fast.pdf'
Export-Merged $pngFastPath @($png) (New-Options 'SmartFast' $none)
$pngFastText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($pngFastPath))
if (-not $pngFastText.Contains('/DCTDecode')) { throw 'SmartFast PNG was not converted to JPEG.' }

$pngLosslessPath = Join-Path $outRoot 'png-lossless.pdf'
Export-Merged $pngLosslessPath @($png) (New-Options 'Lossless' $none)
$pngLosslessText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($pngLosslessPath))
if (-not $pngLosslessText.Contains('/FlateDecode') -or -not $pngLosslessText.Contains('/Predictor 15')) { throw 'Lossless PNG did not use lossless Flate image data.' }

$standardPath = Join-Path $outRoot 'standard-220.pdf'
Export-Merged $standardPath @($jpg) (New-Options 'Standard' $none)
if ((Get-Sha (Get-FirstDctBytes $standardPath)) -eq $sourceHash) { throw 'Standard quality unexpectedly direct embedded the JPEG.' }

$finePath = Join-Path $outRoot 'fine-300.pdf'
Export-Merged $finePath @($jpg) (New-Options 'FinePrint' $none)
$fineText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($finePath))
if (-not $fineText.Contains('/DCTDecode') -or $fineText.Contains('/Predictor 15')) { throw 'FinePrint is not JPEG-encoded.' }

$orientationFiles = @(Get-ChildItem -LiteralPath (Join-Path $fixtureRoot 'orientation') -Filter 'orientation-*.jpg' -File -ErrorAction SilentlyContinue | Sort-Object Name)
if ($orientationFiles.Count -eq 8) {
    Export-Merged (Join-Path $outRoot 'orientation-smart.pdf') $orientationFiles (New-Options 'SmartFast' $none)
    Export-Merged (Join-Path $outRoot 'orientation-reference.pdf') $orientationFiles (New-Options 'Standard' $none)
}

$performanceFiles = @($jpg, $jpg, $png)
$performancePath = Join-Path $outRoot 'performance-smart.pdf'
$watch = [Diagnostics.Stopwatch]::StartNew()
Export-Merged $performancePath $performanceFiles (New-Options 'SmartFast' $none) $PerformanceRepeats
$watch.Stop()

[pscustomobject]@{
    DirectJpegSha256 = $sourceHash
    DirectDctSha256 = $embeddedHash
    WatermarkedDctSha256 = $watermarkEmbeddedHash
    ValidationPdfs = if ($orientationFiles.Count -eq 8) { 9 } else { 7 }
    PerformancePages = $performanceFiles.Count * $PerformanceRepeats
    PerformanceSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
    PerformanceBytes = (Get-Item -LiteralPath $performancePath).Length
} | Format-List
Write-Host 'v1 validation passed.'
