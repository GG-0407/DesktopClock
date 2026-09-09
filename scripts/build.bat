@echo off
rem 构建桌面时钟主程序（零依赖，仅使用系统自带的 .NET Framework 编译器）
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
pushd "%~dp0.."
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
    /win32icon:app.ico ^
    /out:DesktopClock.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    src\Program.cs src\Calendar.cs
if %errorlevel%==0 (echo Build OK: DesktopClock.exe) else (echo Build FAILED)
pause
popd
if %errorlevel%==0 (echo 构建成功: DesktopClock.exe) else (echo 构建失败)
pause
