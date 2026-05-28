"""
report.py  —  Player ability analysis and team balance report.

Usage:
    python report.py
    python report.py --player-cards data/player_cards.csv \
                     --roster-a data/team_blitz.csv \
                     --roster-b data/team_grind.csv \
                     --sim-games 2000 --seed 42
"""

import argparse
import csv
import random
import sys
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from src.runtime_config import resolve_team_runtime_config

# ── Default paths ─────────────────────────────────────────────────────────────

DEFAULT_CARDS  = Path("data/player_cards.csv")
DEFAULT_A      = Path("data/team_blitz.csv")
DEFAULT_B      = Path("data/team_grind.csv")

# ── Impact weights ─────────────────────────────────────────────────────────────
#
# Heuristic estimate of per-game value contributed per 1 unit of effect_value.
# Based on how often each trigger fires across a typical 25-rally, 4-exchange
# game and how pivotal each effect is to changing outcomes.
#
EFFECT_WEIGHTS: Dict[str, float] = {
    "attack_value_bonus":     2.5,   # fires every attack
    "set_value_delta":        2.5,   # fires every set → directly lifts attack
    "serve_value_bonus":      1.5,   # fires each serve (roughly half of plays)
    "block_value_bonus":      2.0,   # fires every time this player blocks
    "adjacent_block_bonus":   2.5,   # lifts TWO lanes simultaneously
    "pierce_block":           7.0,   # big one-shot effect (conditional, flat)
    "single_block_only":      5.0,   # neutralises double-block advantage (flat)
    "chase_card_bonus":       1.5,   # fires on chase attempts
    "dig_threshold":          2.5,   # fires every kill-dig attempt
    "deflect_dig_threshold":  2.0,   # fires on deflects (~15 % of rallies)
    "tip_value_bonus":        2.0,   # fires on tips
    "tip_dig_threshold":      1.5,   # fires when defending tips
    # ── New game-modifying shot mechanics ─────────────────────────────────────
    "wipe_block":             4.0,   # instant point on card=1 into a block (flat)
    "no_chase":               5.0,   # kills with high cards skip chase entirely (flat)
    "roll_shot":              4.0,   # goes over block, dug like a tip (flat)
    "heavy_spin":             6.0,   # block bypassed + no chase on fail (flat)
    "seam_shot":              5.0,   # deflect outcomes become instant attacker win (flat)
    "tip_threshold_delta":    3.0,   # per-point expansion of the tip range (on high set)
    # ── Hand/structural ability mechanics ─────────────────────────────────────
    "over_block_bonus":       3.0,   # conditional attack boost vs double block
    "hold_card":              4.0,   # resource preservation (held card = tempo edge)
    "hand_peek":              2.0,   # information advantage before card commitment
    "exchange_card":          3.0,   # resource flexibility via deck swap
    "hand_size_mod":          2.0,   # per-point persistent hand size shift
}

# Effects where effect_value is a boolean flag — use flat weight only
BOOLEAN_EFFECTS = {"pierce_block", "single_block_only",
                   "wipe_block", "no_chase", "roll_shot", "heavy_spin", "seam_shot",
                   "hold_card", "hand_peek", "exchange_card"}

# Multipliers for triggers that fire less often
TRIGGER_MULT: Dict[str, float] = {
    "on_serve":             0.55,  # only fires when this team is serving
    "on_chase":             0.25,  # ~25 % of rallies reach a chase
    "on_dig_failure":       0.15,  # conditional on dig failing first
    "on_block_deflection":  0.20,  # deflects are a subset of blocks
    "on_tip":               0.20,  # tips happen ~10-15 % of attacks
    "on_quick_set":         0.35,  # only when MB is in the eligible lanes
    "on_dig_success":       0.40,  # fires on successful kill digs only
}

# Conditional-ability discount (applied when condition_field is set)
CONDITION_DISCOUNT = 0.60


# ── Data loading ──────────────────────────────────────────────────────────────

def load_player_cards_raw(path: Path):
    """Return list of raw row dicts from player_cards CSV."""
    rows = []
    with open(path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            name = row["player_name"].strip()
            if name:
                rows.append(row)
    return rows


def load_roster_names(path: Path) -> Dict[str, str]:
    """Return {player_name: role_string} from a roster CSV."""
    result = {}
    with open(path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            result[row["player_name"].strip()] = row["role"].strip()
    return result


# ── Impact scoring ─────────────────────────────────────────────────────────────

def ability_impact(trigger: str, condition_field: str, effect: str,
                   effect_value: int) -> float:
    """Compute a heuristic impact score for one ability row."""
    weight = EFFECT_WEIGHTS.get(effect, 1.0)
    # Boolean effects: flat weight, ignore effect_value
    if effect in BOOLEAN_EFFECTS:
        score = weight
    else:
        score = weight * effect_value
    # Apply trigger frequency multiplier
    score *= TRIGGER_MULT.get(trigger, 1.0)
    # Apply conditional discount
    if condition_field.strip():
        score *= CONDITION_DISCOUNT
    return round(score, 2)


# ── Formatting helpers ─────────────────────────────────────────────────────────

ROLE_ORDER = ["Setter", "OPP", "MB", "OH", "DS", "Libero"]
COL_W = 78

def bar(char="─", width=COL_W):
    return char * width

def header(text: str):
    pad = max(0, COL_W - len(text) - 2)
    return f"  {text}{'':>{pad}}"


def fmt_condition(field: str, value: str) -> str:
    if not field:
        return ""
    return f"  [{field} {value}]"


def fmt_effect(effect: str, value: int) -> str:
    if effect in BOOLEAN_EFFECTS:
        return effect
    sign = "+" if value >= 0 else ""
    return f"{effect} {sign}{value}"


# ── Main report ───────────────────────────────────────────────────────────────

def build_player_data(cards_path: Path) -> Dict[str, dict]:
    """
    Returns {player_name: {"role": str, "abilities": [...], "total_score": float}}
    where each ability is {"name", "trigger", "cond_field", "cond_value",
                           "effect", "effect_value", "score"}.
    """
    rows = load_player_cards_raw(cards_path)
    players: Dict[str, dict] = {}
    for row in rows:
        name       = row["player_name"].strip()
        role       = row["role"].strip()
        ab_name    = row.get("ability_name", "").strip()
        trigger    = row.get("trigger", "").strip()
        cond_field = row.get("condition_field", "").strip()
        cond_value = row.get("condition_value", "").strip()
        effect     = row.get("effect", "").strip()
        try:
            eff_val = int(row.get("effect_value", 0) or 0)
        except ValueError:
            eff_val = 0

        if name not in players:
            players[name] = {"role": role, "abilities": [], "total_score": 0.0}

        if not ab_name:
            continue  # plain player row, no ability

        score = ability_impact(trigger, cond_field, effect, eff_val)
        players[name]["abilities"].append({
            "name":       ab_name,
            "trigger":    trigger,
            "cond_field": cond_field,
            "cond_value": cond_value,
            "effect":     effect,
            "eff_val":    eff_val,
            "score":      score,
        })
        players[name]["total_score"] = round(
            players[name]["total_score"] + score, 2
        )
    return players


def print_team(team_name: str, roster: Dict[str, str],
               players: Dict[str, dict]) -> float:
    """Print one team block and return the team's total score."""
    print(f"\n  {'━' * (COL_W - 2)}")
    print(f"  {team_name}")
    print(f"  {'━' * (COL_W - 2)}")

    team_total = 0.0
    for role_str in ROLE_ORDER:
        # Find the player on this team with this role
        player_name = next(
            (n for n, r in roster.items() if r.lower() == role_str.lower()), None
        )
        if player_name is None:
            continue
        pdata = players.get(player_name, {"role": role_str, "abilities": [],
                                           "total_score": 0.0})
        score = pdata["total_score"]
        team_total += score

        # Player header line
        name_role = f"{player_name}  [{role_str}]"
        score_str = f"Score: {score:.1f}"
        gap = max(2, COL_W - 4 - len(name_role) - len(score_str))
        print(f"\n  {name_role}{' ' * gap}{score_str}")

        if not pdata["abilities"]:
            print(f"    (no abilities)")
            continue

        for ab in pdata["abilities"]:
            cond = fmt_condition(ab["cond_field"], ab["cond_value"])
            eff  = fmt_effect(ab["effect"], ab["eff_val"])
            line = f"    {ab['name']:<22}  {ab['trigger']:<22}  {eff}"
            if cond:
                line += f"  {cond}"
            score_part = f"→ {ab['score']:.1f}"
            line = f"{line:<70}  {score_part}"
            print(line)

    print()
    print(f"  {'─' * (COL_W - 2)}")
    print(f"  Team Total Score: {team_total:.1f}")
    return round(team_total, 1)


def print_matchups(roster_a: Dict[str, str], roster_b: Dict[str, str],
                   players: Dict[str, dict], score_a: float, score_b: float):
    """Print role-by-role matchup table."""
    print(f"\n  {'━' * (COL_W - 2)}")
    print(f"  POSITION MATCHUPS")
    print(f"  {'━' * (COL_W - 2)}")
    print(f"  {'Role':<8}  {'Team A':^26}  {'Team B':^26}  {'Edge':>10}")
    print(f"  {'─' * (COL_W - 2)}")

    for role_str in ROLE_ORDER:
        name_a = next((n for n, r in roster_a.items()
                       if r.lower() == role_str.lower()), "—")
        name_b = next((n for n, r in roster_b.items()
                       if r.lower() == role_str.lower()), "—")
        s_a = players.get(name_a, {}).get("total_score", 0.0)
        s_b = players.get(name_b, {}).get("total_score", 0.0)
        diff = s_a - s_b
        if abs(diff) < 0.1:
            edge = "Tied"
        elif diff > 0:
            edge = f"A +{diff:.1f}"
        else:
            edge = f"B +{abs(diff):.1f}"
        a_col = f"{name_a} ({s_a:.1f})"
        b_col = f"{name_b} ({s_b:.1f})"
        print(f"  {role_str:<8}  {a_col:<26}  {b_col:<26}  {edge:>10}")

    print(f"  {'─' * (COL_W - 2)}")
    diff_total = score_a - score_b
    if abs(diff_total) < 0.1:
        total_edge = "Tied"
    elif diff_total > 0:
        total_edge = f"A +{diff_total:.1f}"
    else:
        total_edge = f"B +{abs(diff_total):.1f}"
    print(f"  {'TOTAL':<8}  {'Team A (' + str(score_a) + ')':^26}  "
          f"{'Team B (' + str(score_b) + ')':^26}  {total_edge:>10}")


def print_effect_legend():
    print(f"\n  {'━' * (COL_W - 2)}")
    print(f"  IMPACT SCORE NOTES")
    print(f"  {'━' * (COL_W - 2)}")
    print(f"  Scores are heuristic estimates based on effect strength × trigger")
    print(f"  frequency × conditional probability.  Higher = more game impact.")
    print()
    rows = [
        ("attack_value_bonus",    "2.5 / pt",  "fires every attack"),
        ("set_value_delta",       "2.5 / pt",  "fires every set"),
        ("dig_threshold",         "2.5 / pt",  "fires every kill-dig"),
        ("adjacent_block_bonus",  "2.5 / pt",  "affects TWO lanes"),
        ("pierce_block",          "7.0 flat",  "conditional — big swing"),
        ("single_block_only",     "5.0 flat",  "neutralises double block"),
        ("block_value_bonus",     "2.0 / pt",  "fires every block"),
        ("serve_value_bonus",     "1.5 / pt × 0.55 (serve freq)",  ""),
        ("chase_card_bonus",      "1.5 / pt × freq",               "on_chase or on_dig_failure"),
        ("deflect_dig_threshold", "2.0 / pt × 0.20",               "deflects only"),
        ("tip_value_bonus",       "2.0 / pt × 0.20",               "tips only"),
        ("tip_threshold_delta",   "3.0 / pt × on_set",             "expands tip range on high set"),
        ("roll_shot",             "4.0 flat",  "goes over block, dug like a tip"),
        ("heavy_spin",            "6.0 flat",  "block bypassed + no chase on fail"),
        ("seam_shot",             "5.0 flat",  "deflect → instant attacker win"),
        ("no_chase",              "5.0 flat",  "kill dig fail skips chase"),
        ("wipe_block",            "4.0 flat",  "card=1 into block → instant win"),
    ]
    for eff, wt, note in rows:
        note_str = f"  ({note})" if note else ""
        print(f"    {eff:<26}  {wt:<28}{note_str}")


def run_sim(cards_path: Path, roster_a_path: Path, roster_b_path: Path,
            n_games: int, seed: int) -> Tuple[float, float]:
    """Run a quick simulation and return (win_pct_a, win_pct_b)."""
    try:
        import src.abilities as ab_mod
        import src.simulation as sim_mod
        import src.strategies as strat_mod
        player_cards = ab_mod.load_player_cards(cards_path)
        cfg_a = resolve_team_runtime_config(roster_a_path, None)
        cfg_b = resolve_team_runtime_config(roster_b_path, None)
        engine_a = ab_mod.build_ability_engine(cfg_a.roster_path, player_cards)
        engine_b = ab_mod.build_ability_engine(cfg_b.roster_path, player_cards)
        strat_rng = random.Random(seed)
        strategy_a = strat_mod.RandomStrategy(random.Random(seed ^ 0xAAAA_AAAA))
        strategy_b = strat_mod.RandomStrategy(random.Random(seed ^ 0x5555_5555))
        sim = sim_mod.Simulation(
            strategy_a, strategy_b,
            n_games=n_games, seed=seed,
            engine_a=engine_a, engine_b=engine_b,
            name_a=cfg_a.team_name,
            name_b=cfg_b.team_name,
            use_hand_a=cfg_a.use_hand,
            use_hand_b=cfg_b.use_hand,
            deck_type_a=cfg_a.deck_type,
            deck_type_b=cfg_b.deck_type,
            passive_ability_a=cfg_a.passive_ability,
            passive_ability_b=cfg_b.passive_ability,
            setter_templates_a=cfg_a.setter_templates,
            setter_templates_b=cfg_b.setter_templates,
            broken_play_templates_a=cfg_a.broken_play_templates,
            broken_play_templates_b=cfg_b.broken_play_templates,
        )
        stats = sim.run()
        wins_a = stats.wins.get(cfg_a.team_name, 0)
        wins_b = stats.wins.get(cfg_b.team_name, 0)
        return round(wins_a / n_games * 100, 1), round(wins_b / n_games * 100, 1)
    except Exception as exc:
        print(f"\n  [sim error: {exc}]")
        return 0.0, 0.0


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Player ability balance report")
    parser.add_argument("--player-cards", type=Path, default=DEFAULT_CARDS)
    parser.add_argument("--roster-a",     type=Path, default=DEFAULT_A)
    parser.add_argument("--roster-b",     type=Path, default=DEFAULT_B)
    parser.add_argument("--sim-games",    type=int,  default=2000,
                        help="Number of games to simulate (0 = skip sim)")
    parser.add_argument("--seed",         type=int,  default=42)
    args = parser.parse_args()

    for p in (args.player_cards, args.roster_a, args.roster_b):
        if not p.exists():
            sys.exit(f"File not found: {p}")

    players  = build_player_data(args.player_cards)
    roster_a = load_roster_names(args.roster_a)
    roster_b = load_roster_names(args.roster_b)

    print()
    print("=" * COL_W)
    print("  VOLLEYBALL CARD SIM  —  PLAYER ABILITY REPORT")
    print("=" * COL_W)

    score_a = print_team("TEAM A", roster_a, players)
    score_b = print_team("TEAM B", roster_b, players)

    print_matchups(roster_a, roster_b, players, score_a, score_b)
    print_effect_legend()

    if args.sim_games > 0:
        print(f"\n  {'━' * (COL_W - 2)}")
        print(f"  SIMULATION RESULTS  ({args.sim_games:,} games, seed {args.seed})")
        print(f"  {'━' * (COL_W - 2)}")
        print(f"  Running...", end="", flush=True)
        pct_a, pct_b = run_sim(
            args.player_cards, args.roster_a, args.roster_b,
            args.sim_games, args.seed
        )
        print(f"\r  Team A: {pct_a:.1f}%   Team B: {pct_b:.1f}%"
              f"{'':>30}")

    print()
    print("=" * COL_W)
    print()


if __name__ == "__main__":
    main()
