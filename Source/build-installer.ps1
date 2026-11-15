$ErrorActionPreference='Stop'
$sourceDir=$PSScriptRoot
$suiteDir=Split-Path $sourceDir -Parent
$portableDir=Join-Path $suiteDir 'Portable'
$payloadZip=Join-Path $sourceDir 'payload.zip'
$payloadXml=Join-Path $sourceDir 'payload.xml'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $payloadZip) {Remove-Item -LiteralPath $payloadZip}
[IO.Compression.ZipFile]::CreateFromDirectory($portableDir,$payloadZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$document=New-Object Xml.XmlDocument
$filesRoot=$document.CreateElement('Files')
$document.AppendChild($filesRoot)|Out-Null
$relativeRoot=[IO.Path]::GetFullPath($portableDir).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $portableDir -Recurse -File|ForEach-Object { $relativeName=$_.FullName.Substring($relativeRoot.Length).Replace('\','/');$element=$document.CreateElement('File');$element.SetAttribute('path',$relativeName);$element.SetAttribute('sha256',(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant());$filesRoot.AppendChild($element)|Out-Null }
$document.Save($payloadXml)
$frameworkDir=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Xaml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')|ForEach-Object {'/reference:'+(Join-Path $frameworkDir $_)}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')|ForEach-Object {'/reference:'+(Join-Path $frameworkDir ('WPF/'+$_))}
$baseSource=Join-Path $sourceDir 'apps/ProgressNotesAssistant'
$installer=Join-Path $suiteDir '临床文书助手四合一_安装程序.exe'
& (Join-Path $frameworkDir 'csc.exe') /nologo /target:winexe /platform:x86 /optimize+ /main:ClinicalSuite.SuiteSetup ('/out:'+$installer) ('/win32icon:'+(Join-Path $baseSource 'app.ico')) ('/win32manifest:'+(Join-Path $baseSource 'app.manifest')) ('/resource:'+$payloadZip+',payload.zip') ('/resource:'+$payloadXml+',payload.xml') @references (Join-Path $sourceDir 'SuiteSetup.cs')
if($LASTEXITCODE -ne 0) {throw '四合一安装程序构建失败'}
Copy-Item -LiteralPath (Join-Path $baseSource 'app.config') -Destination ($installer+'.config') -Force
Write-Output ('统一安装程序构建完成：'+$installer)
