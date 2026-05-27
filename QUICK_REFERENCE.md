# Quick Reference - Current Game State

**Last Updated:** Phase 5 Complete (May 2026)

## 🎮 Current Balance

| Metric | Value | Status |
|--------|-------|--------|
| Smart vs Easy | 58% win rate | ✓ Balanced |
| Smart vs Medium | 49-51% win rate | ✓ Competitive |
| Smart vs Hard | 53-57% win rate | ✓ Slight skill edge |
| Matching frequency | ~27% of rallies | ✓ Significant impact |
| Avg exchanges/rally | 1.85-2.00 | ✓ Tactical depth |

## 🏐 Core Game Rules

**Deck:** 28 cards (1×1, 2×2, 3×3, 4×4, 5×4, 6×4, 7×4, 8×3, 9×2, 10×1)  
**Hand Size:** 5 cards  
**Win Condition:** First to 15 points (rally scoring)  
**Positions:** Setter, OPP (opposite), MB (middle), OH (outside), DS (defensive), Libero

## 🎯 Attack System

**Set Templates:**
- Sets 1-3: Quick (lanes 1-2-3 front, max 3 attackers)
- Sets 4-5: Strong Side (lanes 1-2 front, 1-2-3 back, max 3)
- Sets 6-7: Weak Side (lanes 2-3 front, 1-2-3 back, max 3)
- Sets 8-9: High Outside (lanes 1-3 front, 1-2-3 back, **max 4**)
- Set 10: Free Choice (all lanes, **max 4**)

**Attack Resolution:**
- Attack > Block + 3 → **Kill** (dig required)
- Attack > Block (0-2 diff) → **Deflect** (tip dig)
- Block ≥ Attack → **Stuffed** (defender scores)

## 🔄 Matching System (27% of rallies)

**Blocker-Blocker Match:** 2 identical block values → **Defender wins** (~14% of rallies)

**Attacker-Blocker Match:** Attacker matches blocker → **Lane eliminated**; attacker chooses remaining lanes (~12% of rallies)

**Attacker-Attacker Match:** Front+back same value → **Defender wins** (<1% of rallies)

**Partial Match:** Multi-attacker, some match blockers → **Cards removed, continue**

**Multi-Lane:** Processed high-to-low; if all lanes are eliminated, **Defender wins**

## ⚡ Active Abilities (50+ total)

**Phase 5 Additions:**
- `FORCE_HIGH_BLOCK`: Ignore blocks ≤ threshold
- `DECK_SWAP_OPPONENT`: Replace opponent's highest card on dig
- `WILD_BLOCK`: Low cards block any lane (not in strategy yet)

**Phase 4 Core:**
- `SLIDE_LANES`: Shift after blocks revealed
- `BACK_ROW_PIERCE`: Ignore blocks on back-row attacks
- `MIN_BLOCKER_ONLY`: Only min block card counts

**Classic Effects:**
- Value bonuses (attack, block, serve, set, dig, chase)
- Thresholds (dig, deflect, tip)
- Pierce effects (block bypass)
- Special shots (wipe, roll, seam)
- Card management (hold, exchange)

## 📊 Typical Scoring (200 games)

| Method | Frequency | Winner |
|--------|-----------|---------|
| Stuffed blocks | 48-50% | Attacker |
| Blocker-blocker match | 14-15% | Defender |
| Tips not dug | 12-13% | Attacker |
| Single deflections | 12-13% | Attacker |
| Kills (chase failed) | 1.5-2% | Attacker |
| Other | <2% | Varies |

## 🤖 AI Strategies

**RandomStrategy:**
- Random legal decisions
- 1% win rate vs dummy
- Baseline comparison

**SmartStrategy:**
- Odd/even lane logic
- Setter targeting
- Tactical matching awareness
- 50-60% win rate vs dummy

**DummyStrategy:**
- Easy: Basic decisions
- Medium: Improved blocking
- Hard: Advanced positioning

## 📂 Key Files

**Core Game Logic:**
- `src/game.py`: Rally execution, matching system (lines 167-270)
- `src/players.py`: Roles, set templates
- `src/abilities.py`: 50+ ability definitions

**Strategy AI:**
- `src/strategies.py`: Random, Smart, Dummy implementations

**Data:**
- `data/player_cards.csv`: 50+ players with abilities
- `data/team_*.csv`: Various test rosters
- `data/team_phase5.csv`: Latest ability showcase

**Documentation:**
- `SESSION_NOTES.md`: Complete Phase 5 development details
- `CHANGELOG.md`: Version history
- `README.md`: Project overview

## 🧪 Quick Test Commands

```bash
# Standard balance test
python3 main.py --games 200 --mode pvd --strategy-a smart \
  --roster-a data/team_test1.csv \
  --roster-b data/team_dummy_medium.csv \
  --player-cards data/player_cards.csv

# Phase 5 abilities
python3 main.py --games 100 --mode pvd --strategy-a smart \
  --roster-a data/team_phase5.csv \
  --roster-b data/team_dummy_hard.csv \
  --player-cards data/player_cards.csv

# Reproducible test
python3 main.py --games 100 --seed 12345 --mode pvd --strategy-a smart
```

## 🚧 Known Limitations

1. WILD_BLOCK defined but not used by strategy AI
2. SmartStrategy doesn't predict matching scenarios
3. No EXCHANGE_CARD ability yet (was proposed)
4. Limited PvP testing (mostly PvD focus)

## 🎯 Recommended Next Steps

**High Priority:**
- Implement WILD_BLOCK in SmartStrategy
- Add matching prediction/exploitation
- Create more Phase 5 test teams

**Medium Priority:**
- EXCHANGE_CARD ability if desired
- Balance tuning for FORCE_HIGH_BLOCK
- Ability trigger statistics tracking

**Low Priority:**
- Multi-lane combo bonuses
- Additional matching patterns
- Advanced chase mechanics

---

**System Status:** ✅ Stable, tested, production-ready  
**Performance:** ~50-100 games/second  
**Test Coverage:** 1,500+ games simulated  
**Last Major Update:** Phase 5 Comprehensive Matching System
