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
set "DB_PATH=%TOOLKIT_DIR%\ecosystem.db"
set "REPORT_DIR=%TOOLKIT_DIR%\reports"

:: Check if game path exists
if not exist "%GAME_PATH%\Data\Config" (
    echo [!] Default game path not found: %GAME_PATH%
    echo.
    set /p "GAME_PATH=Enter your 7 Days To Die install path: "
)

:: Ask for mods folder
echo.
echo Enter the path to your Mods folder
echo   Example: C:\Steam\steamapps\common\7 Days To Die\Mods
echo   Or press ENTER to skip mod analysis
echo.
set /p "MODS_PATH=Mods folder path: "

echo.
echo  ----------------------------------------
echo   Starting Analysis...
echo  ----------------------------------------
echo.

:: Run the analysis from toolkit directory
cd /d "%TOOLKIT_DIR%"

if "%MODS_PATH%"=="" (
    echo [*] Running base game analysis only (no mods)...
    dotnet run -- build "%GAME_PATH%" "%DB_PATH%"
) else (
    echo [*] Running full analysis with mods...
    dotnet run -- full-analyze "%GAME_PATH%" "%MODS_PATH%" "%DB_PATH%"
)

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [X] Analysis failed! Check errors above.
    pause
    exit /b 1
)

echo.
echo  ----------------------------------------
echo   Generating HTML Report...
echo  ----------------------------------------
echo.

dotnet run -- report "%DB_PATH%" "%REPORT_DIR%" --html

echo.
echo  ========================================
echo   Analysis Complete!
echo  ========================================
echo.
echo  Database: %DB_PATH%
echo  Reports:  %REPORT_DIR%
echo.

pause
