# Draft Playtest Guide (No Implementation)

This is a manual playtest flow for trying draft mode now, without changing engine code.

## Goal

Prototype whether pre-match drafting creates fun roster identity decisions while still exporting to normal roster CSV files for simulation.

## Inputs

- data/draft_pool.csv
- data/draft_rules.csv
- data/player_cards.csv

## Recommended first ruleset

Use `s2_alpha_snake_24` from data/draft_rules.csv.

- Pool size: 24 cards
- Bans: 1 per team
- Pick order: snake
- Picks per team: 6
- Role limit: max 2 of any single role
- Role coverage: require at least 1 Setter, 1 OH, 1 MB, 1 OPP, and at least 1 of DS/Libero

## Manual draft sequence

1. Load the 24 enabled cards from data/draft_pool.csv.
2. Each team bans 1 card (simultaneous reveal).
3. Run snake draft until each team has 6 cards.
4. Validate each drafted roster against role limits and coverage.
5. Export each drafted roster into standard roster CSV format under data/.

## Export format

Create files like:
- data/team_draft_a.csv
- data/team_draft_b.csv

Use this CSV schema:

player_name,role
Name1,Setter
Name2,OPP
Name3,MB
Name4,OH
Name5,DS
Name6,Libero

## Sim command (after manual export)

python main.py --mode pvp --strategy-a smart --strategy-b smart --games 1000 --seed 42 --player-cards data/player_cards.csv --roster-a data/team_draft_a.csv --roster-b data/team_draft_b.csv

## Session log template

Record each playtest in:
- results/draft_playtest_session_YYYY-MM-DD.md

Template:

- Ruleset:
- Seed:
- Bans:
- Draft order:
- Team A roster:
- Team B roster:
- Match result snapshot:
- Fun score (1-5):
- Balance concern (Y/N):
- Notes:

## Known caveat

The draft pool currently includes one duplicate display name (`Echo`) across different role contexts.
The runtime now resolves those entries by roster role, but for table clarity in live drafting you may still prefer avoiding both in the same match.
