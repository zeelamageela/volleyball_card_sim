# Phase 6 Session Notes

**Date:** May 26, 2026
**Status:** Phase 6 Complete — Dummy Teams, Ability Cards PDF, Balance Calibration

---

## Session Goals Achieved

1. **Blitz vs Grind PvP balance confirmed** — 54.2% Blitz wins (target ~50–57%) ✅
2. **3 dummy team tiers designed and calibrated** (Easy / Medium / Hard) ✅
3. **Printable ability card PDF generator created** (`make_cards.py`) ✅
4. **Set template reference cards added to PDF** ✅

---

## New Files This Session

| File | Purpose |
|------|---------|
| `make_cards.py` | Generates `ability_cards.pdf` — 9 cards per 8.5×11 page |
| `ability_cards.pdf` | 30 ability cards + 5 set template cards, 4 pages total |
| `data/team_dummy_easy.csv` | Easy tier dummy roster |
| `data/team_dummy_medium.csv` | Medium tier dummy roster |
| `data/team_dummy_hard.csv` | Hard tier dummy roster |

> **Dependency added:** `reportlab` — install with:
> ```
> .venv\Scripts\pip install reportlab
> ```

---

## Teams Overview

### Player Teams (PvP)
| Team | File | Archetype |
|------|------|-----------|
| Blitz | `team_blitz.csv` | Attack / Power |
| Grind | `team_grind.csv` | Defense / Grind |

### Dummy Teams (PvD — used with DummyStrategy, blind play)
| Team | File | Difficulty |
|------|------|-----------|
| Easy | `team_dummy_easy.csv` | ~75% player win rate |
| Medium | `team_dummy_medium.csv` | ~57% player win rate |
| Hard | `team_dummy_hard.csv` | ~45% player win rate |

---

## Calibrated Win Rates (10,000 games, seed 42, Smart vs Dummy)

| Tier | Blitz wins | Grind wins | Avg | Target |
|------|-----------|-----------|-----|--------|
| Easy | 76.5% | 71.3% | 73.9% | 75% |
| Medium | 60.5% | 57.9% | 59.2% | 57% |
| Hard | 44.7% | 36.6% | 40.7% | 45% |

**Key design insight:** SmartStrategy vs DummyStrategy gap ≈ 40 percentage points.
Even Blitz-level abilities only give the dummy team a 16% win rate.
Dummy team abilities must be significantly overpowered to compensate for blind play.
`adjacent_block_bonus` is the most effective lever for blind dummies — covers lanes Smart players route to.

---

## All 30 Player Cards (`data/player_cards.csv`)

### Blitz (attack archetype)
| Player | Role | Ability | Trigger | Effect |
|--------|------|---------|---------|--------|
| Sarge | Setter | Command Set | on_set, card ≥6 | set_value_delta +2 |
| Strike | OPP | Power Arm | on_attack, card ≥7 | over_block_bonus +3 |
| Ram | MB | Front Wall | on_block | block_value_bonus +2 |
| Blaze | OH | Drive Shot | on_attack, card ≥8 | roll_shot |
| Dash | DS | Hot Pursuit | on_dig_failure | chase_card_bonus +3 |
| Reed | Libero | Sure Hands | on_dig | dig_threshold +1 |

### Grind (defense archetype)
| Player | Role | Ability | Trigger | Effect |
|--------|------|---------|---------|--------|
| Echo | Setter | Float Serve | on_serve | serve_value_bonus +2 |
| Lance | OPP | Needle | on_attack, card ≥8 | pierce_block (ignore 1 blocker card) |
| Shield | MB | Wide Net | on_block | adjacent_block_bonus +2 |
| Drift | OH | Heavy Topspin | on_attack, card ≥7 | attack_value_bonus +2 |
| Scoop | DS | Safety Net | on_dig_failure | chase_card_bonus +2 |
| River | Libero | Clean Pass | on_dig_success (normal) | set_value_delta +2 |

### Easy Dummy
| Player | Role | Ability | Trigger | Effect |
|--------|------|---------|---------|--------|
| Breeze | Setter | Soft Set | on_set, card ≥5 | set_value_delta +2 |
| Clutch | OPP | Lucky Swing | on_attack, card ≥7 | over_block_bonus +5 |
| Anchor | MB | Steady Block | on_block | adjacent_block_bonus +2 |
| Grit | OH | Hustle | on_attack, card ≥7 | roll_shot |
| Scrap | DS | Scramble | on_dig_failure | chase_card_bonus +3 |
| Hope | Libero | Safe Hands | on_dig | dig_threshold +1 |

### Medium Dummy
| Player | Role | Ability | Trigger | Effect |
|--------|------|---------|---------|--------|
| Vex | Setter | Precision Set | on_set, card ≥5 | set_value_delta +3 |
| Flip | OPP | Power Cut | on_attack, card ≥7 | over_block_bonus +5 |
| Shift | MB | Block Shift | on_block | adjacent_block_bonus +4 |
| Trickster | OH | Snap Shot | on_attack, card ≥7 | roll_shot |
| Snatch | DS | Quick Save | on_dig_failure | chase_card_bonus +4 |
| Mirror | Libero | Reflect Pass | on_dig | dig_threshold +2 |

### Hard Dummy
| Player | Role | Ability | Trigger | Effect |
|--------|------|---------|---------|--------|
| Conductor | Setter | Command Rally | on_set (unconditional) | set_value_delta +4 |
| Bandit | OPP | Overpower | on_attack, card ≥6 | over_block_bonus +6 |
| Atlas | MB | Iron Wall | on_block | adjacent_block_bonus +5 |
| Shadow | OH | Phantom Shot | on_attack, card ≥6 | roll_shot |
| Surge | DS | Wave Break | on_dig_failure | chase_card_bonus +6 |
| Vault | Libero | Iron Hands | on_dig | dig_threshold +2 |

---

## Run Commands

### PvP (player vs player)
```
.\.venv\Scripts\python.exe main.py --mode pvp --strategy-a smart --strategy-b smart --games 10000 --seed 42 --player-cards data/player_cards.csv --roster-a data/team_blitz.csv --roster-b data/team_grind.csv
```

### PvD (player vs dummy)
```
.\.venv\Scripts\python.exe main.py --mode pvd --strategy-a smart --games 10000 --seed 42 --player-cards data/player_cards.csv --roster-a data/team_blitz.csv --roster-b data/team_dummy_easy.csv
```
Change `team_blitz` → `team_grind` and `team_dummy_easy` → `team_dummy_medium` / `team_dummy_hard` as needed.

### Generate ability cards PDF
```
.\.venv\Scripts\python.exe make_cards.py --output ability_cards.pdf
```
Optional filter (e.g. for a single match): `--teams Blitz Grind`

---

## make_cards.py Overview

- Reads `data/player_cards.csv` + all roster CSVs to assign team membership
- Outputs 9 cards per 8.5×11 page (3 cols × 3 rows), 0.45" margins, 7pt gutters
- Card layout: player name + role header, ability name, description text
- Last page appends one **set template reference card** per team (shows normal set table, broken play table, lane key)
- Currently no fills (prototype/B&W mode) — to re-enable fills, restore the two `roundRect`/`rect` calls in `draw_card()` using `TEAM_COLORS`

---

## Set Templates (universal — same for all teams)

### Normal Set (setter sets)
| Card | Front lanes | Back row | Max attackers |
|------|-------------|----------|---------------|
| 1–3 | OH · MB · OPP | — | 3 |
| 4–5 | OH · MB | OH/MB/OPP | 3 |
| 6–7 | MB · OPP | OH/MB/OPP | 3 |
| 8–9 | OH · OPP | OH/MB/OPP | 4 |
| 10 | OH · MB · OPP | OH/MB/OPP | 4 |

### Broken Play (non-setter sets — triggered when setter dug the ball)
| Card | Front lanes | Back row | Max attackers |
|------|-------------|----------|---------------|
| 1–3 | OH · MB | MB only | 2 |
| 4–7 | OH · OPP | MB only | 1 |
| 8–10 | MB · OPP | MB only | 2 |

Lane key: Lane 1 = OH · Lane 2 = MB · Lane 3 = OPP

---

## Known Issues / Future Work

- **Set template card line spacing**: The horizontal dividers on the set template cards slightly crowd the text below them. Could be fixed by adjusting `cur_y` offsets in `draw_set_template_card()` in `make_cards.py`.
- **Hard tier balance**: Hard dummy avg win rate is 59.3% (target was 55%). Grind vs Hard is notably tougher (63.4% dummy wins) vs Blitz vs Hard (55.3% dummy wins). Could tune by reducing `Atlas` `adjacent_block_bonus` from +5 → +4, or `Surge` chase from +6 → +5.
- **Team-specific set templates**: Discussed but deferred — would require changes to `src/players.py` and full recalibration.
- **Interactive play**: `src/interactive.py` exists but hasn't been tested this session.
- **README.md**: Still references old setup (no mention of `reportlab`, dummy teams, or `make_cards.py`).

---

## Design Philosophy (preserve for future sessions)

1. **One ability per player** — each of the 30 cards has exactly one passive ability
2. **Single-trigger design** — abilities fire on one event (on_set, on_attack, on_block, on_dig, on_dig_failure, on_serve)
3. **Dummy teams intentionally overpowered** — DummyStrategy plays blind; abilities compensate for ~40pp strategy gap
4. **adjacent_block_bonus is the blind dummy's best tool** — covers lanes Smart players route to
5. **Set templates are universal** — same rules for all teams; differentiation comes from ability cards only
