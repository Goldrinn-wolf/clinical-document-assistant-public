@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 936 >nul
title 临床文书助手 - 环境检查与启动
set "CHECK_ONLY="
if /i "%~1"=="--check-only" set "CHECK_ONLY=1"
set "APP_DIR=%~dp001_直接运行\临床文书助手四合一"
set "NET_RELEASE="
set "NET_NUMBER=0"
echo 临床文书助手四合一 v1.4.1-public - 环境补全版
echo.
ver
echo.
for /f "tokens=1,2,3" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release /reg:32 2^>nul') do if /i "%%A"=="Release" if /i "%%B"=="REG_DWORD" set "NET_RELEASE=%%C"
if not defined NET_RELEASE goto missing_net
set /a NET_NUMBER=0+%NET_RELEASE% >nul 2>nul
if errorlevel 1 set "NET_NUMBER=0"
echo 检测到 .NET Framework Release：%NET_NUMBER%
if %NET_NUMBER% LSS 528040 goto missing_net
echo [通过] 已安装 .NET Framework 4.8 或更高版本。
echo.
for %%F in ("ClinicalSuite.exe" "ClinicalSuite.exe.config" "OfflineLab.dll" "apps\DischargeAssistant\DischargeAssistant.exe" "apps\DischargeAssistant\DischargeAssistant.exe.config" "apps\InstructionsAssistant\InstructionsAssistant.exe" "apps\InstructionsAssistant\InstructionsAssistant.exe.config" "apps\FirstCourseAssistant\FirstCourseAssistant.exe" "apps\FirstCourseAssistant\FirstCourseAssistant.exe.config" "apps\ExaminationAssistant\ExaminationAssistant.exe" "apps\ExaminationAssistant\ExaminationAssistant.exe.config" "apps\ExaminationAssistant\OfflineLab.dll" "apps\ProgressNotesAssistant\ProgressNotesAssistant.exe" "apps\ProgressNotesAssistant\ProgressNotesAssistant.exe.config" "apps\ProgressNotesAssistant\OfflineLab.dll") do if not exist "%APP_DIR%\%%~F" goto missing_files
echo [通过] 主程序和各工具的必要文件齐全。
if defined CHECK_ONLY exit /b 0
echo 正在启动文书助手……
start "" /D "%APP_DIR%" "%APP_DIR%\ClinicalSuite.exe"
if errorlevel 1 goto start_failed
exit /b 0

:missing_net
echo.
echo [需要补环境] 本程序要求 .NET Framework 4.8。
echo 只有 4.0、4.5、4.6、4.7 等旧版仍不够；仅安装新版 .NET 8/9/10 也不能代替它。
echo.
echo 请打开本文件夹的“03_环境组件”，运行：
echo NET-Framework-4.8-离线安装.exe
echo 按微软安装向导完成安装；如提示重启，重启后再点本启动入口。
echo 已有 4.8 或 4.8.1 的电脑无需重复安装。
echo.
echo Windows 7 需要先有 SP1；Windows 8 原版不支持此环境，可使用 Windows 8.1 或更高版本。
echo 安装环境需要管理员权限，院内电脑可交信息科协助。
echo.
if defined CHECK_ONLY exit /b 10
pause
exit /b 10

:missing_files
echo.
echo [文件不完整] 请完整解压整个压缩包，再从解压后的文件夹启动。
echo 保留 01_直接运行 下的 apps 文件夹、全部 EXE、配置文件和 OfflineLab.dll。
echo 如解压后仍缺文件，请检查安全软件的隔离记录或联系信息科。
echo.
if defined CHECK_ONLY exit /b 20
pause
exit /b 20

:start_failed
echo.
echo [启动未完成] .NET 检查已通过，请保留随后显示的错误提示。
echo 请尝试直接打开 01_直接运行\临床文书助手四合一\ClinicalSuite.exe，查看具体报错。
echo.
if defined CHECK_ONLY exit /b 21
pause
exit /b 21
