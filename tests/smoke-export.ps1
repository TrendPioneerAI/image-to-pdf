param(
    [switch]$Separate,
    [switch]$Landscape,
    [int]$Repeat = 1
)
$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent $testRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $appRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
if (-not $exe) { throw 'Build the application before running this smoke test.' }
$assembly = [Reflection.Assembly]::LoadFrom($exe.FullName)
$snapshotType = $assembly.GetType('LocalImageToPdf.ImageSnapshot', $true)
$optionsType = $assembly.GetType('LocalImageToPdf.ExportOptions', $true)
$exporterType = $assembly.GetType('LocalImageToPdf.PdfExporter', $true)
$orientationType = $assembly.GetType('LocalImageToPdf.PageOrientation', $true)
$qualityType = $assembly.GetType('LocalImageToPdf.QualityPreset', $true)
$modeType = $assembly.GetType('LocalImageToPdf.ExportMode', $true)
$listType = [Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($snapshotType)
$snapshots = [Activator]::CreateInstance($listType)
$fixtureDir = Join-Path $testRoot 'fixtures'
foreach ($file in (Get-ChildItem -LiteralPath $fixtureDir -File | Where-Object { $_.Extension -in '.jpg','.jpeg','.png','.bmp' } | Sort-Object Name)) {
    $snapshot = [Activator]::CreateInstance($snapshotType)
    $snapshotType.GetProperty('Path').SetValue($snapshot, $file.FullName, $null)
    $snapshotType.GetProperty('ManualRotation').SetValue($snapshot, 0, $null)
    $listType.GetMethod('Add').Invoke($snapshots, @($snapshot))
}
if ($Repeat -gt 1) {
    $baseCount = $snapshots.Count
    for ($round = 1; $round -lt $Repeat; $round++) {
        for ($index = 0; $index -lt $baseCount; $index++) {
            $original = $snapshots[$index]
            $snapshot = [Activator]::CreateInstance($snapshotType)
            $snapshotType.GetProperty('Path').SetValue($snapshot, $original.Path, $null)
            $snapshotType.GetProperty('ManualRotation').SetValue($snapshot, $original.ManualRotation, $null)
            $listType.GetMethod('Add').Invoke($snapshots, @($snapshot))
        }
    }
}
$options = [Activator]::CreateInstance($optionsType)
$optionsType.GetProperty('Orientation').SetValue($options, [Enum]::Parse($orientationType, $(if ($Landscape) { 'Landscape' } else { 'Portrait' })), $null)
$optionsType.GetProperty('AutoRotate').SetValue($options, $true, $null)
$optionsType.GetProperty('MarginMm').SetValue($options, 10, $null)
$optionsType.GetProperty('Quality').SetValue($options, [Enum]::Parse($qualityType, 'Print'), $null)
$optionsType.GetProperty('Mode').SetValue($options, [Enum]::Parse($modeType, $(if ($Separate) { 'Separate' } else { 'Merge' })), $null)
$optionsType.GetProperty('BaseName').SetValue($options, 'smoke', $null)
$outputDir = Join-Path $testRoot 'out'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$output = Join-Path $outputDir 'smoke.pdf'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
$separateDir = Join-Path $outputDir 'separate'
if ($Separate -and (Test-Path -LiteralPath $separateDir)) { Remove-Item -LiteralPath $separateDir -Recurse -Force }
if ($Separate) { New-Item -ItemType Directory -Force -Path $separateDir | Out-Null }
$token = New-Object System.Threading.CancellationToken
$method = $exporterType.GetMethod('ExportMerged', [Reflection.BindingFlags] 'Public,NonPublic,Static')
$script:maxMemory = 0
$progress = [Action[int]]{
    param($value)
    $current = [GC]::GetTotalMemory($false)
    if ($current -gt $script:maxMemory) { $script:maxMemory = $current }
}
$invokeArgs = New-Object object[] 5
$invokeArgs[0] = [string]$(if ($Separate) { $separateDir } else { $output })
$invokeArgs[1] = $snapshots
$invokeArgs[2] = $options
$invokeArgs[3] = $progress
$invokeArgs[4] = $token
$methodName = if ($Separate) { 'ExportSeparate' } else { 'ExportMerged' }
$method = $exporterType.GetMethod($methodName, [Reflection.BindingFlags] 'Public,NonPublic,Static')
$method.Invoke($null, $invokeArgs)
if ($Separate) {
    $info = Get-ChildItem -LiteralPath $separateDir -Filter '*.pdf'
    Write-Host ("Separate export complete: files={0}, pages={1}" -f $info.Count, $snapshots.Count)
} else {
    $info = Get-Item -LiteralPath $output
    Write-Host ("Smoke export complete: {0} bytes, pages={1}, maxManagedMemory={2:N0}" -f $info.Length, $snapshots.Count, $script:maxMemory)
}
