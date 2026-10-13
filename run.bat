@echo off
rem Windows launcher for the Slay the Spire PvP mod.
rem Copy sts-pvp.jar and this file into the game folder (Steam > Manage > Browse local files), then:
rem   run.bat host [port]          wait for the opponent
rem   run.bat join <ip> [port]     connect to the host (its Hamachi IPv4)
setlocal
cd /d "%~dp0"
set PORT=24680
if /i "%~1"=="host" (
    if not "%~2"=="" set PORT=%~2
    set MODE=-Dpvp.host=true
) else if /i "%~1"=="join" (
    if "%~2"=="" goto usage
    if not "%~3"=="" set PORT=%~3
    set MODE=-Dpvp.join=%~2
) else goto usage
jre\bin\java -Xmx1G -Dsun.java2d.dpiaware=true %MODE% -Dpvp.port=%PORT% -cp "sts-pvp.jar;desktop-1.0.jar" com.megacrit.cardcrawl.desktop.DesktopLauncher
goto :eof
:usage
echo usage: run.bat host [port] ^| run.bat join ^<ip^> [port]
pause
