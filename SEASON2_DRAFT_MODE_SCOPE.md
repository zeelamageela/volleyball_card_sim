# Season 2 Draft Mode Scope

Status: Active scope. Not fully implemented yet.

## Objective

Build a pre-match ability draft mode that layers on top of the Season 1 engine and increases replayability, theme identity, and decisive rally outcomes.

## Core Season 2 decisions (locked)

1. Teams are preconstructed.
2. Teams draft abilities, not full player rosters.
3. No new trigger timing categories will be added.
4. Effects must stay one sentence and use existing trigger windows.

## Theme direction (Season 2 identity)

The game theme is Robot/Cyborg/Alien Monster Volleyball.

Primary categories:
1. Robot: precision, targeting, deterministic boosts.
2. Cyborg: adaptation, hand/deck manipulation, conversion effects.
3. Alien Monster: chaos, mutation, high-risk spikes.
4. Phantom: deception, feints, lane misdirection, forced bad commits.

Note: Chrono (time) effects can exist as a rare Phantom subtype, but not as a separate fourth pillar in alpha.

## Design goals for fun and pace

1. Increase decisive attack wins and clutch moments.
2. Reduce prolonged low-impact rallies.
3. Preserve strategic choices while shortening average rally length.
4. Keep all draft effects readable and fast to resolve.

## Non-goals

1. No new inside-loop rally phases.
2. No team-specific engine branch logic.
3. No hidden state that blocks deterministic replay.
4. No text-heavy or multi-step effects that slow turns.

## Draft format for Season 2 alpha

1. Draft type: Snake draft.
2. Shared pool size: 24 abilities total.
3. Bans: Optional, 1 ban per side before picks.
4. Picks per team: 4-6 (start with 4 for faster testing).
5. Duplicates: No duplicate of same ability on one team.

## Slot and budget constraints (recommended alpha)

Use both slot gating and budget gating to prevent single-strategy stacking.

1. Slot model (per team):
- 1 Setter-support slot
- 1 Attack-finisher slot
- 1 Block/control slot
- 1 Flex slot

2. Budget model (per team):
- Total budget: 8 points
- Cost tiers: 1, 2, 3
- Rule: at most one cost-3 finisher

## Ability family targets (pool construction)

To speed up play and increase battle resolution, bias the pool toward decisive effects.

1. Finisher (attack-closing): 30-35%
2. Tempo (short one-action bursts): 20-25%
3. Disruption (force weak commits): 15-20%
4. Conversion (defense-to-offense): 15-20%
5. Risk/reward (high drama): 10-15%

Rule of thumb: Prefer one-action burst effects over persistent rally-long stacking.

## Phantom category guidelines

Phantom is the fourth category for alpha and should focus on deception without adding heavy rules.

Approved Phantom pattern examples:
1. Lane feint or post-commit lane shift (lightweight).
2. One-card visibility disruption (peek, mask, or forced commit).
3. Single-lane blocker degradation.

Avoid in alpha:
1. Full hand reveal effects.
2. Multi-step rewinds.
3. State tracking across multiple rallies.

## Data model updates

Use the existing draft files and add ability-oriented fields.

1. data/draft_pool.csv
- ability_id
- ability_name
- category_tag (robot/cyborg/alien/phantom)
- family_tag (finisher/tempo/disruption/conversion/risk)
- role_slot (setter/attack/block/flex)
- trigger
- effect_summary
- cost_tier
- rarity_bucket
- enabled

2. data/draft_rules.csv
- rule_set
- pool_size
- bans_per_team
- pick_order
- picks_per_team
- budget_total
- max_cost3_finishers
- slot_setter
- slot_attack
- slot_block
- slot_flex
- allow_duplicates_team

Current prototype artifacts:
- data/draft_pool.csv
- data/draft_rules.csv
- DRAFT_PLAYTEST_GUIDE.md
- results/draft_playtest_session_TEMPLATE.md

## Integration constraints

1. Preserve data/player_cards.csv as the source-of-truth for runtime ability definitions.
2. Draft output must resolve into standard team load format used by the current CLI.
3. Draft phase outputs must be deterministic with fixed seed and saved artifacts.

## What to test next (ordered)

1. Draft reproducibility
- Same seed and ruleset produce identical draft outputs.

2. Draft legality
- Slot constraints and budget constraints enforce correctly.

3. Runtime wiring smoke
- Drafted ability load can run full game simulations with no branch-only code path.

4. Pace impact
- Compare average exchanges per rally and average rally duration versus Season 1 lock baseline.
- Target: 10-20% fewer exchanges while keeping strategy depth.

5. Decisive outcome impact
- Track attack-ending share and contested battle resolution rate.
- Verify that faster endings come from tactical abilities, not random blowouts.

6. Fun/readability playtest
- Short session survey after each draft set:
  - Were abilities easy to understand?
  - Did draft picks feel meaningful?
  - Were there memorable swing moments?

## Initial alpha build plan

1. Define 24-ability alpha pool (8 Robot, 8 Cyborg, 8 Alien/Monster, 6 of these also tagged Phantom-capable patterns).
2. Apply slot+budget metadata in data/draft_pool.csv.
3. Configure s2_alpha_snake_24 in data/draft_rules.csv.
4. Run manual draft sessions and export to data/team_draft_a.csv and data/team_draft_b.csv.
5. Run matrix and focused matchup simulations, then record in results/draft_playtest_session_TEMPLATE.md copies.

## Continuation checklist

1. Finalize first 24 abilities and costs.
2. Lock alpha draft ruleset and publish one-page quick draft reference.
3. Add draft output validator script (legality and reproducibility).
4. Add a compact draft telemetry report (pick rates, win rates by ability, category diversity).
5. Decide whether to keep optional bans for beta or move to always-on bans.

## Open questions

1. Should alpha use 4 picks or 5 picks per team as default?
2. Should optional bans be enabled in all tests or only in competitive tests?
3. Should Chrono subtype effects appear in alpha pool at all, or wait for beta?
