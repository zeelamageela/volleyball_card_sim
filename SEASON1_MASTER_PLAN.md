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
Status: Not started

Acceptance:

- [ ] Live ability pool is coherent and explainable.
- [ ] Redundant/experimental abilities parked as inactive.

### Week 5: Content expansion sprint 1
Status: Not started

Acceptance:

- [ ] Add 1-2 teams using existing mechanics.
- [ ] New teams near target ranges.

### Week 6: Content expansion sprint 2
Status: Not started

Acceptance:

- [ ] Reach minimum 6-team ecosystem.
- [ ] No extreme out-of-band matchup without explicit reason.

### Week 7: Stability and regression week
Status: Not started

Acceptance:

- [ ] High-sample, multi-seed regressions controlled.
- [ ] Results hold across seeds.

### Week 8: Season 1 lock and Season 2 readiness
Status: Not started

Acceptance:

- [ ] Season 1 freeze complete and documented.
- [ ] Draft mode scope defined (not implemented).

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

You are currently at the end of Week 3 with an approved isolated waiver.
Next actionable roadmap step is Week 4 (Ability curation pass).

## Session handoff order

When resuming in a new session, open files in this order:

1. SEASON1_MASTER_PLAN.md
2. SEASON1_WEEK3.md
3. SIMULATION_QUICK_GUIDE.md
4. data/teams.csv
5. data/set_templates.csv
