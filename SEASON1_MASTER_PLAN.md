# Season 1 Master Plan (Single Source of Truth)

Date created: 2026-05-28

This is the primary roadmap for long-term direction and current progress.
Future sessions should read this file first, then open the current week file.

## Long-term path (vision lock)

1. Season 1: Core stabilization and content expansion
- More teams
- Better matchup spread
- Cleaner docs
- Better telemetry

2. Season 2: Draft mode
- Single pre-game draft flow
- Same rally rules

3. Season 3: Card-modifier layer
- Only after draft metrics are healthy and rules remain readable

## Product principle

Do not add new inside-loop rally mechanics during Season 1.
Depth should come from content, templates, passives, and balancing process.

## Season 1 design constitution

- No new rally phases this season.
- No new attack/block resolution branches.
- New abilities use existing trigger windows only.
- Every live ability is explainable in one sentence.
- CSV-first balancing workflow for all tuning.

## Season 1 exit gates (ready for Season 2)

All gates must pass:

- Core matchup targets in band for 3 consecutive matrix runs.
- No unresolved rule ambiguity in active reference docs.
- At least 6 distinct viable teams are balanced and playable.
- Rally ending distribution stable across at least 3 seeds.
- Balance runs reproducible from one command.

## Metrics to track every balance run

- Win rate by matchup
- Avg rallies per game
- Avg exchanges per rally
- Top ending-type share
- Cross-seed volatility
- Non-interactive ending share (if/when formalized)

## 8-week Season 1 roadmap and current status

### Week 1: Baseline lock
Status: Completed

Acceptance:

- [x] Baseline matrix CSV exists and is reproducible.
- [x] Target bands are documented and agreed.

Primary file: SEASON1_WEEK1.md

### Week 2: Telemetry and reporting hygiene
Status: Completed

Acceptance:

- [x] One run shows what improved/regressed.
- [x] Outliers identifiable quickly.

Primary file: SEASON1_WEEK2.md

### Week 3: Team identity pass (no new mechanics)
Status: Completed with approved temporary waiver

Acceptance:

- [x] Teams feel different by play pattern.
- [x] PvD target bands stable with identity lock.
- [ ] Blitz vs Easy avg_exchanges >= 1.8 (waived for Week 3 only)

Primary file: SEASON1_WEEK3.md
Waiver file: results/week3_waiver_2026-05-27.md

### Week 4: Ability curation pass
Status: Completed

Acceptance:

- [x] Live ability pool is coherent and explainable.
- [x] Redundant/experimental abilities parked as inactive.

Primary file: SEASON1_WEEK4.md
Curation artifact: data/ability_effect_pool.csv

### Week 5: Content expansion sprint 1
Status: Completed with approved temporary waiver

Acceptance:

- [x] Add 1-2 teams using existing mechanics.
- [x] New teams near target ranges (with narrow approved waiver rows).

Draft candidates selected and wired: Spread (Lane Chess) and Backline (Transition Sniper)
Baseline artifact: results/week5_baseline_all_1000g_seed42_20260528_092739.csv
Target sheet expanded to include Spread/Backline PvP and PvD bands; current Week 5 misses are explicit.
Closeout artifact: results/season1_week5_microedit_focus_seed42_1000g_2026-05-28.csv
Waiver file: results/week5_waiver_2026-05-28.md

### Week 6: Content expansion sprint 2
Status: Completed with approved temporary exceptions

Acceptance:

- [x] Reach minimum 6-team ecosystem.
- [x] No extreme out-of-band matchup without explicit reason (explicit exceptions documented).

Hard-pressure test artifact: results/week6_hard_pressure_seed42_1000g.csv
PvP exception file: results/week6_pvp_exception_2026-05-28.md
Waiver file: results/week6_waiver_2026-05-28.md

### Week 7: Stability and regression week
Status: Completed with approved temporary stability exceptions

Acceptance:

- [x] High-sample, multi-seed regressions controlled.
- [x] Results hold across seeds (with explicit approved exceptions).

Stability exception file: results/week7_stability_exception_2026-05-28.md
Regression log: results/week7_regression_test_2026-05-28.log

### Week 8: Season 1 lock and Season 2 readiness
Status: Completed with approved lock exceptions

Acceptance:

- [x] Season 1 freeze complete and documented.
- [x] Draft mode scope defined (not implemented).

Primary file: SEASON1_WEEK8.md
Lock manifest: results/week8_lock_manifest_2026-05-28.csv
Freeze decision: results/week8_freeze_decision_2026-05-28.md
Season 2 scope stub: SEASON2_DRAFT_MODE_SCOPE.md

## Weekly ritual (operating loop)

- Monday: pick 1-2 hypotheses
- Midweek: focused matrix tests
- Friday: full matrix + regression check
- End of week: snapshot, decide next week priorities

## What not to do in Season 1

- No new trigger timing types
- No team-specific special-case rule branches
- No tuning decisions from one noisy run
- No abilities that are hard to explain physically

## Current active checkpoint

Season 1 lock is complete with approved documented exceptions.
Next actionable roadmap step is Season 2 scope refinement and prioritized cleanup of accepted exceptions.

## Session handoff order

When resuming in a new session, open files in this order:

1. SEASON1_MASTER_PLAN.md
2. SEASON2_DRAFT_MODE_SCOPE.md
3. SIMULATION_QUICK_GUIDE.md
4. data/teams.csv
5. data/set_templates.csv
6. data/player_cards.csv
