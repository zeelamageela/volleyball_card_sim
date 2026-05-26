# Changelog

## Phase 5 - Comprehensive Matching System (May 2026)

### Added
- **Comprehensive card matching system** resolving ~27% of rallies
  - Blocker-blocker matches (2 identical blockers) → Defender wins
  - Single attacker deflections (attacker matches blocker) → Attacker wins
  - Front+back attacker matches (2 attackers same value) → Defender wins
  - Partial lane matching (multi-attacker scenarios)
  - Multi-lane processing (high-to-low by attack value)

- **New Abilities**
  - `FORCE_HIGH_BLOCK`: Ignore blocks ≤ threshold (Titan, Breaker)
  - `DECK_SWAP_OPPONENT`: Replace opponent's highest card on dig success (Thief)
  - `WILD_BLOCK`: Low-value cards can block any lane (Flex, Shield) - defined, not in strategy yet

- **New Players** (data/player_cards.csv)
  - Flex (MB): wild_block ≤4, block_value_bonus
  - Shield (MB): wild_block ≤5, adjacent_block_bonus
  - Titan (OPP): force_high_block ≤5, attack_value_bonus
  - Breaker (OPP): force_high_block ≤6, over_block_bonus
  - Thief (DS): deck_swap_opponent, chase_card_bonus

- **New Test Roster** (data/team_phase5.csv)
  - Showcases Phase 5 abilities: Conductor, Titan, Flex, Quantum, Thief, Mirror

### Changed
- **Increased attacker capacity** in set templates
  - Sets 1-7: max_attackers 2-3 → **3**
  - Sets 8-10: max_attackers 3 → **4**

- **Enhanced statistics output** (src/simulation.py)
  - Changed "Top rally endings (5)" to "Rally endings (all types)"
  - Now shows complete breakdown of all scoring methods

- **Matching logic in game.py** (lines 167-270)
  - Replaced simple front+back matching with comprehensive system
  - Added context-based winner determination
  - Implemented lane prioritization (high to low)
  - Track last match result for multi-lane scenarios

### Balance Impact
- Win rates vs dummy AI: 85-93% → **50-60%** (improved competitiveness)
- Matching frequency: 0% → **27%** of rallies
- Average exchanges/rally: 2.0-2.4 → **1.85-2.0** (more decisive)
- Scoring diversity increased (8+ rally-ending types common)

### Testing
- 1,500+ games simulated across 8 test configurations
- No crashes or stability issues
- Performance: ~50-100 games/second

### Files Modified
- `src/game.py`: Comprehensive matching implementation, ability triggers
- `src/players.py`: Increased max_attackers in set templates
- `src/abilities.py`: Added Phase 5 ability constants and methods
- `src/simulation.py`: Enhanced statistics output
- `data/player_cards.csv`: Added 6 new Phase 5 players
- `data/team_phase5.csv`: Created Phase 5 test roster

### Known Issues
- WILD_BLOCK ability defined but not used by SmartStrategy yet
- SmartStrategy doesn't actively predict/exploit matching scenarios
- No EXCHANGE_CARD ability (was proposed but not implemented)

---

## Phase 4 - Tactical Abilities (Prior to Phase 5)

### Added
- SLIDE_LANES: Shift to adjacent lane after blocks revealed
- BACK_ROW_PIERCE: Back-row attacks ignore blocks
- MIN_BLOCKER_ONLY: Only minimum block card counts
- Tactical matching detection in SmartStrategy
- Broken play mechanics (non-setter sets)

### Players
- Conductor (Setter): slide_lanes, set_value_delta
- Quantum (OH): min_blocker_only, attack_value_bonus
- Mirror (Libero): adjacent_block_bonus

---

## Phase 3 - Strategy AI (Prior to Phase 4)

### Added
- SmartStrategy: Tactical decision-making with odd/even logic
- DummyStrategy difficulty tiers (Easy, Medium, Hard)
- Setter targeting and protection
- Multi-lane pressure tactics

---

## Phase 2 - Ability System (Prior to Phase 3)

### Added
- 50+ player abilities with trigger-condition-effect pattern
- AbilityEngine for team-level ability management
- Conditional abilities (attack_card_value, hand_size, etc.)
- player_cards.csv ability database

---

## Phase 1 - Core Mechanics (Initial Release)

### Added
- Rally-based volleyball simulation
- 6 player positions (Setter, OPP, MB, OH, DS, Libero)
- Set template system (10 values determine attack options)
- 28-card deck with weighted distribution
- Attack resolution (Kill, Deflect, Stuffed)
- Dig and chase mechanics
- Multi-game simulation with statistics
