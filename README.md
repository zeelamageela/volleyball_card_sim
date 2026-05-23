# Volleyball Card Sim

A turn-based card simulation of a volleyball match. Each rally is resolved by
players committing cards from their hand to serve, receive, set, attack, and
block. Abilities on player cards modify the outcome of each phase.

---

## Requirements

- Python 3.12 or newer
- No third-party packages — pure standard library

---

## Setup (macOS / Linux)

```bash
# 1. Clone the repo
git clone <your-repo-url>
cd volleyball_card_sim

# 2. Create a virtual environment
python3 -m venv .venv

# 3. Activate it
source .venv/bin/activate

# 4. No packages to install — you're ready
```

> On Windows use `.venv\Scripts\activate` instead, and `python` instead of `python3`.

---

## Running the simulation

### Quick start (uses default teams)
```bash
python main.py --games 1000 --seed 42 \
  --player-cards data/player_cards.csv \
  --roster-a data/team_a.csv \
  --roster-b data/team_b.csv
```

### All CLI flags
| Flag | Default | Description |
|---|---|---|
| `--games N` | 1000 | Number of games to simulate |
| `--seed N` | random | RNG seed for reproducibility |
| `--player-cards FILE` | required | CSV of player abilities |
| `--roster-a FILE` | required | Team A roster CSV |
| `--roster-b FILE` | optional | Team B roster CSV (omit for pvd mode) |
| `--mode {pvp,pvd}` | `pvp` | `pvp` = both real teams; `pvd` = Team A vs Dummy |
| `--verbose` | off | Print every rally result |

### Convenience scripts
```bash
# macOS / Linux
bash run_sim.sh

# Windows
run_sim.bat
```

### Ability/balance report
```bash
python report.py --player-cards data/player_cards.csv \
                 --roster-a data/team_a.csv \
                 --roster-b data/team_b.csv
```

### Tests
```bash
python -m pytest tests/
```

---

## Project layout

```
volleyball_card_sim/
├── main.py                  # CLI entry point
├── report.py                # Ability analysis and balance report
├── run_sim.bat              # Windows quick-run script
├── run_sim.sh               # macOS/Linux quick-run script
├── requirements.txt         # No external deps — Python 3.12+ only
├── data/
│   ├── player_cards.csv     # Master ability table (all players)
│   ├── team_a.csv           # Team A roster (player_name, role)
│   ├── team_b.csv           # Team B roster
│   └── team_dummy.csv       # Fixed dummy team for pvd mode
├── src/
│   ├── abilities.py         # Ability loading and engine
│   ├── cards.py             # Card / Deck definitions
│   ├── game.py              # Rally resolution (phase-by-phase)
│   ├── game_state.py        # Score tracking
│   ├── players.py           # Player / GridPlayer / Team
│   ├── simulation.py        # Multi-game runner
│   └── strategies.py        # RandomStrategy + DummyStrategy
└── tests/
    └── test_game.py
```

---

## Data files

### `data/player_cards.csv`

Each row is one ability for one player.

| Column | Description |
|---|---|
| `player_name` | Matches names in roster CSVs |
| `role` | `Setter / OPP / MB / OH / DS / Libero` |
| `ability_name` | Display name |
| `trigger` | When it fires (see triggers table below) |
| `condition_field` | Field checked for conditional abilities (blank = always fires) |
| `condition_value` | Threshold for the condition (e.g. `>=6`) |
| `effect` | What the ability does (see effects table below) |
| `effect_value` | Magnitude |
| `is_active` | Reserved — always `false` for now |

**Triggers:** `on_serve`, `on_set`, `on_attack`, `on_block`, `on_block_deflection`, `on_dig`, `on_dig_success`, `on_dig_failure`, `on_chase`, `on_tip`

**Effects:** `serve_value_bonus`, `set_value_delta`, `tip_threshold_delta`, `over_block_bonus`, `adjacent_block_bonus`, `pierce_block`, `seam_shot`, `roll_shot`, `dig_threshold`, `deflect_dig_threshold`, `chase_card_bonus`, `attack_value_bonus`, `single_block_only`, `hold_card`, `no_chase`, `wipe_block`

### `data/team_a.csv` / `data/team_b.csv`

```csv
player_name,role
Lancer,Setter
Cannon,OPP
Fortress,MB
Spike,OH
Hustle,DS
Hawk,Libero
```

Player names must exactly match entries in `player_cards.csv`.

---

## Card deck

28 cards with values 1–10, weighted toward the mid-range:

| Value | Count |
|---|---|
| 1 | 1 |
| 2 | 2 |
| 3 | 3 |
| 4 | 4 |
| 5 | 4 |
| 6 | 4 |
| 7 | 4 |
| 8 | 3 |
| 9 | 2 |
| 10 | 1 |

Each team draws from their own shuffled copy. Players start each turn with a
5-card hand and refill after committing attack cards.

---

## Attack lanes

| Lane | Role |
|---|---|
| 1 | OH (outside hitter / left side) |
| 2 | MB (middle blocker) |
| 3 | OPP (opposite / right side) |

The set card value determines which lanes are eligible for that rally's attack.
`SET_ELIGIBLE_LANES` in `src/game.py` maps set value → eligible lane list.

---

## Blocking model

Blocking is **attack-lane-aware**: the blocker always covers actual attack
lanes rather than guessing.

- **Double-block** (2 cards) goes on the primary threat lane.
- **Single-block** (1 card) covers any remaining attack lane.
- The attacker then chooses the least-blocked lane to commit to.

Resolution:
- `attack > block` → **Kill** (attacker scores, or dig attempted)
- `block > attack` by 0–2 → **Deflect** (soft block, dig attempted)
- `block > attack` by 3+  → **Stuffed** (blocker scores immediately)

---

## Game modes

| Mode | Description |
|---|---|
| `pvp` | Both teams use `RandomStrategy` with team-specific abilities |
| `pvd` | Team A uses `RandomStrategy`; Team B is `DummyStrategy` (no abilities, deterministic decisions) |

`DummyStrategy` always blocks the middle-priority lane, avoids the double-block
when attacking, always tips, and plays max card on serve/receive/set/dig.
