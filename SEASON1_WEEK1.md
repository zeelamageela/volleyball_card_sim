# Season 1 - Week 1 Baseline Lock

Date: 2026-05-27

## Week 1 goals

1. Produce one reproducible baseline matrix snapshot.
2. Define target bands for key matchups.
3. Identify top-priority balance gaps without changing rules.

## Baseline command

```powershell
.\.venv\Scripts\python.exe balance_matrix.py --mode all --games 1000 --seed 42 --output results\season1_week1_baseline_2026-05-27.csv
```

## Baseline artifact

- results/season1_week1_baseline_2026-05-27.csv

## Week 1 target bands

- data/balance_targets.csv

## Snapshot summary (from baseline CSV)

- Blitz vs Grind (pvp): Blitz 34.2%, Grind 65.8% -> outside 45-55 target band.
- Blitz vs Easy (pvd): 99.2% -> above 70-80 target band (Easy too weak).
- Blitz vs Medium (pvd): 71.6% -> above 52-62 target band (Medium too weak).
- Blitz vs Hard (pvd): 30.2% -> inside 25-35 target band.
- Grind vs Easy (pvd): 99.6% -> above 70-80 target band (Easy too weak).
- Grind vs Medium (pvd): 66.0% -> above 52-62 target band (Medium weak).
- Grind vs Hard (pvd): 23.2% -> slightly below 25-35 target band.

## Week 1 pass/fail checklist

- [x] Baseline matrix generated and saved.
- [x] Initial target ranges documented in CSV.
- [x] Major out-of-band matchups identified.
- [x] Team-level tuning plan approved for Week 2.

## Recommended Week 2 tuning order (no rules changes)

1. Pull Hard down first (deck_type and passive strength).
2. Push Easy up second (deck_type and passive strength).
3. Nudge Medium into center band.
4. Tune Blitz vs Grind identity gap after dummy tiers are in range.

## Notes

- Keep seed fixed while tuning so deltas are comparable.
- Prioritize deck_types.csv and teams.csv changes before ability rewrites.
- Re-run the same baseline command after each tuning batch.

## Week 2 tuning passes (May 27, 2026)

Artifacts:

- results/season1_week2_pass1_2026-05-27.csv
- results/season1_week2_pass2_2026-05-27.csv
- results/season1_week2_pass3_2026-05-27.csv
- results/season1_week2_pass4_2026-05-27.csv

Current best snapshot (Pass 4):

- Blitz vs Easy: 98.1% (target 70-80) -> Easy still far too weak.
- Blitz vs Medium: 44.5% (target 52-62) -> Medium now too strong.
- Blitz vs Hard: 30.0% (target 25-35) -> on target.
- Grind vs Easy: 98.5% (target 70-80) -> Easy far too weak.
- Grind vs Medium: 45.6% (target 52-62) -> Medium too strong.
- Grind vs Hard: 24.9% (target 25-35) -> effectively on edge of target.
- Blitz vs Grind PvP remains out of band (34.2 / 65.8 vs target 45-55).

Interpretation:

1. Deck-only tuning cannot currently satisfy Easy and Medium targets simultaneously.
2. Hard tier is now close to target and should be treated as near-locked.
3. Next tuning should focus on Easy and Medium roster/passive power (still no core rule changes).

## Shared dummy deck constraint update

Constraint adopted:

- All dummy tiers use the same `dummy` deck type.
- Tier separation is tuned through roster construction + set_template + passive assignment.

Current locked config (best under shared-dummy constraint):

- Easy: roster `team_dummy_medium.csv`, set_template `Medium`, passive `Safe Setter`, deck `dummy`
- Medium: roster `team_dummy_medium.csv`, set_template `Hard`, passive `Back Court Threat`, deck `dummy`
- Hard: roster `team_dummy_hard.csv`, set_template `Medium`, passive `Elite Draw`, deck `dummy`
	- plus Hard roster tweak: `Lucky` at Libero slot in `team_dummy_hard.csv`

Best shared-dummy result artifact:

- results/season1_week2_same_dummy_pass8_500g.csv

Pass 8 (500 games, seed 42) summary vs target bands:

- Blitz vs Easy: 71.2 (target 70-80) -> in band
- Blitz vs Medium: 61.2 (target 52-62) -> in band
- Blitz vs Hard: 27.2 (target 25-35) -> in band
- Grind vs Easy: 70.6 (target 70-80) -> in band
- Grind vs Medium: 54.8 (target 52-62) -> in band
- Grind vs Hard: 30.8 (target 25-35) -> in band

Open item:

- Blitz vs Grind PvP remains out of target (not addressed in this dummy-tier pass).
