# Volleyball Card Sim - One Page Status

Date: 2026-05-29

## Where the game is right now

- Season 1 is closed and frozen as GO with explicit exceptions.
- Core engine and CSV-first workflow are stable.
- Season 2 draft mode is scoped and being explored through manual playtests, not engine implementation yet.
- Printing pipeline is now routed to root-level outputs and includes updated reference cards.

## What is stable

- Core attack resolution and matching behaviors are implemented and documented.
- Team and roster config flow through CSV files remains reliable for fast iteration.
- Regression guardrails and multi-seed validation process exist and were run during closeout.
- Draft playtest flow works end-to-end: draft roster export -> sim -> session logging.

## Known accepted Season 1 exceptions

- Some PvP mirrors remain out of ideal target bands.
- Some Hard pressure matchups remain above/below preferred caps depending on branch/seed.
- A few pacing metrics (for example exchange profile in specific matchups) can drift from target.
- These are documented and intentionally carried forward into Season 2.

## What recent draft tests showed

- First drafted pair was relatively close but not even (~53/47).
- Additional tested pairs had clear strength gaps (~69/31 and ~64/36 style splits).
- Against dummy gauntlet, draft teams separated clearly, especially vs Hard.
- Typical ending mix remains defense-heavy in many runs (frequent Stuffed and Deflect-related outcomes).

## Main design concern going into Season 2

- Gameplay feel risk: sessions can feel too slow and/or too repetitive.
- This appears to be both a pace issue and a texture issue:
  - Pace: time-to-resolution at the table.
  - Texture: repeated outcome patterns reducing excitement.

## Season 2 working direction (current thinking)

- Focus on physical playtest feel first, then bring validated ideas back into sim.
- Keep implementation conservative while experimenting IRL with draft and pacing knobs.
- Prioritize mechanics that increase tension and readability without adding heavy runtime complexity.

## Immediate practical workflow

1. Edit player names/abilities in one source: data/player_cards.csv.
2. Print full packet from root: all_cards.pdf.
3. Print delta packet from root: all_updated_cards.pdf (changed since baseline).
4. Run physical sessions, capture short notes on speed, excitement, and confusing moments.
5. Promote only proven table improvements into Season 2 sim changes.

## What to decide next

- What exact "fun and speed" success targets Season 2 should optimize for.
- Which 2-3 mechanics are first-class candidates for Season 2 implementation.
- Whether to keep draft as pre-match only or integrate deeper match-time effects later.
