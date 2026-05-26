@echo off
cd /d "%~dp0"

echo === Testing vs Easy Dummy ===
.venv\Scripts\python.exe main.py --games 1000 --seed 42 --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_dummy_easy.csv --mode pvd --strategy-a smart
echo.

echo === Testing vs Medium Dummy ===
.venv\Scripts\python.exe main.py --games 1000 --seed 42 --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_dummy_medium.csv --mode pvd --strategy-a smart
echo.

echo === Testing vs Hard Dummy ===
.venv\Scripts\python.exe main.py --games 1000 --seed 42 --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_dummy_hard.csv --mode pvd --strategy-a smart
echo.

echo === Blitz vs Grind ===
.venv\Scripts\python.exe main.py --games 1000 --seed 42 --player-cards data\player_cards.csv --roster-a data\team_blitz.csv --roster-b data\team_grind.csv --mode pvp --strategy-a smart --strategy-b smart
echo.
pause
