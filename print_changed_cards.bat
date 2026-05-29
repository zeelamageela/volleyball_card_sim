@echo off
setlocal
cd /d "%~dp0"
call ".venv\Scripts\activate.bat"
if exist all_updated_cards.pdf del /f /q all_updated_cards.pdf
if exist all_updated_cards.pdf (
	echo Could not remove all_updated_cards.pdf. Close it in your PDF viewer and run again.
	exit /b 1
)
python make_cards.py --changed-only --output all_updated_cards.pdf
if errorlevel 1 exit /b %errorlevel%

if not exist all_updated_cards.pdf (
	echo No changed cards since last generated baseline.
)
