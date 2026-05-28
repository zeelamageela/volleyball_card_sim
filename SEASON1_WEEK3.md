# Season 1 - Week 3 Kickoff

Date: 2026-05-27

## Week 3 objective

Raise Easy rally quality (Blitz vs Easy avg_exchanges >= 1.8) while preserving:

- shared dummy deck constraint
- distinct Easy/Medium/Hard identity profiles
- PvD win-rate target bands

## Starting baseline (from Week 2 lock)

- Easy: team_dummy_easy.csv / EasyLite / Safe Setter / dummy
- Medium: team_dummy_medium.csv / Hard / Back Court Threat / dummy
- Hard: team_dummy_hard.csv / Medium / Elite Draw / dummy

## Week 3 tuning attempts (today)

### Pass 1

Artifact: results/week3_easy_quality_pass1_1000g_seed42.csv

- Change: EasyLite normal card 10 allows up to 2 hitters.
- Outcome:
  - PvD win-rate bands: PASS
  - Blitz vs Easy avg_exchanges: 1.766 (still below 1.8)

### Pass 2

Artifact: results/week3_easy_quality_pass2_1000g_seed42.csv

- Change: EasyLite normal cards 9-10 allow up to 2 hitters.
- Outcome:
  - Blitz vs Easy win rate fell below target band (67.5).
  - Rejected.

### Pass 3 (current active Week 3 candidate)

Artifact: results/week3_easy_quality_pass3_1000g_seed42.csv
Validation: validate_balance_targets.py on pass3 artifact

- Change: Keep card 10 two-hitter, and increase broken-play high card density.
- Outcome:
  - PvD win-rate bands: PASS
  - Blitz vs Easy avg_exchanges: 1.767 (near miss)

### Pass 4

Artifact: results/week3_easy_quality_pass4_1000g_seed42.csv

- Change: pass2 template shape + Easy passive disabled.
- Outcome:
  - Blitz vs Easy win rate below target band.
  - Rejected and rolled back.

  ### Pass 5

  Artifact: results/week3_easy_quality_pass5_1000g_seed42.csv

  - Change: widened low-card broken-play lane options (A/B) only.
  - Outcome:
    - Result was effectively unchanged from pass3.
    - Blitz vs Easy avg_exchanges remained 1.767.

  ### Pass 6

  Artifact: results/week3_easy_quality_pass6_1000g_seed42.csv

  - Change: hybrid high-card normal profile (9-10 two hitters, no back-row lane support).
  - Outcome:
    - Blitz vs Easy fell below win-rate target (68.1).
    - Rejected and rolled back.

## Current status

- Active config restored to pass3 profile.
- Week 3 objective is not fully complete yet.
- Best gap remaining: Blitz vs Easy avg_exchanges is ~0.033 below target floor.
- Observed frontier in tested configs: changes strong enough to clear 1.8 exchanges tended to pull Blitz vs Easy below 70%.

## Week 3 temporary waiver (approved)

Approved for Week 3 only:

- Keep current pass3-style active config and lock it.
- Accept Blitz vs Easy avg_exchanges at 1.767 (target floor 1.8) as a temporary waiver.
- Do not relax the Easy win-rate target band (70-80) in `data/balance_targets.csv`.

Lock artifacts:

- results/week3_lock_2026-05-27/
- results/week3_lock_2026-05-27/MANIFEST.sha256.csv

Waiver rationale:

- The measured shortfall is small (~0.033).
- Tested template-space changes that close this gap tended to break the Easy win-rate floor.
- Preserving identity lock + PvD win-rate stability takes priority for this week.

## Week 3 waiver confidence check (2000g)

Date: 2026-05-28

Artifacts:

- results/week3_waiver_confidence_pvd_2000g_seed42_2026-05-28.csv
- results/week3_waiver_confidence_pvd_2000g_seed42_2026-05-28.validation.txt
- results/week3_waiver_confidence_pvd_2000g_seed314_2026-05-28.csv
- results/week3_waiver_confidence_pvd_2000g_seed314_2026-05-28.validation.txt
- results/week3_waiver_confidence_pvd_2000g_2026-05-28_summary.csv

Results summary:

- Seed 42: PASS=17 FAIL=1 SKIP=2
  - Only FAIL: avg_exchanges, Blitz vs Easy = 1.765 (target >= 1.8)
- Seed 314: PASS=17 FAIL=1 SKIP=2
  - Only FAIL: avg_exchanges, Blitz vs Easy = 1.758 (target >= 1.8)

Decision:

- Confidence check confirms waiver scope is stable and isolated.
- Week 3 lock + waiver remains accepted.

## Next recommended Week 3 step

Test strategy-level tuning for Easy only (no roster/deck changes):

1. Slightly reduce early single-lane predictability in EasyLite broken templates for low cards only.
2. Re-run pvd 1000g seed42.
3. If pass, run confidence check at 2000g seeds 42/314.
