@echo off
setlocal enabledelayedexpansion
title 7D2D Mod Ecosystem Analyzer
color 0A

echo.
echo  ========================================
echo   7D2D Mod Ecosystem Analyzer
echo  ========================================
echo.

set "TOOLKIT_DIR=%~dp0toolkit\XmlIndexer"
set "REPORT_DIR=%TOOLKIT_DIR%\reports"
set "CONFIG_FILE=%~dp0config.txt"

:: Load config file if it exists
set "GAME_PATH="
set "MODS_PATH="
set "CODEBASE_PATH="

if not exist "%CONFIG_FILE%" (
    echo.
    echo  [!] ERROR: config.txt not found!
    echo.
    echo  Please create a config.txt file in this folder.
    echo  Copy config.example.txt to config.txt and edit it.
    echo.
    echo  Example config.txt contents:
    echo  -----------------------------
    echo  GAME_PATH=C:\Steam\steamapps\common\7 Days To Die
    echo  MODS_PATH=C:\Steam\steamapps\common\7 Days To Die\Mods
    echo  -----------------------------
    echo.
    echo  Common Steam locations:
    echo    C:\Steam\steamapps\common\7 Days To Die
    echo    C:\Program Files ^(x86^)\Steam\steamapps\common\7 Days To Die
    echo    D:\SteamLibrary\steamapps\common\7 Days To Die
    echo.
    pause
    exit /b 1
)

echo [*] Loading config from config.txt...
for /f "usebackq tokens=1,* delims==" %%a in ("%CONFIG_FILE%") do (
    set "key=%%a"
    set "val=%%b"
    if /i "!key!"=="GAME_PATH" set "GAME_PATH=!val!"
    if /i "!key!"=="MODS_PATH" set "MODS_PATH=!val!"
    if /i "!key!"=="CODEBASE_PATH" set "CODEBASE_PATH=!val!"
)

:: Validate game path
if "!GAME_PATH!"=="" (
    echo.
    echo  [!] ERROR: GAME_PATH not set in config.txt!
    echo.
    echo  Add this line to config.txt:
    echo  GAME_PATH=C:\Steam\steamapps\common\7 Days To Die
    echo.
    pause
    exit /b 1
)

:: Check if game path exists
if not exist "!GAME_PATH!\Data\Config" (
    echo.
    echo  [!] ERROR: Game path invalid!
    echo.
    echo  Path in config: !GAME_PATH!
    echo  Could not find Data\Config folder at this location.
    echo.
    pause
    exit /b 1
)

:: Default mods path if not set
if "!MODS_PATH!"=="" set "MODS_PATH=!GAME_PATH!\Mods"

echo.
echo  Configuration:
echo    Game: !GAME_PATH!
echo    Mods: !MODS_PATH!
echo    Output: !REPORT_DIR!
echo.
echo  ----------------------------------------
echo   Running Analysis...
echo  ----------------------------------------
echo.

:: Run the analysis from toolkit directory
cd /d "!TOOLKIT_DIR!"

:: Build command with optional codebase path
set "CMD=dotnet run -- report "!GAME_PATH!" "!MODS_PATH!" "!REPORT_DIR!" --open"
if not "!CODEBASE_PATH!"=="" set "CMD=!CMD! --codebase "!CODEBASE_PATH!""

!CMD!

if !ERRORLEVEL! NEQ 0 (
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

endlocal
