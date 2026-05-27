# Phase 6 Session Notes

**Date:** May 26, 2026
**Status:** Phase 6 Complete — Dummy Teams, Ability Cards PDF, Balance Calibration

---

## Phase 7 Continuation Handoff (May 27, 2026)

### What was completed this session

1. Team passive abilities were implemented in game flow and team model:
- Deep Bench (Grind): 6-card hand size support
- Safe Setter (Easy): setter digs do not force broken play
- Back Court Threat (Medium): back-row attacks ignore first blocker
- Elite Draw (Hard): draw 2, keep higher for action draws

2. Passive support plumbing was added:
- Team now accepts passive_ability and exposes draw_for_action()
- Rally action draw paths now use draw_for_action() (serve/receive/set/dig/block/attack blind draws)

3. Card generation was expanded to include players with no abilities:
- make_cards now loads all roster players first, then overlays ability rows
- no-ability players now render with "No special abilities"
- this increased player cards from 17 to 30

4. Team template cards now print passive text:
- passive block appears under set template tables for non-TBD teams

5. Major deck redesign was finalized and validated:
- Engine supports deck_type selection (standard vs dummy).
- Standard deck remains baseline for hand-play teams.
- Dummy deck is intentionally top-heavy for blind-play teams.
- Current dummy counts in code: 1x2, 2x2, 3x2, 4x3, 5x2, 6x2, 7x4, 8x5, 9x4, 10x2.
- Current distribution target for dummy deck: Low 32% / Mid 14% / High 54%.

6. Template-card formatting was refined after this handoff was drafted:
- Set rows now use explicit lane columns instead of a compact front/back list.
- Set names and quickset target notation are shown per row.
- A separate deck makeup reference card was added to the printable reference set.

### Files changed in this continuation

- src/players.py
- src/game.py
- make_cards.py
- data/team_passives.csv
- data/set_templates.csv
- data/teams.csv
- test_passives.py
- test_balance_passives.py

### Artifacts generated

- ability_cards.pdf (updated default output)
- ability_cards_with_passives.pdf
- all_player_cards.pdf

### Validation run summary

1. Passive smoke test script:
- command: python3 test_passives.py
- result: all four passive checks executed successfully

2. Balance comparison script:
- command: python3 test_balance_passives.py
- result snapshot (100 games each, Blitz vs dummy):
  - Easy: 42% no passive vs 40% with Safe Setter
  - Medium: 55% no passive vs 55% with Back Court Threat
  - Hard: 92% no passive vs 89% with Elite Draw

### Important observations

1. Passives currently have low or mixed impact on win rates in sampled runs.
2. Elite Draw appears to improve single-draw quality, but did not improve macro win rate in the 100-game sample.
3. Make-cards behavior now aligns with physical-print needs: all rostered players get a card.
4. Deck composition is now a primary balance tool (not only abilities), especially for dummy teams.

### Ready-to-run commands next session

- python3 make_cards.py
- python3 make_cards.py --output all_player_cards.pdf
- python3 test_passives.py
- python3 test_balance_passives.py

### Suggested first tasks next session

1. Run larger balance samples (1000-10000 games) before tuning passives.
2. Decide whether passive effects should be stronger or replaced with more structural effects.
3. Decide Blitz passive (currently TBD in team_passives.csv and teams.csv).
4. If desired, wire CSV-driven team/passive loading in main/play/report so passives are not only test-script configured.

### Open decision points

1. Keep current passive strengths for flavor, or tune for measurable balance effect.
2. Confirm whether legacy deleted roster/test CSV files in data/ are intentional before cleanup.

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

---

## Post-Phase 6 Rule Change: Matching System Revision

**Date:** May 25, 2026  
**Issue:** Discovered during physical playtest that attacker-blocker matching rules were completely wrong in code

### Old Rule (incorrect):
- Attacker matches blocker → **Immediate winner determined**
  - Multiple lanes armed → Attacker wins (deflection out)
  - Single lane armed → Defender wins (stuffed)

### New Rule (correct for physical play):
- Attacker matches blocker → **Lane is ELIMINATED (cannot be chosen)**
  - Attacker must choose from remaining non-matched lanes
  - If ALL lanes matched → Defender wins automatically

### Example from Physical Playtest:
**Setup:** Blitz arms lanes 1,2,3 with cards 7,4,3. Easy Dummy blocks 7,7,2.
- Lane 1: 7 vs 7 → **MATCH → Eliminated**
- Lane 2: 4 vs 7 → Would be stuffed (available)  
- Lane 3: 3 vs (2+2 adjacent) = 4 → Would be stuffed (available)
- **Result:** Player chooses Lane 3, gets stuffed

### Code Changes:
- Modified [src/game.py](src/game.py) lines 318-335: Attacker-blocker match removes lane from attack_cards
- Removed deflection-dig mechanic that was incorrectly applied to matches
- Added proper tracking via last_match_result for "all lanes eliminated" outcome
- Updated [QUICK_REFERENCE.md](QUICK_REFERENCE.md): Matching system rules section rewritten

### Balance Impact:
- Win rates stable (Blitz still ~76% vs Easy Dummy)
- ~12% of rallies end with "All lanes eliminated" (all matches)
- Matches now create **tactical lane elimination** rather than deflections or instant points
- Attackers must plan for reduced lane availability
- Smart blocking can force bad lane choices by matching preferred lanes

### Design Rationale:
Matching as elimination creates cleaner physical gameplay:
1. No complex dig resolution needed for matches — cards simply removed
2. Forces attackers to consider multiple viable lanes when arming
3. Rewards blockers for reading correctly without instant scoring
4. Creates tactical risk/reward: match your best lane = must attack weaker lane
5. Intuitive for physical card play: matched cards physically set aside, can't be used

---

## Post-Phase 6 Rule Addition: Dummy Double-Block Rule

**Date:** May 25, 2026  
**Issue:** Dummy blocking logic for physical play was not implemented in code

### Physical Play Rule for Dummy Blocking:

When the dummy team blocks **2 attacked lanes** with **3 total blockers**:

1. **Count the parity** of the dummy's hand cards (odd vs even)
2. **Majority EVEN** → Double block the **rightmost** (higher lane number), single block leftmost
3. **Majority ODD or tie** → Double block the **leftmost** (lower lane number), single block rightmost

### Example:
**Attacker arms:** Lane 1 (OH) and Lane 2 (MB)  
**Dummy hand:** [2, 4, 6, 8, 9] → 4 even, 1 odd → **EVEN majority**

**Result:**
- Lane 2 (rightmost): Double block [2, 4]
- Lane 1 (leftmost): Single block [6]

**If hand was [3, 5, 7, 8, 9]** → 3 odd, 2 even → **ODD majority**

**Result:**
- Lane 1 (leftmost): Double block [3, 5]
- Lane 2 (rightmost): Single block [7]

### Code Changes:
- Modified [src/strategies.py](src/strategies.py) lines 288-330: `DummyStrategy.choose_block_cards()`
- Now implements full parity-based blocking logic
- Handles 1, 2, or 3 attacked lanes appropriately
- 2 lanes → 3 blockers (double + single)
- 3 lanes → 3 blockers (one per lane)

### Balance Impact:
- Dummy now presents more varied blocking patterns
- Smart strategy can still exploit by tracking hand parity
- Creates realistic physical play experience where dummy doesn't just stack one lane
- Win rates remain stable: Blitz 76.6% vs Easy (1000 games, seed 42) — essentially unchanged
- Matching frequency slightly increased due to more distributed blocking
