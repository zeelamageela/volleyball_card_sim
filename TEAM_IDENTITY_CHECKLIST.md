# Team Identity Checklist

Use this checklist before accepting a roster/tuning pass.

## Identity constraints

- [ ] Each tier has a distinct roster core (no copy-paste team shells).
- [ ] No two dummy tiers share more than 3 of 6 exact role-player slots.
- [ ] Each team has 2-3 signature interactions (ability + template + passive).
- [ ] Team fantasy can be explained in one sentence.

## Balance constraints

- [ ] All dummy tiers use deck_type `dummy`.
- [ ] Blitz vs Easy player win rate is within 70-80.
- [ ] Blitz vs Medium player win rate is within 52-62.
- [ ] Blitz vs Hard player win rate is within 25-35.
- [ ] Grind vs Easy player win rate is within 70-80.
- [ ] Grind vs Medium player win rate is within 52-62.
- [ ] Grind vs Hard player win rate is within 25-35.

## Feel constraints

- [ ] Top ending type share <= target in `data/balance_targets.csv`.
- [ ] No tier feels like only numeric scaling of another tier.
- [ ] Easy/Medium/Hard feel different in lane pressure and rally texture.

## Process constraints

- [ ] Matrix run executed with fixed seed and documented artifact.
- [ ] Any roster changes are recorded in session notes.
- [ ] If identity and balance conflict, capture the tradeoff explicitly.
