#!/usr/bin/env python3
"""
analyze_meta.py — Meta snapshot for volleyball_card_sim
Run:  .venv/Scripts/python.exe analyze_meta.py
"""
import random, sys, io
from collections import Counter
from pathlib import Path

# Redirect stdout to a UTF-8 file
_out = io.TextIOWrapper(
    open(Path(__file__).parent / "meta_results.txt", "wb"),
    encoding="utf-8", line_buffering=True
)
sys.stdout = _out
sys.stderr = _out

sys.path.insert(0, str(Path(__file__).parent))

from src.abilities import load_player_cards, AbilityEngine, PlayerCard
from src.players  import PlayerRole
from src.strategies import SmartStrategy
from src.simulation import Simulation

CARDS_CSV = Path("data/player_cards.csv")
SEED = 2026

# ── load & split merged "Breaker" into OH and OPP variants ──────────────────
all_cards = load_player_cards(CARDS_CSV)
_bk = all_cards["Breaker"]
all_cards["Breaker(OH)"]  = PlayerCard(player_name="Breaker(OH)",  role_name="OH",
    abilities=[a for a in _bk.abilities if a.ability_name in ("Solo Shot","Lane Split")])
all_cards["Breaker(OPP)"] = PlayerCard(player_name="Breaker(OPP)", role_name="OPP",
    abilities=[a for a in _bk.abilities if a.ability_name in ("High Standard","Smash")])

# ── player lists by intended role ────────────────────────────────────────────
ROLE_PLAYERS = {
    PlayerRole.SETTER: ["Hammer","Maestro","Echo","Lancer","Vex","Conductor"],
    PlayerRole.OPP:    ["Crusher","Phantom","Thread","Cannon","Clutch","Bandit",
                        "Flip","Titan","Mirage","Breaker(OPP)"],
    PlayerRole.MB:     ["Fortress","Wall","Anchor","Shift","Atlas","Flex","Shield","Prism"],
    PlayerRole.OH:     ["Blade","Spike","Roller","Grit","Shadow","Trickster",
                        "Drift","Quantum","Edge","Breaker(OH)"],
    PlayerRole.DS:     ["Hustle","Ghost","Scrap","Pierce","Rocket","Laser","Thief","Surge"],
    PlayerRole.LIBERO: ["Hawk","Spider","Hope","Mirror","Vault"],
}

ROLE_LABEL = {
    PlayerRole.SETTER: "Setter",
    PlayerRole.OPP:    "OPP",
    PlayerRole.MB:     "MB",
    PlayerRole.OH:     "OH",
    PlayerRole.DS:     "DS",
    PlayerRole.LIBERO: "Libero",
}

# ── reference team (all high-tier; used as the fixed opponent) ───────────────
REF = {
    PlayerRole.SETTER: "Conductor",
    PlayerRole.OPP:    "Titan",
    PlayerRole.MB:     "Atlas",
    PlayerRole.OH:     "Quantum",
    PlayerRole.DS:     "Surge",
    PlayerRole.LIBERO: "Vault",
}

def make_engine(assignment: dict) -> AbilityEngine:
    return AbilityEngine({role: all_cards[name] for role, name in assignment.items()})

def run(eng_a: AbilityEngine, eng_b: AbilityEngine, n: int, seed: int = SEED):
    sa = SmartStrategy(random.Random(seed ^ 0xAAAA_AAAA))
    sb = SmartStrategy(random.Random(seed ^ 0x5555_5555))
    sim = Simulation(sa, sb, n_games=n, seed=seed,
                     engine_a=eng_a, engine_b=eng_b, name_a="A", name_b="B")
    s = sim.run()
    return s.wins.get("A", 0) / n, s


# ═════════════════════════════════════════════════════════════════════════════
W = 65
print("=" * W)
print("   VOLLEYBALL CARD SIM - META SNAPSHOT  (seed={})".format(SEED))
print("=" * W)

# =============================================================================
# 1.  RALLY ENDING DISTRIBUTION
# =============================================================================
print("\n-- 1. RALLY ENDING DISTRIBUTION  (ref vs ref, 3 000 games) --\n")
ref_eng_a = make_engine(REF)
ref_eng_b = make_engine(REF)
_, big = run(ref_eng_a, ref_eng_b, 3000)

buckets: Counter = Counter()
for reason, cnt in big.reason_counts.items():
    r = reason.lower()
    if reason == "Stuffed":
        buckets["Stuffed (block wins)"] += cnt
    elif reason == "Deflect not dug":
        buckets["Deflect not dug (block wins)"] += cnt
    elif reason == "Kill (chase failed)":
        buckets["Kill — chase failed (atk wins)"] += cnt
    elif reason == "Kill (dig failed)":
        # After _categorise_reason, 'Kill, no chase' reasons also land here
        buckets["Kill — no chase (atk wins)"] += cnt
    elif reason == "Tip not dug":
        buckets["Tip not dug (atk wins)"] += cnt
    elif "serve ace" in r:
        buckets["Serve ace"] += cnt
    elif "rally limit" in r:
        buckets["Rally limit reached"] += cnt
    elif "wipe" in r:
        buckets["Wipe off block (atk wins)"] += cnt
    elif "roll shot" in r:
        buckets["Roll shot (atk wins)"] += cnt
    elif "seam" in r:
        buckets["Seam shot (atk wins)"] += cnt
    elif "deflection out" in r or "matches blocker" in r:
        buckets["Match deflection (atk wins)"] += cnt
    elif "offensive confusion" in r or "attackers match" in r:
        buckets["Atk-atk cancel (def wins)"] += cnt
    elif "cancel" in r:
        buckets["All lanes cancelled (def wins)"] += cnt
    else:
        buckets[f"Other: {reason[:40]}"] += cnt

total_rallies = sum(buckets.values())
bar_scale = 40 / max(buckets.values())
for cat, cnt in sorted(buckets.items(), key=lambda x: -x[1]):
    bar = "|" * int(cnt * bar_scale)
    print(f"  {cat:<42} {cnt:>6}  {cnt/total_rallies:5.1%}  {bar}")
print(f"\n  Total rallies : {total_rallies:,}   Avg per game : {total_rallies/3000:.1f}")
print(f"  Avg exchanges : {big.avg_exchanges_per_rally:.2f}")
_out.flush()

# =============================================================================
# 2.  PLAYER RANKINGS BY POSITION
# =============================================================================
print("\n\n-- 2. PLAYER RANKINGS BY POSITION  (500 games each vs full ref team) --")
print("\n   How to read: Team A swaps ONE position with the candidate player;")
print("   Team B is always the full reference team.  * = the reference player.\n")

opp_eng_b = make_engine(REF)
rankings: dict = {}
GAMES_PER_RANK = 300  # fast enough, ~14k games total

for role, players in ROLE_PLAYERS.items():
    label = ROLE_LABEL[role]
    results = []
    for name in players:
        assignment = dict(REF)
        assignment[role] = name
        wr, _ = run(make_engine(assignment), opp_eng_b, GAMES_PER_RANK)
        results.append((name, wr))
    results.sort(key=lambda x: -x[1])
    rankings[role] = results

    print(f"  {label}:")
    bar_w = 25
    for i, (name, wr) in enumerate(results):
        star = "*" if name == REF[role] else " "
        bar  = "#" * int(wr * bar_w)
        tier = ("S" if wr >= 0.56 else
                "A" if wr >= 0.50 else
                "B" if wr >= 0.44 else "C")
        print(f"    {i+1:2}. {star}{name:<16} {wr:5.1%}  [{tier}]  {bar}")
    print()
    _out.flush()

# =============================================================================
# 3. OPTIMAL TEAM (greedy pick - top player at each position)
# =============================================================================
print("-- 3. OPTIMAL TEAM  (greedy: best per-position pick) --\n")

best_team = {role: rankings[role][0][0] for role in ROLE_PLAYERS}
print("  Roster:")
col_w = max(len(n) for n in best_team.values()) + 2
for role, name in best_team.items():
    ref_name = REF[role]
    note = "" if name == ref_name else f"  ← upgraded from {ref_name}"
    print(f"    {ROLE_LABEL[role]:<8}: {name:<{col_w}}{note}")

# Validate: best team vs reference
wr_best, _ = run(make_engine(best_team), make_engine(REF), 1000, seed=SEED + 1)
print(f"\n  Win rate vs full reference team: {wr_best:.1%}  (1 000 games)")

# Also check best vs itself (sanity: should be ~50%)
wr_mirror, _ = run(make_engine(best_team), make_engine(best_team), 500, seed=SEED + 2)
print(f"  Win rate in mirror match:        {wr_mirror:.1%}  (500 games, expect ~50%)")
_out.flush()
print()
