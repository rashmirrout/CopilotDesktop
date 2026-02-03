@echo off
echo Building Copilot Agent Desktop...
dotnet build src\CopilotAgent.App\CopilotAgent.App.csproj -c Debug
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Launching application...
start src\CopilotAgent.App\bin\Debug\net8.0-windows\CopilotAgent.exe

echo.
echo Application launched! Check for the window.
echo If you encounter errors, check the log at:
echo %APPDATA%\CopilotAgent\Logs\
echo.
pause