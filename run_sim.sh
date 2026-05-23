#!/usr/bin/env bash
# Mac / Linux equivalent of run_sim.bat
set -e
cd "$(dirname "$0")"

.venv/bin/python main.py \
  --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_a.csv \
  --roster-b data/team_b.csv

echo ""

.venv/bin/python report.py \
  --player-cards data/player_cards.csv \
  --roster-a data/team_a.csv \
  --roster-b data/team_b.csv
