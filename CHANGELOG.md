# Changelog

## Locked Ruleset Rewrite (September 2026)

Rewrote the core resolution engine to match a newly locked, deliberately simplified ruleset — the previous system (below, Phase 5 and earlier) had gone deep enough on balance/complexity that it stopped being fun to play. This is a rules simplification, not a balance pass; PvD win rates are known to be off the old target bands and are being deliberately deprioritized for now.

### Retired
- **Card-matching cancellation system** (blocker-blocker, attacker-attacker, attacker-blocker lane elimination) — removed entirely, no replacement mechanic.
- **Three-tier attack resolution** (kill / soft-deflect / hard-deflect / stuffed by margin) — replaced by a two-tier kill/stuffed split with deflection only on an exact numeric tie.
- **Two-tier chase** (ARMED_ATTACK single-lane exposed attack, then FREE_BALL) — collapsed into one chase mechanic; success means different things depending on context (receive vs. dig failure).
- **Per-team normal set-template CSV bundles** (`data/set_templates.csv`, `set_type=normal`) — replaced by one universal 3-tier template for every team. Broken-play templates are untouched (still per-team, still an open question).
- **Quickset-specific no-chase rule** — replaced by a more general "unblocked hit can't be chased" rule.

### Changed
- Deck: flat 40 cards (4 copies each of Ace(1)-10), replacing the old skewed 28-card standard deck.
- Tip threshold raised from <=3 to <=5; tip now checks against the lane's single lowest blocker card instead of the full block total.
- Added the "combo" mechanic for two attackers sharing one lane: declared resolve order, stuffed-then-remove-highest-blocker-then-resolve-second-card.

### Added
- `src/mats.py` / `list_mats.py` — an explicit `Mat` object (6 fixed seats, each with a player + at most one ability) for the 4 pickable team identities (Blitz, Grind, Spread, Backline), built from the existing roster/ability CSVs.

### Discovered (not fixed, worth knowing)
- `data/player_cards.csv`'s `is_active: false` does not mean an ability is disabled — it means "passive, fires automatically." All 52 current ability rows are live in every game today, despite reading like a placeholder/inactive value.

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
