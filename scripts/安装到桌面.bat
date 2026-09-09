@echo off
rem Create a desktop shortcut for the clock (ASCII-only content to avoid codepage issues).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-shortcut.ps1"
if %errorlevel%==0 (echo Shortcut created on desktop.) else (echo Failed to create shortcut.)
pause
