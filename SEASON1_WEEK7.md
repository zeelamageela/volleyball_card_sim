# Season 1 - Week 7 Stability and Regression Week

Date: 2026-05-28

## Week 7 objective

Prove the current Season 1 state is stable enough to carry forward, with explicit exceptions documented instead of hidden.

## Starting point

Reference files:
- results/week6_waiver_2026-05-28.md
- results/week6_hard_pressure_restored_best_grind_seed42_1000g.csv
- results/week6_pvp_snapshot_seed42_1000g.csv
- data/player_cards.csv
- data/teams.csv
- data/balance_targets.csv

## Week 7 acceptance checklist

- [x] High-sample, multi-seed regressions controlled.
- [x] Results hold across seeds (with explicit approved exceptions).
- [x] Known exceptions remain narrow and documented.

## Planned checks

1. Multi-seed matrix + validation pipeline.
2. Regression test execution for locked targets.
3. Snapshot of remaining exception rows for handoff to Week 8.

## Executed checks

1. Multi-seed matrix + validation pipeline (2000g, seeds 42/314/2718)
	- Summary: results/week7_kickoff_all_2000g_20260528_132301_summary.csv
2. Regression test for locked scope
	- Log: results/week7_regression_test_2026-05-28.log
	- Status: PASS (1 passed)
3. Stability exception consolidation
	- File: results/week7_stability_exception_2026-05-28.md

## Week 7 closeout

Status: Completed with approved temporary stability exceptions.

Carry-forward to Week 8:
- Use the Week 7 stability exception file as the lock/freeze risk register.
