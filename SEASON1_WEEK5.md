# Season 1 - Week 5 Content Expansion Sprint 1

Date: 2026-05-28

## Week 5 objective

Add 1-2 teams using only existing mechanics and push them toward target ranges.

## Team identities selected

- Spread A: Lane Chess
- Backline A: Transition Sniper

## CSV changes applied

- data/teams.csv
- data/team_passives.csv
- data/set_templates.csv
- data/team_spread.csv
- data/team_backline.csv
- data/player_cards.csv

## Design notes

Spread (Lane Chess):
- Emphasis on lane manipulation and seam exploitation.
- Core effects: slide_lanes, seam_shot, roll_shot, dig_threshold.

Backline (Transition Sniper):
- Emphasis on defense-to-offense transition and back-row finish.
- Core effects: back_row_pierce, dig_threshold, draw_and_add_block.

## Acceptance checklist (Week 5)

- [x] Add 1-2 teams using existing mechanics.
- [x] New teams near target ranges (with approved temporary waiver).

## Baseline matrix artifacts

- results/week5_baseline_all_1000g_seed42_20260528_092739.csv
- results/week5_baseline_all_1000g_seed42_20260528_092739.validation.txt
- results/week5_baseline_all_1000g_seed42_20260528_092739.matrix.log.txt
- results/week5_baseline_all_1000g_20260528_092739_summary.csv

## Baseline findings

- Formal validation status: FAIL (PASS=50, FAIL=6, SKIP=0)
- Spread vs Backline baseline is near even: Spread 50.1% vs Backline 49.9%
- Spread stays inside global rally-quality bands in the observed baseline slice.
- Backline looks slightly stronger than desired against dummies, especially versus Easy and Hard.
- Existing pre-Week-5 target failures remain in the environment, so this baseline should be treated as a first-pass content snapshot rather than a lock candidate.

## Target sheet expansion

- Added explicit Week 5 win-rate targets for Spread and Backline in data/balance_targets.csv.
- Added a close-match PvP target for Spread vs Backline (45-55 both directions).
- Added standard PvD dummy bands for both new teams against Easy, Medium, and Hard.

## Validation after target expansion

- Updated validation status against the expanded target sheet: FAIL (PASS=55, FAIL=9, SKIP=0)
- Spread vs Backline PvP passes both directions.
- Spread vs Hard fails high at 41.1 against a 25-35 target.
- Backline vs Easy fails high at 88.0 against a 70-80 target.
- Backline vs Hard fails high at 44.8 against a 25-35 target.
- Legacy failures outside Week 5 still remain in Blitz/Grind target rows.

## Representative Week 5 rows

- Spread vs Easy: 76.7% win rate, avg_exchanges 1.851
- Spread vs Medium: 53.1% win rate, avg_exchanges 3.201
- Spread vs Hard: 41.1% win rate, avg_exchanges 2.301
- Backline vs Easy: 88.0% win rate, avg_exchanges 1.898
- Backline vs Medium: 56.2% win rate, avg_exchanges 3.216
- Backline vs Hard: 44.8% win rate, avg_exchanges 2.363

## Next action

Tune Spread/Backline against dummy bands first, then rerun focused Week 5 matrix before any confidence suite.

## Tuning log (2026-05-28)

Spread-first pass:
- Vector Seam Probe threshold tested at >=9, then restored to >=8.
- Ribbon Roll threshold kept at >=7 (from >=6 baseline).
- Best focused checkpoint from this cycle: Spread/Easy PASS, Spread/Medium PASS, Spread/Hard still high.
- Artifact: results/spread_focused_1000g_seed42_20260528_100346.csv

Backline-second pass:
- Cannon Backline Cannon threshold: >=6 to >=7.
- Snipe Transition Snipe threshold: >=6 to >=7.
- Relay Transition Tempo quick-set bonus: +2 to +1.
- Echo/Floor dig_threshold: +3 to +2.

Latest focused artifacts:
- results/backline_focus_seed42_1000g_2026-05-28.csv
- results/spread_focus_seed42_1000g_2026-05-28.csv
- results/backline_spread_pvp_focus_seed42_1000g_2026-05-28.csv

Latest focused status snapshot:
- Backline vs Easy: 80.7 (slightly high)
- Backline vs Hard: 37.0 (high)
- Backline vs Medium: 52.9 (in band)
- Backline vs Spread / Spread vs Backline: both in band
- Spread vs Hard: still high
- Spread vs Medium: dipped low in the latest focused run

## Session wrap (2026-05-28)

- Week 5 identity expansion is implemented and tuned in two ordered passes (Spread first, Backline second).
- Validation coverage now explicitly includes Spread/Backline rows in data/balance_targets.csv.
- Remaining work is narrow and known: pull Spread vs Hard down while protecting Spread vs Medium floor, and trim Backline vs Easy/Hard slightly.

Recommended next session start:

1. Re-run focused Week 5 matrix slices from results/backline_focus_seed42_1000g_2026-05-28.csv and results/spread_focus_seed42_1000g_2026-05-28.csv as baseline comparators.
2. Apply one micro-adjustment at a time, prioritizing dummy-band closure over PvP mirror changes.
3. Re-check expanded validator output before any confidence suite.

## Week 5 closeout

Status: Completed with approved temporary waiver.

Closeout artifacts:
- results/season1_week5_microedit_focus_seed42_1000g_2026-05-28.csv
- results/season1_week5_microedit_focus_seed42_1000g_2026-05-28.validation.txt
- results/week5_waiver_2026-05-28.md

Week 6 kickoff file:
- SEASON1_WEEK6.md
