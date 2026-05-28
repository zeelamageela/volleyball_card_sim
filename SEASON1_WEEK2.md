# Season 1 - Week 2 Closeout

Date: 2026-05-27

## Week 2 completion checklist

- [x] Baseline config frozen for Week 2 lock.
- [x] Multi-seed confidence runs completed.
- [x] Target validation reports generated for each confidence run.
- [x] Approved exception (PvP gap) documented.
- [x] Week 3 objective defined.

## Locked Week 2 config

Active runtime mapping:

- Blitz: team_blitz.csv / Blitz / TBD / standard / use_hand=true
- Grind: team_grind.csv / Grind / Deep Bench / standard / use_hand=true
- Easy: team_dummy_easy.csv / EasyLite / TBD / dummy / use_hand=false
- Medium: team_dummy_medium.csv / Hard / Back Court Threat / dummy / use_hand=false
- Hard: team_dummy_hard.csv / Medium / Elite Draw / dummy / use_hand=false

Lock snapshot directory:

- results/week2_lock_2026-05-27/
- results/week2_lock_2026-05-27/MANIFEST.sha256.csv

## Primary Week 2 validation artifact

- results/season1_week2_identity_branch_passL_500g.csv
- results/season1_week2_identity_notes.md

## Confidence suite (larger runs)

Command intent: 3 seeds, 2000 games each, full matrix.

Artifacts:

- results/week2_confidence_all_2000g_seed42_2026-05-27.csv
- results/week2_confidence_all_2000g_seed42_2026-05-27.validation.txt
- results/week2_confidence_all_2000g_seed314_2026-05-27.csv
- results/week2_confidence_all_2000g_seed314_2026-05-27.validation.txt
- results/week2_confidence_all_2000g_seed2718_2026-05-27.csv
- results/week2_confidence_all_2000g_seed2718_2026-05-27.validation.txt
- results/week2_confidence_all_2000g_2026-05-27_summary.csv

Confidence summary:

- Seed 42: PASS=20 FAIL=4
- Seed 314: PASS=21 FAIL=3
- Seed 2718: PASS=21 FAIL=3

Consistent failing checks across seeds:

1. PvP win-rate gap (Blitz vs Grind and reverse) outside 45-55 target.
2. Blitz vs Easy avg_exchanges slightly below 1.8 floor (~1.746 to 1.749).

## Approved Week 2 waiver

Accepted exception for Week 2 lock:

- PvP Blitz vs Grind is intentionally outside the 45-55 target band.

Rationale:

- Team identity and dummy-tier progression stability are prioritized entering Week 3.
- PvD tier targets are stable across seeds and remain the primary progression gate.

## Week 3 starting objective

Improve Easy rally quality (raise Blitz vs Easy avg_exchanges to >=1.8) while preserving current PvD win-rate bands and keeping the shared dummy deck + identity lock intact.

## Operational commands for Week 3 start

Single matrix + validator:

```powershell
.\.venv\Scripts\python.exe run_balance_pipeline.py --mode all --games 2000 --seeds 42 --label week3_baseline
```

Regression guard test (locked Week 2 PvD bands):

```powershell
.\.venv\Scripts\python.exe -m pytest tests/test_balance_regression.py -q
```
