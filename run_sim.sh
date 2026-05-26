#!/usr/bin/env bash
# Mac / Linux equivalent of run_sim.bat
set -e
cd "$(dirname "$0")"

echo "=== Testing vs Easy Dummy ==="
python3 main.py \
  --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_dummy_easy.csv \
  --mode pvd --strategy-a smart

echo ""
echo "=== Testing vs Medium Dummy ==="
python3 main.py \
  --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_dummy_medium.csv \
  --mode pvd --strategy-a smart

echo ""
echo "=== Testing vs Hard Dummy ==="
python3 main.py \
  --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_dummy_hard.csv \
  --mode pvd --strategy-a smart

echo ""
echo "=== Blitz vs Grind ==="
python3 main.py \
  --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_grind.csv \
  --mode pvp --strategy-a smart --strategy-b smart
