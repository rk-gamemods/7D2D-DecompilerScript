@echo off
title 7D2D Mod Ecosystem Analyzer
color 0A

echo.
echo  ========================================
echo   7D2D Mod Ecosystem Analyzer
echo  ========================================
echo.

:: Default paths (edit these if needed)
set "GAME_PATH=C:\Steam\steamapps\common\7 Days To Die"
set "TOOLKIT_DIR=%~dp0toolkit\XmlIndexer"
set "REPORT_DIR=%TOOLKIT_DIR%\reports"

:: Check if game path exists
if not exist "%GAME_PATH%\Data\Config" (
    echo [!] Default game path not found: %GAME_PATH%
    echo.
    set /p "GAME_PATH=Enter your 7 Days To Die install path: "
)

:: Mods folder is usually in the game directory
set "MODS_PATH=%GAME_PATH%\Mods"

echo.
echo Enter the path to your Mods folder
echo   Default: %MODS_PATH%
echo   Press ENTER to use default, or enter a custom path
echo.
set /p "CUSTOM_MODS=Mods folder path (or press ENTER): "

if not "%CUSTOM_MODS%"=="" set "MODS_PATH=%CUSTOM_MODS%"

echo.
echo  ----------------------------------------
echo   Starting Analysis...
echo  ----------------------------------------
echo.
echo Game Path: %GAME_PATH%
echo Mods Path: %MODS_PATH%
echo Output:    %REPORT_DIR%
echo.

:: Run the analysis from toolkit directory
cd /d "%TOOLKIT_DIR%"

echo [*] Running full analysis with report generation...
dotnet run -- report "%GAME_PATH%" "%MODS_PATH%" "%REPORT_DIR%" --open

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [X] Analysis failed! Check errors above.
    pause
    exit /b 1
)

echo.
echo  ========================================
echo   Analysis Complete!
echo  ========================================
echo.
echo  Reports:  %REPORT_DIR%
echo.
echo  TIP: To track game updates, run Decompile-7D2D.ps1
echo       which creates git commits for each version.
echo.

pause
