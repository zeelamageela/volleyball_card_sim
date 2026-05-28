# Volleyball Card Sim

Volleyball card-game simulation engine with AI strategies, player abilities, passive team effects, and printable card generation.

## Reference hierarchy

Use this order when information conflicts:
1. Engine behavior in `src/`
2. CSV docs you edit (`data/set_templates.csv`, `data/teams.csv`, `data/team_passives.csv`, `data/deck_types.csv`)
3. Session notes and historical docs

## Requirements

- Python 3.12+
- Optional: `reportlab` for `make_cards.py`

Install dependencies:

```bash
python -m pip install -r requirements.txt
```

## Common commands

Simulate PvP (Blitz vs Grind):

```bash
python main.py --mode pvp --strategy-a smart --strategy-b smart --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_grind.csv
```

Simulate PvD (Blitz vs Easy Dummy):

```bash
python main.py --mode pvd --strategy-a smart --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_blitz.csv \
  --roster-b data/team_dummy_easy.csv
```

Interactive game:

```bash
python play.py --your-team data/team_blitz.csv --ai-team data/team_dummy_medium.csv
```

Generate printable cards PDF:

```bash
python make_cards.py --output ability_cards.pdf
```

Run full balance matrix (CSV output):

```bash
python balance_matrix.py --games 1000 --seed 42 --mode all --output balance_matrix_results.csv
```

Passive checks:

```bash
python test_passives.py
python test_balance_passives.py
```

## Data files

- `data/player_cards.csv`: player abilities
- `data/team_blitz.csv`: Blitz roster
- `data/team_grind.csv`: Grind roster
- `data/team_dummy_easy.csv`: Easy dummy roster
- `data/team_dummy_medium.csv`: Medium dummy roster
- `data/team_dummy_hard.csv`: Hard dummy roster
- `data/teams.csv`: team metadata doc
- `data/team_passives.csv`: passive definitions doc
- `data/set_templates.csv`: set template definitions doc
- `data/deck_types.csv`: deck composition definitions by `deck_type`

## Current notes

- Master roadmap and progress tracker: SEASON1_MASTER_PLAN.md
- Quick run commands: SIMULATION_QUICK_GUIDE.md
- Season roadmap checkpoint: SEASON1_WEEK1.md
- Week 2 closeout: SEASON1_WEEK2.md
- Week 3 active checkpoint: SEASON1_WEEK3.md
- Team identity checklist: TEAM_IDENTITY_CHECKLIST.md
- Identity roster proposal: IDENTITY_ROSTER_PROPOSAL.md
- Identity experiment notes: results/season1_week2_identity_notes.md
- Quick rules reference: `QUICK_REFERENCE.md` (aligned to engine behavior)
- Physical flow reference: `PHYSICAL_PLAY_REFERENCE.md`
- Historical handoff logs: `SESSION_NOTES.md`, `SESSION_NOTES_phase6.md`

## Historical context

Older assumptions and phased writeups are preserved in session/changelog files for evolution tracking. Treat them as historical unless they match the current engine.
