# Identity Roster Proposal (Next Pass)

Goal: preserve the shared dummy deck constraint while restoring distinct tier identities.

## Current locked (identity-constrained) config

- Easy: `team_dummy_easy.csv`, template `EasyLite`, passive `TBD`, deck `dummy`
- Medium: `team_dummy_medium.csv`, template `Hard`, passive `Back Court Threat`, deck `dummy`
- Hard: `team_dummy_hard.csv`, template `Medium`, passive `Elite Draw`, deck `dummy`

This configuration preserves shared dummy deck constraints, keeps all three dummy rosters distinct, and hits current PvD target bands.

## Proposed identity-forward tier fantasies

1. Easy - "Open lanes, low punish"
- Roster style: fewer high-punish attackers, weaker DS/Libero conversion.
- Template style: lower commitment pressure.
- Passive: defensive smoothing only.

2. Medium - "Lane traps and swing turns"
- Roster style: one high-threat attacker + one control blocker + reliable defense.
- Template style: mixed pressure templates.
- Passive: one pressure passive (not both draw and lane pressure).

3. Hard - "Relentless conversion"
- Roster style: stacked conversion engine (attack + block + dig).
- Template style: aggressive pressure templates.
- Passive: high consistency passive.

## Active roster identities

Easy (active)
- Roster: Flex (Setter), Tempo (OPP), Atlas (MB), Blaze (OH), Wall (DS), Reflexive (Libero)
- Template: EasyLite (single-hitter biased)
- Identity: lane access but reduced conversion density

Medium (active)
- Roster: Trigger, Razor, Ricochet, Havoc, Phantom, Lucky
- Template: Hard
- Identity: pressure spikes and lane traps

Hard (active)
- Roster: Flex, Glide, Swift, Tempo, Wall, Lucky
- Template: Medium
- Identity: consistent conversion engine

## Acceptance criteria

- Dummy deck remains `dummy` for all three tiers.
- Easy/Medium/Hard each have unique 6-player roster files.
- Tier win-rate bands remain in target on 500-game matrix at seed 42.
- Identity checklist passes in TEAM_IDENTITY_CHECKLIST.md.
- PvP Blitz vs Grind remains a separate unresolved target stream.
