# Week 7 Stability Exception - 2026-05-28

Scope: Season 1 Week 7 Stability and Regression Week

## Inputs

- results/week7_kickoff_all_2000g_20260528_132301_summary.csv
- results/week7_kickoff_all_2000g_seed42_20260528_132301.validation.txt
- results/week7_kickoff_all_2000g_seed314_20260528_132301.validation.txt
- results/week7_kickoff_all_2000g_seed2718_20260528_132301.validation.txt
- results/week7_regression_test_2026-05-28.log

## Regression guard status

- PASS: tests/test_balance_regression.py (Week 2 lock scope)

## Multi-seed validation status

- Seed 42 validator_exit=1
- Seed 314 validator_exit=1
- Seed 2718 validator_exit=1

## Persistent cross-seed exceptions (all 3 seeds)

1. Blitz vs Grind PvP remains outside 45-55 mirror target in both directions.
2. Grind vs Hard PvD remains above 35 cap.
3. Spread vs Medium PvD remains below 52 floor.
4. Backline vs Easy PvD remains above 80 cap.
5. Blitz vs Easy avg_exchanges remains slightly below 1.8 floor.

## Near-persistent / seed-sensitive exceptions

1. Backline vs Spread PvP is slightly above cap in 2/3 seeds.
2. Backline vs Hard PvD is slightly above cap in 2/3 seeds.
3. Spread vs Easy PvD exceeded cap in 1/3 seeds.
4. Blitz vs Medium PvD exceeded cap in 1/3 seeds.

## Decision

Week 7 is accepted as Completed with approved temporary stability exceptions.

Rationale:
- High-sample multi-seed run exists and is reproducible.
- Failures are narrow, known, and documented.
- Regression guard is green for locked Week 2 scope.
- Remaining issues are suitable for Week 8 lock/freeze decisioning.
