@echo off
setlocal

set "PROJECT=%~dp0VoiceInputApp\VoiceInputApp.csproj"
set "OUTPUT_DIR=%~dp0VoiceInputApp\bin\Debug\net8.0-windows10.0.19041.0"
set "EXE_PATH=%OUTPUT_DIR%\VoiceInput.exe"

tasklist /FI "IMAGENAME eq VoiceInput.exe" | find /I "VoiceInput.exe" >nul
if %ERRORLEVEL%==0 (
    echo VoiceInput is running. Please exit it before building.
    exit /b 1
)

echo Building...
dotnet build "%PROJECT%"
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

if exist "%EXE_PATH%" (
    echo Build succeeded.
    echo EXE: %EXE_PATH%
    exit /b 0
) else (
    echo Build finished, but VoiceInput.exe was not found.
    exit /b 1
)
