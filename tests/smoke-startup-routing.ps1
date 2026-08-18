$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exeName = ([char]0x56fe).ToString() + ([char]0x7247).ToString() + ([char]0x8f6c).ToString() + 'PDF.exe'
$guideName = ([char]0x5feb).ToString() + ([char]0x901f).ToString() + ([char]0x4f7f).ToString() + ([char]0x7528).ToString() + ([char]0x6307).ToString() + ([char]0x5357).ToString() + '.pdf'
$exe = Join-Path $projectRoot ('dist\' + $exeName)
$pdf = Join-Path $projectRoot ('dist\' + $guideName)
$image = Join-Path $projectRoot 'assets\guide-bg-01.png'

if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $projectRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath $pdf)) { throw 'The quick-start PDF is required.' }
if (-not (Test-Path -LiteralPath $image)) { throw 'The image fixture is required.' }

function Test-DirectPdfRoute([string[]]$paths) {
    $arguments = New-Object object[] 1
    $arguments[0] = $paths
    return [bool]$script:routeMethod.Invoke($null, $arguments)
}

$assembly = [Reflection.Assembly]::LoadFile($exe)
$programType = $assembly.GetType('LocalImageToPdf.Program', $true)
$script:routeMethod = $programType.GetMethod('ShouldLaunchPdfToImages', [Reflection.BindingFlags]'Static, NonPublic')
if ($null -eq $script:routeMethod) { throw 'Startup route method was not found.' }
$script:onboardingMethod = $programType.GetMethod('ShouldShowSendToOnboarding', [Reflection.BindingFlags]'Static, NonPublic')
if ($null -eq $script:onboardingMethod) { throw 'Onboarding decision method was not found.' }
$settingsType = $assembly.GetType('LocalImageToPdf.AppSettings', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$completedProperty = $settingsType.GetProperty('SendToOnboardingCompleted')
if ($null -eq $completedProperty) { throw 'Onboarding settings property was not found.' }

if (Test-DirectPdfRoute @($image)) { throw 'An image must open the image-to-PDF window.' }
if (-not (Test-DirectPdfRoute @($pdf))) { throw 'A PDF must directly open the PDF-to-image window.' }
if (-not (Test-DirectPdfRoute @($pdf, $pdf))) { throw 'Multiple PDFs must directly open the PDF-to-image window.' }
if (Test-DirectPdfRoute @($image, $pdf)) { throw 'Mixed inputs must not be misclassified as PDF-only.' }

$onboardingArguments = New-Object object[] 2
$onboardingArguments[0] = $settings
$onboardingArguments[1] = $false
if (-not [bool]$script:onboardingMethod.Invoke($null, $onboardingArguments)) { throw 'First launch without a shortcut must show onboarding.' }
$completedProperty.SetValue($settings, $true, $null)
if ([bool]$script:onboardingMethod.Invoke($null, $onboardingArguments)) { throw 'Completed onboarding must not be shown again.' }
$completedProperty.SetValue($settings, $false, $null)
$onboardingArguments[1] = $true
if ([bool]$script:onboardingMethod.Invoke($null, $onboardingArguments)) { throw 'An existing shortcut must suppress onboarding.' }

[pscustomobject]@{
    Result = 'PASS'
    ImageRoute = 'image-to-pdf'
    PdfOnlyRoute = 'direct-pdf-to-image'
    BatchPdfRoute = 'direct-pdf-to-image'
    FirstRunOnboarding = 'one-time'
} | Format-List
