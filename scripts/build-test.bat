@echo off
rem 构建并运行农历/节气算法校验
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
pushd "%~dp0.."
"%CSC%" /nologo /target:exe /optimize+ /platform:anycpu /codepage:65001 ^
    /out:calcheck.exe ^
    /r:System.dll /r:System.Core.dll ^
    src\Test.cs src\Calendar.cs
if %errorlevel%==0 (calcheck.exe) else (echo Build FAILED)
pause
popd
