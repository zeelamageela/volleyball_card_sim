# Simulation Quick Guide

This guide is the fastest way to run the common simulation workflows.

## 1) Setup

Windows (from project root):

```powershell
.\.venv\Scripts\python.exe --version
```

If you need dependencies:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

macOS/Linux equivalents use `python3` instead of `.venv\\Scripts\\python.exe`.

## 2) Quick PvP (player team vs player team)

Smart vs Smart, Blitz vs Grind:

```powershell
.\.venv\Scripts\python.exe main.py --mode pvp --strategy-a smart --strategy-b smart --games 1000 --seed 42
```

Random vs Random (faster baseline sanity check):

```powershell
.\.venv\Scripts\python.exe main.py --mode pvp --strategy-a random --strategy-b random --games 300 --seed 42
```

## 3) Quick PvD (player team vs dummy team)

Blitz vs Easy:

```powershell
.\.venv\Scripts\python.exe main.py --mode pvd --strategy-a smart --team-a-name Blitz --team-b-name Easy --games 1000 --seed 42
```

Blitz vs Medium:

```powershell
.\.venv\Scripts\python.exe main.py --mode pvd --strategy-a smart --team-a-name Blitz --team-b-name Medium --games 1000 --seed 42
```

Blitz vs Hard:

```powershell
.\.venv\Scripts\python.exe main.py --mode pvd --strategy-a smart --team-a-name Blitz --team-b-name Hard --games 1000 --seed 42
```

## 4) Interactive play (manual playthrough)

Play Blitz vs Medium dummy:

```powershell
.\.venv\Scripts\python.exe play.py --your-team data\team_blitz.csv --ai-team data\team_dummy_medium.csv --player-cards data\player_cards.csv --verbose
```

## 5) Balance matrix (CSV output)

Full matrix (PvP + PvD):

```powershell
.\.venv\Scripts\python.exe balance_matrix.py --mode all --games 1000 --seed 42 --output balance_matrix_results.csv
```

PvD only:

```powershell
.\.venv\Scripts\python.exe balance_matrix.py --mode pvd --games 1000 --seed 42 --output balance_matrix_pvd.csv
```

Single focused matchup:

```powershell
.\.venv\Scripts\python.exe balance_matrix.py --mode pvd --team-a Blitz --team-b Hard --games 1000 --seed 42 --output blitz_vs_hard.csv
```

## 6) CSV-driven tuning loop

1. Edit team settings in data/teams.csv.
2. Edit passives in data/team_passives.csv.
3. Edit set templates in data/set_templates.csv.
4. Edit deck compositions in data/deck_types.csv.
5. Re-run main.py or balance_matrix.py and compare results.

## 7) Quick troubleshooting

- If a roster or cards file cannot be found, run commands from the project root.
- If results look noisy, increase games to 5000-10000.
- Keep seed fixed while tuning so changes are easier to compare.

## 8) Validate against target bands (PASS/FAIL)

After you generate a matrix CSV, run:

```powershell
.\.venv\Scripts\python.exe validate_balance_targets.py --results results\season1_week2_identity_branch_passL_500g.csv
```

Use your own `--results` file path for each new tuning run.

## 9) One-command matrix + validation pipeline

Run a confidence suite over multiple seeds (example: 3 seeds, 2000 games each):

```powershell
.\.venv\Scripts\python.exe run_balance_pipeline.py --mode all --games 2000 --seeds 42 314 2718 --label week2_confidence
```

Outputs:

- Per-seed matrix CSV
- Per-seed validation report (`.validation.txt`)
- Run summary CSV

## 10) Regression guard test (Week 2 lock)

Assert locked Week 2 PvD win bands remain in range:

```powershell
.\.venv\Scripts\python.exe -m pytest tests\test_balance_regression.py -q
```

## 11) Week 3 quality loop (Easy rally quality)

Run focused PvD matrix for iteration:

```powershell
.\.venv\Scripts\python.exe balance_matrix.py --mode pvd --games 1000 --seed 42 --output results\week3_iter.csv
```

Validate target bands and quality metrics:

```powershell
.\.venv\Scripts\python.exe validate_balance_targets.py --results results\week3_iter.csv
```

## 12) Smart player card printing (changed-only)

Full rebuild of all cards:

```powershell
.\.venv\Scripts\python.exe make_cards.py --output all_player_cards.pdf
```

Changed-only reprint (uses cache):

```powershell
.\.venv\Scripts\python.exe make_cards.py --changed-only --output changed_player_cards.pdf --no-reference-cards
```

Notes:

- Cache file: `data/print_cache.json`
- Delete the cache file to force a fresh changed-only baseline run.
- Optional per-player controls in `data/player_cards.csv`:
	- `print_card=false` to skip printing
	- `skip_print=true` to skip printing
