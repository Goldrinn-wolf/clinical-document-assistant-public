$ErrorActionPreference='Stop'
$sourceDir=$PSScriptRoot
$suiteDir=Split-Path $sourceDir -Parent
$examDir=Join-Path $suiteDir 'ExaminationAssistant'
$portableDir=Join-Path $examDir 'Portable'
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $suiteDir 'Portable/apps/ExaminationAssistant') -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $portableDir -Force}
Copy-Item -LiteralPath (Join-Path $examDir '使用说明.txt') -Destination $portableDir -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip=Join-Path $sourceDir 'examination-payload.zip'
if(Test-Path -LiteralPath $payloadZip){Remove-Item -LiteralPath $payloadZip}
[IO.Compression.ZipFile]::CreateFromDirectory($portableDir,$payloadZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$doc=New-Object Xml.XmlDocument
$filesRoot=$doc.CreateElement('Files');$doc.AppendChild($filesRoot)|Out-Null
Get-ChildItem -LiteralPath $portableDir -File | ForEach-Object {$entry=$doc.CreateElement('File');$entry.SetAttribute('path',$_.Name);$entry.SetAttribute('sha256',(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant());$filesRoot.AppendChild($entry)|Out-Null}
$payloadXml=Join-Path $sourceDir 'examination-payload.xml';$doc.Save($payloadXml)
$setupSource=[IO.File]::ReadAllText((Join-Path $sourceDir 'SuiteSetup.cs')).Replace('临床文书助手四合一','辅助检查助手').Replace('ClinicalSuite','ExaminationAssistant').Replace('出院带药 · 出院医嘱 · 入院首程 · 日常病程','勾选检查 · 粘贴报告 · 一键生成').Replace('一次安装，四个入口；各工具继续使用原本机数据。','独立生成辅助检查文本，病程内的辅助检查入口仍保留。').Replace('四合一','辅助检查').Replace('1.1.1','1.0.0').Replace('1.1.0','1.0.0').Replace('"ExaminationAssistant","DischargeAssistant","InstructionsAssistant","FirstCourseAssistant","ProgressNotesAssistant","ExaminationAssistant"','"ExaminationAssistant"')
$generatedSetup=Join-Path $sourceDir 'ExaminationSetup.cs';[IO.File]::WriteAllText($generatedSetup,$setupSource,[Text.UTF8Encoding]::new($true))
$frameworkDir=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Xaml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')|ForEach-Object {'/reference:'+(Join-Path $frameworkDir $_)}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')|ForEach-Object {'/reference:'+(Join-Path $frameworkDir ('WPF/'+$_))}
$baseSource=Join-Path $sourceDir 'apps/ProgressNotesAssistant'
$installer=Join-Path $examDir '辅助检查助手_安装程序.exe'
& (Join-Path $frameworkDir 'csc.exe') /nologo /target:winexe /platform:x86 /optimize+ /main:ExaminationAssistant.SuiteSetup ('/out:'+$installer) ('/win32icon:'+(Join-Path $baseSource 'app.ico')) ('/win32manifest:'+(Join-Path $baseSource 'app.manifest')) ('/resource:'+$payloadZip+',payload.zip') ('/resource:'+$payloadXml+',payload.xml') @references $generatedSetup
if($LASTEXITCODE -ne 0){throw '辅助检查安装程序构建失败'}
Copy-Item -LiteralPath (Join-Path $baseSource 'app.config') -Destination ($installer+'.config') -Force
Write-Output ('独立辅助检查安装程序构建完成：'+$installer)
