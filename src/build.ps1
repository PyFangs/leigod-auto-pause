param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
$references += @('Microsoft.CSharp.dll', (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.dll'), (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.UI.Xaml.dll'), (Join-Path $frameworkDir 'WPF/WindowsBase.dll'))
$references += @('System.Runtime','System.ObjectModel','System.Threading.Tasks','System.Runtime.InteropServices.WindowsRuntime') | ForEach-Object {
    (Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET/assembly/GAC_MSIL/' + $_)) -Recurse -Filter '*.dll' | Select-Object -First 1).FullName
}
$references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'System32/WinMetadata') -Filter '*.winmd' | ForEach-Object { $_.FullName }
$referenceArgs = $references | ForEach-Object { '/r:' + $_ }
$sourcePaths = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $outputPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'LeigodGuard.exe' } else { $outputPath = [System.IO.Path]::GetFullPath($OutputPath) }
& $compilerPath /nologo /codepage:65001 /optimize+ /platform:x64 /target:winexe ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + $outputPath) @referenceArgs @sourcePaths
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "已生成：$outputPath"
