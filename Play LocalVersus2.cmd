@echo off
cd /d "%~dp0"
if not exist "Builds\LocalVersus2\LocalVersus2.exe" (
  echo Builds\LocalVersus2\LocalVersus2.exe does not exist yet.
  echo Open the project in Unity and create the file Temp\build-localversus2 to build it.
  pause
  exit /b 1
)
start "" "Builds\LocalVersus2\LocalVersus2.exe"
