@echo off
cd /d "%~dp0"
.venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_hard.csv --verbose %*
pause
