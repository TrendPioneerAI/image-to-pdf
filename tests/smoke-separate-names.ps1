$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent $testRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $appRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
$assembly = [Reflection.Assembly]::LoadFrom($exe.FullName)
$snapshotType = $assembly.GetType('LocalImageToPdf.ImageSnapshot', $true)
$optionsType = $assembly.GetType('LocalImageToPdf.ExportOptions', $true)
$exporterType = $assembly.GetType('LocalImageToPdf.PdfExporter', $true)
$qualityType = $assembly.GetType('LocalImageToPdf.QualityPreset', $true)
$modeType = $assembly.GetType('LocalImageToPdf.ExportMode', $true)
$listType = [Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($snapshotType)
$snapshots = [Activator]::CreateInstance($listType)
$files = Get-ChildItem -LiteralPath (Join-Path $testRoot 'fixtures') -File |
    Where-Object { $_.Extension -in '.jpg','.jpeg','.png','.bmp' -and $_.BaseName -match '^(01|02|03)_' } |
    Sort-Object Name
$names = @('同名', '同名', '合同/2026')
for ($index = 0; $index -lt $files.Count; $index++) {
    $snapshot = [Activator]::CreateInstance($snapshotType)
    $snapshotType.GetProperty('Path').SetValue($snapshot, $files[$index].FullName, $null)
    $snapshotType.GetProperty('OutputName').SetValue($snapshot, $names[$index], $null)
    $listType.GetMethod('Add').Invoke($snapshots, @($snapshot))
}
$options = [Activator]::CreateInstance($optionsType)
$optionsType.GetProperty('Quality').SetValue($options, [Enum]::Parse($qualityType, 'Print'), $null)
$optionsType.GetProperty('Mode').SetValue($options, [Enum]::Parse($modeType, 'Separate'), $null)
$outDir = Join-Path (Join-Path $testRoot 'out') 'separate-names'
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$method = $exporterType.GetMethod('ExportSeparate', [Reflection.BindingFlags] 'Public,NonPublic,Static')
$invokeArgs = New-Object object[] 5
$invokeArgs[0] = [string]$outDir
$invokeArgs[1] = $snapshots
$invokeArgs[2] = $options
$invokeArgs[3] = [Action[int]] { param($value) }
$invokeArgs[4] = [System.Threading.CancellationToken]::None
$method.Invoke($null, $invokeArgs)
$actual = @(Get-ChildItem -LiteralPath $outDir -Filter '*.pdf' -File | Sort-Object Name | Select-Object -ExpandProperty Name)
$expected = @('合同_2026.pdf', '同名 (2).pdf', '同名.pdf')
if ((Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0) {
    throw ('Unexpected output names: ' + ($actual -join ', '))
}
Write-Host ('Separate naming complete: ' + ($actual -join ', '))
