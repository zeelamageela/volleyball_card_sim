# Quick Reference (Engine-Aligned)

Last updated: May 27, 2026

This file is a short engine behavior guide.

Authoritative sources:
- Runtime behavior: `src/game.py`, `src/players.py`, `src/strategies.py`
- Editable data source-of-truth docs: `data/player_cards.csv`, `data/set_templates.csv`, `data/teams.csv`, `data/team_passives.csv`, `data/deck_types.csv`

If docs and engine disagree, engine wins.

## Core rules

- Win condition: first team to 15 points.
- Team hand size: 5 by default.
- Grind passive `Deep Bench`: hand size 6.
- Lanes: 1=OH, 2=MB, 3=OPP.

## Set templates in engine

Normal play (`SETTER_TEMPLATES`):
- 1-3: front lanes 1/2/3, back none, max attackers 3
- 4-5: front 1/2, back 1/2/3, max 3
- 6-7: front 2/3, back 1/2/3, max 3
- 8-9: front 1/3, back 1/2/3, max 4
- 10: front 1/2/3, back 1/2/3, max 4

Broken play (`BROKEN_PLAY_TEMPLATES`):
- 1-3: front 1/2, back 2, max 2
- 4-7: front 1/3, back 2, max 1
- 8-10: front 2/3, back 2, max 2

## Matching behavior

- Matching resolves before final lane choice and before numeric attack/block outcomes.
- Blocker-blocker same value in one lane:
  blockers neutralize, lane becomes unblocked (0 block value), rally continues.
- Attacker-attacker same value in one lane:
  lane eliminated, defender credited for that elimination result.
- Single-attacker attacker-blocker same value:
  lane eliminated. This is a match, not a touched-block deflect.
- Multi-attacker attacker-blocker matches:
  matched cards are removed; lane can remain active if unmatched attacker cards remain.
- Lanes are processed high-to-low by attack value.
- If all attack lanes are eliminated, rally ends using the final elimination result.

## Attack resolution (`resolve_attack`)

Applies only on a chosen lane that survived matching.

- If attack > block: `KILL`
- Else let diff = block - attack:
  - diff 0-4: `DEFLECT`
  - diff 5+: `STUFFED`

Deflect side:
- diff 0-2: deflect to defender side (defender digs)
- diff 3-4: deflect to attacker side (attacker digs)

## Tip behavior

- Base tip threshold is `3` on normal front-row attacks.
- Back-row attacks cannot tip.
- Some setter abilities can raise the tip threshold for that exchange.
- Tip digs and deflect digs do not use chase.

## Team passives in engine

- `Deep Bench` (Grind): +1 hand size.
- `Safe Setter` (Easy): setter digs do not force broken play.
- `Back Court Threat` (Medium): back-row attacks ignore first blocker.
- `Elite Draw` (Hard): action draws use draw-2 keep-high.

## Dummy blocking rule (implemented)

When 2 lanes are attacked:
- even-majority in dummy hand -> double block rightmost lane
- odd-majority or tie -> double block leftmost lane

When 1 lane is attacked:
- up to 3 blockers on that lane

When 3 lanes are attacked:
- one blocker per lane (first 3 cards)

## Notes

- `WILD_BLOCK` exists in ability definitions but is not currently integrated into strategy placement logic.
- For tuning workflows, update CSV docs first, then apply matching code/runtime updates as needed.
