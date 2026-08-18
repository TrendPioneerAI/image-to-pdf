$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $appRoot 'src'
$distRoot = Join-Path $appRoot 'dist'
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'System C# compiler csc.exe was not found.' }
$compiledOutput = Join-Path $distRoot 'ImageToPdf.exe'
$iconPath = Join-Path $appRoot 'assets\app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'The application icon assets\app.ico was not found.' }
$manifestPath = Join-Path $appRoot 'assets\app.manifest'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'The application manifest assets\app.manifest was not found.' }
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'No C# source files found in src.' }
$references = @(
    'System.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Core.dll',
    'Microsoft.CSharp.dll'
)
$windowsMetadataRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$windowsMetadata = $null
if (Test-Path -LiteralPath $windowsMetadataRoot) {
    $windowsMetadata = Get-ChildItem -LiteralPath $windowsMetadataRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [Version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}
if (-not $windowsMetadata) { throw 'Windows 10/11 SDK metadata Windows.winmd was not found.' }
$frameworkDirectory = Split-Path -Parent $compiler
$windowsRuntime = Join-Path $frameworkDirectory 'System.Runtime.WindowsRuntime.dll'
if (-not (Test-Path -LiteralPath $windowsRuntime)) { throw 'System.Runtime.WindowsRuntime.dll was not found.' }
$systemRuntimeRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime'
$systemRuntime = Get-ChildItem -LiteralPath $systemRuntimeRoot -Recurse -Filter 'System.Runtime.dll' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $systemRuntime) { throw 'System.Runtime.dll was not found.' }
$references += $windowsMetadata
$references += $windowsRuntime
$references += $systemRuntime
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-', '/langversion:5', "/win32icon:$iconPath", "/win32manifest:$manifestPath", "/out:$compiledOutput")
foreach ($reference in $references) { $arguments += "/reference:$reference" }
$arguments += $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $compiledOutput)) { throw 'The compiler did not produce an EXE.' }
$displayName = ([char]0x56fe).ToString() + ([char]0x7247).ToString() + ([char]0x8f6c).ToString() + 'PDF.exe'
$output = Join-Path $distRoot $displayName
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
Move-Item -LiteralPath $compiledOutput -Destination $output
$file = Get-Item -LiteralPath $output
Write-Host ("Build complete: {0} ({1:N0} bytes)" -f $file.FullName, $file.Length)
