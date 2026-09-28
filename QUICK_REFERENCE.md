# Quick Reference (Engine-Aligned)

Last updated: September 5, 2026 — reflects the locked-ruleset engine rewrite (see CHANGELOG.md).

This file is a short engine behavior guide.

Authoritative sources:
- Runtime behavior: `src/game.py`, `src/players.py`, `src/strategies.py`
- Editable data source-of-truth docs: `data/player_cards.csv`, `data/set_templates.csv`, `data/teams.csv`, `data/team_passives.csv`, `data/deck_types.csv`

If docs and engine disagree, engine wins.

## Core rules

- Win condition: first team to 15 points.
- Deck: flat 40 cards, 4 copies each of Ace(1)-10, no face cards.
- Team hand size: 5 by default.
- Grind passive `Deep Bench`: hand size 6.
- Lanes: 1=OH, 2=MB, 3=OPP.

## Set templates in engine

Normal play is now a **single universal template (`SETTER_TEMPLATES`) for every team** — the old per-team CSV bundles for normal sets are no longer applied (`data/set_templates.csv`'s `normal` rows are inert):
- 1-3 (quickset): front lanes 1/2/3, back none, max attackers 2
- 4-7: front 1/2/3, back 1/2/3, max attackers 2
- 8-10: front 1/2/3, back 1/2/3, max attackers 3

Broken play (`BROKEN_PLAY_TEMPLATES`) is still per-team/CSV-driven, unchanged from before — this is a known, explicitly unresolved gap (setter-can't-receive-their-own-set play-call), parked for later:
- 1-3: front 1/2, back 2, max 2
- 4-7: front 1/3, back 2, max 1
- 8-10: front 2/3, back 2, max 2

## Attack resolution (`resolve_attack`)

Two-tier, no card-matching system (the old blocker-blocker/attacker-attacker/attacker-blocker cancellation mechanic was retired entirely):

- attack > block: `KILL`
- attack == block (exact tie): `DEFLECT` — ball falls to the **attacker's own side**; that team digs it (equal-or-higher). **No chase if this dig fails** -- instant point to the other side, same as every other dig.
- attack < block: `STUFFED` — instant point, no dig

## Combo (two hitters, one lane)

When a lane has both a front-row and a back-row attack card, the attacker declares resolve order. The first card resolves normally; if it kills/deflects, the second is discarded unused. If it's genuinely stuffed, the lane's single highest blocker card is removed, then the second card resolves against what remains. Either card may independently be declared a tip.

## Tip behavior

- Any (effective) attack card value <= 5 may be declared a tip, front row only.
- Checked against the lane's single **lowest** blocker card (not the block total/stack sum) -- the inverse of a normal hit vs. block: a blocker's card **same or lower** than the tip reads it and stuffs it outright (a tie stuffs too, there's no deflection for a tip); only a strictly **higher** blocker card misses it and lets it through.
- If the tip beats the block, the defender digs **same-or-lower** against the tip's own value (again the inverse of digging a hit). **No chase on a failed tip dig**, regardless of block state.
- Back-row attacks cannot tip.

## Digging and chase

- Kill/deflect digs: equal-or-higher required.
- **A failed dig is always an immediate point to the attacking side -- no chase, ever, regardless of dig type (kill, tip, roll shot, tied-deflection) or block state.** There is no "broken dig" free-ball recovery anymore.
- **Chase exists in exactly one place: a failed serve reception.** Up to 2 attempts, adding cards to the running total (starting from the failed receive card's own value). Success just resumes a normal Set/Attack for the receiving team, same as a clean pass. Failure = ace, point to the server.

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

## Team mats

`src/mats.py` / `list_mats.py` expose the 4 pickable team identities (Blitz, Grind, Spread, Backline) as an explicit `Mat` object: 6 fixed seats, each with a player and at most one ability (some seats are intentionally blank — that's a valid design, not a bug). Dummy AI opponents and shelved draft-mode rosters are excluded automatically.

## Ability `is_active` field — read this before assuming anything is "off"

`is_active: false` in `data/player_cards.csv` does **not** mean disabled. Per `abilities.py`'s own docs: `false` = **passive, fires automatically**; `true` = "requires manual strategy activation," which isn't wired into any strategy, so `true` is effectively the unreachable state. All current rows are `false`, meaning every ability in the CSV is live and firing in every game today.

## Notes

- `WILD_BLOCK` exists in ability definitions but is not currently integrated into strategy placement logic.
- For tuning workflows, update CSV docs first, then apply matching code/runtime updates as needed.
- Balance is **not currently tuned** to the rewritten engine — PvD (vs. dummy AI) win rates are known to be far outside the old target bands in `data/balance_targets.csv`. This is a deliberate, deprioritized gap (see CHANGELOG.md), not an oversight.
