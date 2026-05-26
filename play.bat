@echo off
cd /d "%~dp0"
echo Choose your difficulty:
echo   [1] Easy   (dummy wins ~16%%)
echo   [2] Medium (dummy wins ~23%%)
echo   [3] Hard   (dummy wins ~59%%)
echo.
set /p choice="Enter 1, 2, or 3: "

if "%choice%"=="1" (
  .venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_easy.csv --player-cards data\player_cards.csv --verbose %*
) else if "%choice%"=="2" (
  .venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_medium.csv --player-cards data\player_cards.csv --verbose %*
) else if "%choice%"=="3" (
  .venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_hard.csv --player-cards data\player_cards.csv --verbose %*
) else (
  echo Invalid choice. Defaulting to Medium difficulty.
  .venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_medium.csv --player-cards data\player_cards.csv --verbose %*
)
pause
