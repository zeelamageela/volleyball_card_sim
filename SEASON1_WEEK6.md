# Season 1 - Week 6 Content Expansion Sprint 2

Date: 2026-05-28

## Week 6 objective

Stabilize newly added teams and close remaining out-of-band rows from Week 5 waiver while keeping team identities distinct.

## Starting point

Reference files:
- results/season1_week5_microedit_focus_seed42_1000g_2026-05-28.csv
- results/week5_waiver_2026-05-28.md
- data/player_cards.csv
- data/balance_targets.csv

## Carry-forward gaps

- Spread vs Easy slightly high.
- Spread vs Hard high.
- Backline vs Hard high.

## Week 6 kickoff baseline (seed 42, 1000g)

Artifacts:
- results/week6_kickoff_baseline_seed42_1000g.csv
- results/week6_kickoff_baseline_seed42_1000g.validation.txt

Focused row status:
- Spread vs Easy: 80.9 (FAIL, high)
- Spread vs Medium: 54.3 (PASS)
- Spread vs Hard: 38.7 (FAIL, high)
- Backline vs Easy: 78.4 (PASS)
- Backline vs Medium: 59.4 (PASS)
- Backline vs Hard: 37.6 (FAIL, high)
- Spread vs Backline: 49.2 (PASS)
- Backline vs Spread: 48.6 (PASS)

Quality check:
- Avg exchanges in band for all 8 focused rows.

## Week 6 acceptance checklist

- [ ] Reach minimum 6-team ecosystem in active matrix coverage.
- [ ] No extreme out-of-band matchup without explicit reason.
- [ ] Resolve Week 5 waiver rows or replace with updated approved waiver.

## Execution loop

1. Apply one micro-adjustment at a time.
2. Run focused matrix slices for changed rows.
3. Validate against expanded targets.
4. Record artifact paths and decision in this file.

## Immediate Week 6 priority

1. Tune Hard-pressure interactions first (shared gap for both Spread and Backline).
2. Re-run the same focused 8-row slice before touching non-Hard rows.

## Hard pressure test (all player teams vs Hard)

Artifact:
- results/week6_hard_pressure_seed42_1000g.csv

Settings:
- mode: pvd
- games: 1000
- seed: 42 (matrix base)
- player strategy: smart

Results summary:
- Blitz vs Hard: win_rate_a 29.1 (PASS), avg_exchanges 2.226 (PASS)
- Grind vs Hard: win_rate_a 38.8 (FAIL, +3.8), avg_exchanges 2.336 (PASS)
- Spread vs Hard: win_rate_a 38.7 (FAIL, +3.7), avg_exchanges 2.345 (PASS)
- Backline vs Hard: win_rate_a 37.3 (FAIL, +2.3), avg_exchanges 2.318 (PASS)

Interpretation:
- Hard-pressure is currently correct only versus Blitz.
- Shared overperformance versus Hard exists across Grind, Spread, and Backline.
- Rally pacing remains healthy in all tested rows.

## Hard-pressure micro experiments (same day)

Experiment A:
- Change: Hard-only Glide Elite Roll threshold from >=7 to >=6.
- Artifact: results/week6_hard_pressure_after_glide_seed42_1000g.csv
- Outcome: Backline moved into band, but Blitz fell below floor and Spread did not improve enough.
- Decision: Rejected.

Experiment B:
- Change: Reverted Glide, then Hard-only Swift Lucky Block draw_and_add_block 2 to 3.
- Artifact: results/week6_hard_pressure_after_swift_seed42_1000g.csv
- Outcome: Spread improved slightly, but Grind and Backline worsened above cap.
- Decision: Rejected.

Current adopted state:
- Reverted to Week 6 kickoff baseline tuning from results/week6_hard_pressure_seed42_1000g.csv.

## Hard template pressure experiments

Template experiment 1:
- Change: Medium template `Tempo D` max_hitters 4 to 5 (Hard uses Medium template).
- Artifact: results/week6_hard_pressure_after_medium_tempoD_seed42_1000g.csv
- Outcome: Spread and Backline improved toward band, Blitz dropped below floor.
- Decision: Rejected.

Template experiment 2:
- Change: Narrowed `Tempo D` card_range to 10 while keeping max_hitters 5.
- Artifact: results/week6_hard_pressure_after_medium_tempoD10_seed42_1000g.csv
- Outcome: Blitz stayed in band, but Grind regressed sharply upward.
- Decision: Rejected.

Template branch state:
- Reverted `data/set_templates.csv` back to baseline Week 6 kickoff value for Medium Tempo D.

## Hard isolated roster lever

Isolation setup:
- Hard team now points to `data/team_dummy_hard_w6.csv` for independent tuning.
- Targeted lever tested: `HardTempo` back_row_pierce trigger from >=5 to >=4.

Artifact:
- results/week6_hard_pressure_after_isolated_hardtempo_seed42_1000g.csv

Outcome vs baseline hard-pressure artifact:
- Blitz: 29.1 to 27.9 (still in band)
- Grind: 38.8 to 39.6 (worse)
- Spread: 38.7 to 38.4 (slight improvement)
- Backline: 37.3 to 36.1 (improvement)

Decision:
- Mixed; not accepted as final Week 6 fix.
- Keep isolation scaffold for cleaner Hard-only iteration, but continue searching for a lever that improves Grind and Spread simultaneously.

## Grind/Spread targeted branch

Objective:
- Bring Spread and Grind vs Hard into 25-35 band via direct team-side tuning after Hard-only levers showed unstable collateral.

Applied branch state (current):
- Spread: Vector Seam Probe threshold >=9
- Grind: No Passive (teams.csv), Lance >=10, Drift >=9, Shield >=10

Key artifact:
- results/week6_hard_pressure_restored_best_grind_seed42_1000g.csv

Current hard-pressure status from this state:
- Blitz vs Hard: 28.1 (PASS)
- Grind vs Hard: 38.0 (FAIL, +3.0)
- Spread vs Hard: 33.8 (PASS)
- Backline vs Hard: 36.2 (FAIL, +1.2)

Interpretation:
- Spread target closure is achieved.
- Grind remains materially above cap despite repeated direct nerfs.
- Backline remains slightly above cap and should be tuned in a separate focused pass.

## Explicit PvP exception

Artifact:
- results/week6_pvp_exception_2026-05-28.md

Decision:
- PvP imbalance for the new teams is explicitly accepted for now.
- Spread is considered shippable in PvP.
- Backline-over-Grind and slight Spread/Backline mirror drift are deferred to a later rebalance pass.
