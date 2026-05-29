# Week 8 Freeze Decision - 2026-05-28

Scope: Season 1 lock and Season 2 readiness

## Decision

Season 1 decision: GO with explicit approved exceptions.

## Freeze package references

- Week 8 lock manifest: results/week8_lock_manifest_2026-05-28.csv
- Week 7 multi-seed summary: results/week7_kickoff_all_2000g_20260528_132301_summary.csv
- Week 7 regression guard log: results/week7_regression_test_2026-05-28.log
- Week 7 stability exception register: results/week7_stability_exception_2026-05-28.md
- Week 6 PvP exception register: results/week6_pvp_exception_2026-05-28.md

## Accepted exceptions at freeze

1. Blitz vs Grind PvP mirror remains outside 45-55 target.
2. Backline-over-Spread mirror drift appears in some seeds.
3. Grind vs Hard can remain slightly above cap depending on seed/state branch.
4. Backline vs Easy/Hard can remain slightly above cap depending on seed/state branch.
5. Blitz vs Easy avg_exchanges can remain slightly below floor.

## Why this is acceptable for lock

- Exceptions are documented, narrow, and reproducible.
- Core engine behavior and CSV-driven architecture are stable.
- Regression guard for locked Week 2 scope is passing.
- Rally quality guardrails are broadly healthy in Week 7 runs.

## Post-freeze plan

- Treat current exceptions as explicit Season 1 known issues.
- Prioritize PvP cleanup and Hard-pressure normalization in early Season 2 balancing passes.
