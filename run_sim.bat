@echo off
cd /d "%~dp0"
.venv\Scripts\python.exe main.py --games 1000 --seed 42 --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_grind.csv
echo.
.venv\Scripts\python.exe report.py --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_grind.csv
pause
