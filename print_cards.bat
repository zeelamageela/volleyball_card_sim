@echo off
setlocal
cd /d "%~dp0"
call ".venv\Scripts\activate.bat"
python make_cards.py --output all_cards.pdf
if errorlevel 1 exit /b %errorlevel%
