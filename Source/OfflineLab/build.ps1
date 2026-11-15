$ErrorActionPreference='Stop'
$sourceDir=$PSScriptRoot
$outputDir=Join-Path (Split-Path $sourceDir -Parent) 'Portable'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$frameworkDir=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $frameworkDir $_) }
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/reference:'+(Join-Path $frameworkDir ('WPF/'+$_)) }
$response=Join-Path $outputDir 'build.rsp'
try {
 $arguments=@('/nologo','/target:winexe','/platform:x86','/optimize+','/main:OfflineLab.Program',('/out:'+(Join-Path $outputDir 'LabReportOrganizer.exe')),('/win32manifest:'+(Join-Path $sourceDir 'app.manifest')))+$references+@('App.cs','Formatter.cs','Tests.cs' | ForEach-Object {Join-Path $sourceDir $_})
 [IO.File]::WriteAllLines($response,@($arguments | ForEach-Object {'"'+$_+'"'}),[Text.UTF8Encoding]::new($true))
 & (Join-Path $frameworkDir 'csc.exe') ('@'+$response)
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 Copy-Item -LiteralPath (Join-Path $sourceDir 'app.config') -Destination (Join-Path $outputDir 'LabReportOrganizer.exe.config') -Force
} finally {if(Test-Path -LiteralPath $response){Remove-Item -LiteralPath $response}}
Write-Output $outputDir
