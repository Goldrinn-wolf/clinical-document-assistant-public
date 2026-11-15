param([switch]$MedicationOnly)
$ErrorActionPreference='Stop'
$sourceDir=$PSScriptRoot
$suiteDir=Split-Path $sourceDir -Parent
$portableDir=Join-Path $suiteDir 'Portable'
$frameworkDir=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$compiler=Join-Path $frameworkDir 'csc.exe'
function Invoke-Csc {
 $responsePath=Join-Path ([IO.Path]::GetTempPath()) ('clinical-csc-'+[Guid]::NewGuid().ToString('N')+'.rsp')
 try {
  $lines=@($args | ForEach-Object { '"'+([string]$_).Replace('"','\"')+'"' })
  [IO.File]::WriteAllLines($responsePath,$lines,[Text.UTF8Encoding]::new($true))
  & $compiler ('@'+$responsePath)
  $script:LASTEXITCODE=$LASTEXITCODE
 } finally { if(Test-Path -LiteralPath $responsePath) { Remove-Item -LiteralPath $responsePath } }
}

$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Xaml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:'+(Join-Path $frameworkDir $_) }
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/reference:'+(Join-Path $frameworkDir ('WPF/'+$_)) }
$smartSource=Join-Path $sourceDir 'OfflineLab'
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
Invoke-Csc /nologo /target:library /platform:x86 /optimize+ /define:OFFLINE_LAB_EMBEDDED ('/out:'+(Join-Path $portableDir 'OfflineLab.dll')) @references (Join-Path $smartSource 'App.cs') (Join-Path $smartSource 'Formatter.cs') (Join-Path $smartSource 'Tests.cs')
if($LASTEXITCODE -ne 0){throw '离线智能整理组件构建失败'}
$smartReference='/reference:'+(Join-Path $portableDir 'OfflineLab.dll')
$medSource=Join-Path $sourceDir 'apps/DischargeAssistant' 
$medDestination=Join-Path $portableDir 'apps/DischargeAssistant'
if($true) {
 New-Item -ItemType Directory -Path $medDestination -Force | Out-Null
 $medSources=@('App.cs','Core.cs','Custom.cs','Tests.cs','Followup.cs','FixTests.cs') | ForEach-Object {Join-Path $medSource $_}
 Invoke-Csc /nologo /target:winexe /platform:x86 /optimize+ /main:DischargeAssistant.Program ('/out:'+(Join-Path $medDestination 'DischargeAssistant.exe')) ('/win32icon:'+(Join-Path $medSource 'app.ico')) ('/win32manifest:'+(Join-Path $medSource 'app.manifest')) ('/resource:'+(Join-Path $medSource 'followup-defaults.xml')+',followup-defaults.xml') ('/resource:'+(Join-Path $medSource 'MainWindow.xaml')+',MainWindow.xaml') ('/resource:'+(Join-Path $medSource 'library.xml')+',library.xml') ('/resource:'+(Join-Path $medSource 'pinyin.tsv')+',pinyin.tsv') @references @medSources
 if($LASTEXITCODE -ne 0) {throw '带药程序构建失败'}
 Copy-Item -LiteralPath (Join-Path $medSource 'app.config') -Destination (Join-Path $medDestination 'DischargeAssistant.exe.config') -Force
}
if($MedicationOnly){return}
foreach($productName in @('InstructionsAssistant','FirstCourseAssistant','ProgressNotesAssistant')) {
 $appSource=Join-Path $sourceDir ('apps/'+$productName)
 $destination=Join-Path $portableDir ('apps/'+$productName)
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 $dataName=if($productName -eq 'InstructionsAssistant') {'instructions.xml'} elseif($productName -eq 'FirstCourseAssistant') {'templates.xml'} else {'library.xml'}
 $sources=@('App.cs','Core.cs','Tests.cs','Custom.cs','CustomTests.cs') | ForEach-Object { Join-Path $appSource $_ }
 if($productName -eq 'ProgressNotesAssistant') { $sources+=Join-Path $appSource 'CoreTests.cs'; $sources+=Join-Path $appSource 'ExamsOnly.cs'; $sources+=Join-Path $appSource 'ExamCategories.cs'; $sources+=Join-Path $appSource 'SmartLabIntegration.cs' }
 if($productName -eq 'FirstCourseAssistant') { $sources+=Join-Path $appSource 'PlanBuilder.cs' }
 $sources+=Join-Path $sourceDir 'Customization.cs'
 $sources+=Join-Path $sourceDir 'CustomTestSupport.cs'
 Invoke-Csc /nologo /target:winexe /platform:x86 /optimize+ ('/main:'+$productName+'.Program') ('/out:'+(Join-Path $destination ($productName+'.exe'))) ('/win32icon:'+(Join-Path $appSource 'app.ico')) ('/win32manifest:'+(Join-Path $appSource 'app.manifest')) ('/resource:'+(Join-Path $appSource 'MainWindow.xaml')+',MainWindow.xaml') ('/resource:'+(Join-Path $appSource $dataName)+','+$dataName) ('/resource:'+(Join-Path $appSource 'pinyin.tsv')+',pinyin.tsv') @references $smartReference @sources
 if($LASTEXITCODE -ne 0) { throw ('构建失败：'+$productName) }
 if($productName -eq 'ProgressNotesAssistant') {
  $examDestination=Join-Path $portableDir 'apps/ExaminationAssistant'
  New-Item -ItemType Directory -Path $examDestination -Force | Out-Null
  Invoke-Csc /nologo /target:winexe /platform:x86 /optimize+ /main:ExaminationAssistant.EntryPoint ('/out:'+(Join-Path $examDestination 'ExaminationAssistant.exe')) ('/win32icon:'+(Join-Path $appSource 'app.ico')) ('/win32manifest:'+(Join-Path $appSource 'app.manifest')) ('/resource:'+(Join-Path $appSource 'MainWindow.xaml')+',MainWindow.xaml') ('/resource:'+(Join-Path $appSource $dataName)+','+$dataName) ('/resource:'+(Join-Path $appSource 'pinyin.tsv')+',pinyin.tsv') @references $smartReference @sources
  if($LASTEXITCODE -ne 0) {throw '辅助检查独立版构建失败'}
  Copy-Item -LiteralPath (Join-Path $portableDir 'OfflineLab.dll') -Destination $examDestination -Force
  Copy-Item -LiteralPath (Join-Path $appSource 'app.config') -Destination (Join-Path $examDestination 'ExaminationAssistant.exe.config') -Force
  Copy-Item -LiteralPath (Join-Path $appSource 'pypinyin_LICENSE.txt') -Destination $examDestination -Force
 }
 if($productName -eq 'ProgressNotesAssistant') { Copy-Item -LiteralPath (Join-Path $portableDir 'OfflineLab.dll') -Destination $destination -Force }
 Copy-Item -LiteralPath (Join-Path $appSource 'app.config') -Destination (Join-Path $destination ($productName+'.exe.config')) -Force
 Copy-Item -LiteralPath (Join-Path $appSource 'pypinyin_LICENSE.txt') -Destination $destination -Force
}
$baseSource=Join-Path $sourceDir 'apps/ProgressNotesAssistant'
Invoke-Csc /nologo /target:winexe /platform:x86 /optimize+ /main:ClinicalSuite.Launcher ('/out:'+(Join-Path $portableDir 'ClinicalSuite.exe')) ('/win32icon:'+(Join-Path $baseSource 'app.ico')) ('/win32manifest:'+(Join-Path $baseSource 'app.manifest')) ('/resource:'+(Join-Path $sourceDir 'Launcher.xaml')+',Launcher.xaml') @references $smartReference (Join-Path $sourceDir 'Launcher.cs')
if($LASTEXITCODE -ne 0) { throw '首页构建失败' }
Copy-Item -LiteralPath (Join-Path $baseSource 'app.config') -Destination (Join-Path $portableDir 'ClinicalSuite.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $suiteDir '使用说明.txt') -Destination $portableDir -Force
Copy-Item -LiteralPath (Join-Path $suiteDir '测试与兼容性.md') -Destination $portableDir -Force
Write-Output ('四合一应用构建完成：'+$portableDir)
