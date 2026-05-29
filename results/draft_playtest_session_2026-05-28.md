# Draft Playtest Session

Date: 2026-05-28
Ruleset: s2_alpha_snake_24 (manual, no bans used in this first smoke)
Seed: 42

## Bans

- Team A ban: none (smoke run)
- Team B ban: none (smoke run)

## Draft order log

1. DraftA: Sarge (Setter)
2. DraftB: Relay (Setter)
3. DraftB: Strike (OPP)
4. DraftA: Lance (OPP)
5. DraftA: Pivot (MB)
6. DraftB: Tower (MB)
7. DraftB: Drift (OH)
8. DraftA: Blaze (OH)
9. DraftA: Orbit (DS)
10. DraftB: Dash (DS)
11. DraftB: Floor (Libero)
12. DraftA: Anchor (Libero)

## Final rosters

Team A (data/team_draft_a.csv):
- Sarge, Setter
- Lance, OPP
- Pivot, MB
- Blaze, OH
- Orbit, DS
- Anchor, Libero

Team B (data/team_draft_b.csv):
- Relay, Setter
- Strike, OPP
- Tower, MB
- Drift, OH
- Dash, DS
- Floor, Libero

No-repeat constraint check:
- No duplicated player names across Team A and Team B.

## Sim commands used

1. python main.py --mode pvp --strategy-a smart --strategy-b smart --games 2000 --seed 42 --player-cards data/player_cards.csv --roster-a data/team_draft_a.csv --roster-b data/team_draft_b.csv --team-a-name DraftA --team-b-name DraftB
2. python main.py --mode pvp --strategy-a smart --strategy-b smart --games 2000 --seed 42 --player-cards data/player_cards.csv --roster-a data/team_draft_b.csv --roster-b data/team_draft_a.csv --team-a-name DraftB --team-b-name DraftA

## Outcome snapshot

DraftA vs DraftB:
- Win rate A: 53.8%
- Avg exchanges: 2.74
- Top endings: Stuffed, Deflect not dug, Tip not dug

DraftB vs DraftA:
- Win rate A (DraftB): 46.6%
- Avg exchanges: 2.75
- Top endings: Stuffed, Deflect not dug, Tip not dug

## Qualitative notes

- Fun score (1-5): 4
- Clarity score (1-5): 4
- Balance concern (Y/N): Y (DraftA has a stable 7-8 point edge)
- What we should change next:
  - Add real ban phase and enforce one-ban-per-team in the next draft trial.
  - Try one alternate pick order and compare if edge shrinks.
  - Introduce role-value caps during draft to reduce over-concentrated defensive stacks.
