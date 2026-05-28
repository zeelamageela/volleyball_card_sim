# Season 1 Week 2 - Identity Notes

Date: 2026-05-27

## Deliverable summary

This note captures identity-focused experiments and their effect on balance.

## Matrix artifacts consulted

- `results/season1_week2_identity_pass1_500g.csv`
- `results/season1_week2_identity_pass2_500g.csv`
- `results/season1_week2_same_dummy_pass8_reconfirm_500g.csv`
- `results/season1_week2_identity_branch_passL_500g.csv`

## What happened

1. Pure identity separation (distinct rosters for all tiers) significantly destabilized win-rate bands.
2. Balance recovered when Easy and Medium reused the same core roster shell with different template/passive settings.
3. Hard remains tunable with roster+passive while keeping shared deck type.
4. A new identity-constrained configuration now passes all PvD target bands while keeping distinct Easy/Medium/Hard rosters.

## Key lesson

With all dummy teams on a single `dummy` deck, roster identity and tier balance can conflict.

- Identity-only changes were unstable without additional template control.
- Introducing `EasyLite` set templates provided the final tuning lever needed to satisfy both identity and PvD balance.

## Recommended path

1. Keep the new identity-constrained lock as active runtime config.
2. Continue PvP (Blitz vs Grind) tuning as a separate stream.
3. Accept future candidates only if they pass BOTH:
   - target win-rate bands
   - TEAM_IDENTITY_CHECKLIST.md

## Current status

- Shared deck constraint: satisfied.
- Active config (identity-constrained):
   - Easy: `team_dummy_easy.csv`, template `EasyLite`, passive `TBD`, deck `dummy`
   - Medium: `team_dummy_medium.csv`, template `Hard`, passive `Back Court Threat`, deck `dummy`
   - Hard: `team_dummy_hard.csv`, template `Medium`, passive `Elite Draw`, deck `dummy`
- Dummy tier win bands (500g): satisfied in `results/season1_week2_identity_branch_passL_500g.csv`.
- Distinct roster identity across Easy/Medium/Hard: satisfied in active config.
- Remaining gap: PvP Blitz vs Grind is still outside target.
