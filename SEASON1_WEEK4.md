# Season 1 - Week 4 Ability Curation

Date: 2026-05-28

## Week 4 objective

Curate the Season 1 live ability pool so it is coherent, explainable, and maintainable.

Constraints:

- No new rally mechanics.
- No new trigger timing types.
- Keep CSV-first workflow.

## Curation artifacts

- data/ability_effect_pool.csv
- data/player_cards.csv
- src/abilities.py

## Curation policy used

1. If an effect appears in data/player_cards.csv, mark as `live`.
2. If an effect exists in engine constants but is not used by current player cards, mark as `parked` for Season 1.
3. Parked effects are not deleted; they remain available for future seasons.

## Current pool summary

- Total engine effects: 32
- Live effects in current Season 1 player pool: 10
- Parked effects: 22

Live effects:

- adjacent_block_bonus
- attack_value_bonus
- back_row_pierce
- dig_threshold
- draw_and_add_block
- heavy_spin
- pierce_block
- roll_shot
- seam_shot
- slide_lanes

## Week 4 acceptance checklist

- [x] Live ability pool is coherent and explainable.
- [x] Redundant/experimental effects are parked as inactive in curation artifact.

## Notes

- This pass does not change rally rules or attack resolution.
- This pass does not remove any engine constants.
- Next step for Week 5 is content expansion using this curated live effect pool.
