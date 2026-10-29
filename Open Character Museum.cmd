@echo off
cd /d "%~dp0"
if not exist "Builds\Museum\CharacterMuseum.exe" (
  echo Builds\Museum\CharacterMuseum.exe does not exist.
  echo Open the project in Unity, open CharacterMuseum.unity and press Play instead.
  pause
  exit /b 1
)
start "Character Museum" "Builds\Museum\CharacterMuseum.exe"
