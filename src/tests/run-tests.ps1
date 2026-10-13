param([string]$OutDir = (Join-Path (Get-Location) 'work/guard-tests'),[switch]$SkipClientClickTest)
$ErrorActionPreference = 'Stop'
$sourceDir = Split-Path $PSScriptRoot -Parent
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
$references += @((Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.dll'), (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.UI.Xaml.dll'), (Join-Path $frameworkDir 'WPF/WindowsBase.dll'))
$references += @('System.Runtime','System.ObjectModel','System.Threading.Tasks','System.Runtime.InteropServices.WindowsRuntime') | ForEach-Object { (Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET/assembly/GAC_MSIL/' + $_)) -Recurse -Filter '*.dll' | Select-Object -First 1).FullName }
$references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'System32/WinMetadata') -Filter '*.winmd' | ForEach-Object { $_.FullName }
$referenceArgs = $references | ForEach-Object { '/r:' + $_ }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$baseSources = @((Join-Path $sourceDir 'Client.cs'),(Join-Path $sourceDir 'Models.cs'),(Join-Path $sourceDir 'Discovery.cs'),(Join-Path $PSScriptRoot 'Fixtures.cs'))
$unitExe = Join-Path $OutDir 'Tests.exe'
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:exe ('/out:' + $unitExe) @referenceArgs @baseSources (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw '状态测试编译失败' }
& $unitExe
if ($LASTEXITCODE -ne 0) { throw '状态测试失败' }
$restoreExe = Join-Path $OutDir 'RestoreTests.exe'
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:exe ('/out:' + $restoreExe) @referenceArgs @baseSources (Join-Path $PSScriptRoot 'RestoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw '托盘恢复测试编译失败' }
& $restoreExe
if ($LASTEXITCODE -ne 0) { throw '托盘恢复测试失败' }
$integrationExe = Join-Path $OutDir 'IntegrationTest.exe'
$allSources = Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:winexe /main:IntegrationTest ('/out:' + $integrationExe) '/r:Microsoft.CSharp.dll' @referenceArgs @allSources (Join-Path $PSScriptRoot 'IntegrationTest.cs')
if ($LASTEXITCODE -ne 0) { throw '界面测试编译失败' }
if (-not $SkipClientClickTest) {
Start-Process -FilePath $integrationExe -WindowStyle Hidden -Wait
$result = Get-Content -LiteralPath (Join-Path $OutDir 'integration-result.txt') -Raw
Write-Output $result
if (-not $result.StartsWith('PASS:')) { throw '模拟界面测试失败' }
}
$uiExe = Join-Path $OutDir 'UiSettingsTests.exe'
$allSources = Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:exe /main:UiSettingsTests ('/out:' + $uiExe) '/r:Microsoft.CSharp.dll' @referenceArgs @allSources (Join-Path $PSScriptRoot 'Fixtures.cs') (Join-Path $PSScriptRoot 'UiSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw '设置界面测试编译失败' }
& $uiExe
if ($LASTEXITCODE -ne 0) { throw '设置界面测试失败' }
$discoveryExe = Join-Path $OutDir 'DiscoveryTests.exe'
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:exe /main:DiscoveryTests ('/out:' + $discoveryExe) '/r:Microsoft.CSharp.dll' @referenceArgs @allSources (Join-Path $PSScriptRoot 'Fixtures.cs') (Join-Path $PSScriptRoot 'DiscoveryTests.cs')
if ($LASTEXITCODE -ne 0) { throw '自动识别测试编译失败' }
& $discoveryExe
if ($LASTEXITCODE -ne 0) { throw '自动识别测试失败' }
$setupExe = Join-Path $OutDir 'FirstRunSetupTests.exe'
& $compilerPath /nologo /codepage:65001 /platform:x64 /target:exe /main:FirstRunSetupTests ('/out:' + $setupExe) '/r:Microsoft.CSharp.dll' @referenceArgs @allSources (Join-Path $PSScriptRoot 'FirstRunSetupTests.cs')
if ($LASTEXITCODE -ne 0) { throw '首次运行测试编译失败' }
& $setupExe
if ($LASTEXITCODE -ne 0) { throw '首次运行测试失败' }
