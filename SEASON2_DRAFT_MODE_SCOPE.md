# Season 2 Draft Mode Scope (Stub)

Status: Draft scope only. Not implemented in Season 1.

## Objective

Define a constrained draft-mode ruleset that can be layered on top of the current Season 1 engine without introducing engine-breaking timing complexity.

## Non-goals

- No new trigger timing categories.
- No per-team engine branch logic.
- No hidden state that prevents deterministic replay.

## Candidate feature scope

1. Pre-match roster draft from a fixed shared pool.
2. Optional ban phase (single ban per side).
3. Draft order templates:
   - Snake draft
   - Alternating picks
4. Team identity tags for draft pool filtering (tempo/control/backline/etc.).

## Data model candidates

- data/draft_pool.csv
  - card_id, player_name, role, identity_tags, rarity_bucket, enabled
- data/draft_rules.csv
  - rule_set, pool_size, bans_per_team, pick_order, max_same_role

Current prototype artifacts:

- data/draft_pool.csv
- data/draft_rules.csv
- DRAFT_PLAYTEST_GUIDE.md
- results/draft_playtest_session_TEMPLATE.md

## Integration constraints

- Preserve `data/player_cards.csv` as source-of-truth ability definitions.
- Keep draft outputs resolvable into standard roster CSV format.
- Draft phase should output deterministic artifact files for replay/testing.

## Validation ideas

- Draft reproducibility test with fixed seed.
- Role-composition legality check.
- Post-draft roster export/import smoke test.

## Immediate playtest workflow

1. Run a manual draft using DRAFT_PLAYTEST_GUIDE.md and ruleset `s2_alpha_snake_24`.
2. Export drafted rosters to `data/team_draft_a.csv` and `data/team_draft_b.csv`.
3. Simulate with existing engine CLI and record results in a copy of results/draft_playtest_session_TEMPLATE.md.

## Open questions

1. How large should the shared draft pool be for Season 2 alpha?
2. Should bans be global or per-role constrained?
3. Do we allow duplicate player cards across teams in early prototypes?
