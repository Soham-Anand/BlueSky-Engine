@echo off
REM Setup file associations for BlueSky Editor on Windows

echo === BlueSky Editor File Association Setup ===
echo.

REM Get the path to BlueSky.Editor executable
set EDITOR_PATH=%~dp0BlueSky.Editor\bin\Release\net8.0\BlueSky.Editor.exe

if not exist "%EDITOR_PATH%" (
    echo Error: BlueSky.Editor not found at: %EDITOR_PATH%
    echo Please build the project first: dotnet build -c Release
    exit /b 1
)

echo Found BlueSky.Editor at: %EDITOR_PATH%
echo.

REM Create registry entries for file associations
echo Setting up Windows file associations...

REM BlueScript (.bluescript)
reg add "HKCU\Software\Classes\.bluescript" /ve /d "BlueSky.BlueScript" /f
reg add "HKCU\Software\Classes\BlueSky.BlueScript" /ve /d "BlueScript File" /f
reg add "HKCU\Software\Classes\BlueSky.BlueScript\DefaultIcon" /ve /d "%EDITOR_PATH%,0" /f
reg add "HKCU\Software\Classes\BlueSky.BlueScript\shell\open\command" /ve /d "\"%EDITOR_PATH%\" \"%%1\"" /f

REM BlueProject (.blueproject)
reg add "HKCU\Software\Classes\.blueproject" /ve /d "BlueSky.BlueProject" /f
reg add "HKCU\Software\Classes\BlueSky.BlueProject" /ve /d "BlueProject File" /f
reg add "HKCU\Software\Classes\BlueSky.BlueProject\DefaultIcon" /ve /d "%EDITOR_PATH%,0" /f
reg add "HKCU\Software\Classes\BlueSky.BlueProject\shell\open\command" /ve /d "\"%EDITOR_PATH%\" \"%%1\"" /f

REM BlueAsset (.blueasset)
reg add "HKCU\Software\Classes\.blueasset" /ve /d "BlueSky.BlueAsset" /f
reg add "HKCU\Software\Classes\BlueSky.BlueAsset" /ve /d "BlueAsset File" /f
reg add "HKCU\Software\Classes\BlueSky.BlueAsset\DefaultIcon" /ve /d "%EDITOR_PATH%,0" /f
reg add "HKCU\Software\Classes\BlueSky.BlueAsset\shell\open\command" /ve /d "\"%EDITOR_PATH%\" \"%%1\"" /f

echo ✓ Windows file associations set up
echo.

REM Refresh shell
echo Refreshing Windows Explorer...
taskkill /f /im explorer.exe >nul 2>&1
start explorer.exe

echo === Setup Complete ===
echo.
echo You can now:
echo 1. Double-click .bluescript files to open in BlueSky Editor
echo 2. Use command line: "%EDITOR_PATH%" "file.bluescript"
echo 3. Drag files onto the BlueSky.Editor.exe
echo.
echo Test with: "%EDITOR_PATH%" "TestScript.bluescript"
echo.

pause