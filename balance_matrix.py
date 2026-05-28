from __future__ import annotations

import argparse
import csv
import random
from pathlib import Path
from typing import Dict, Iterable, List, Tuple

from src.abilities import build_ability_engine, load_player_cards
from src.runtime_config import load_team_configs, resolve_team_runtime_config
from src.simulation import Simulation
from src.strategies import DummyStrategy, RandomStrategy, SmartStrategy


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Run CSV-driven balance matrix and export results to CSV."
    )
    p.add_argument("--games", type=int, default=1000, help="Games per matchup")
    p.add_argument("--seed", type=int, default=42, help="Base RNG seed")
    p.add_argument(
        "--mode",
        choices=["all", "pvp", "pvd"],
        default="all",
        help="all=PvP and PvD, pvp=player-vs-player only, pvd=player-vs-dummy only",
    )
    p.add_argument(
        "--player-strategy",
        choices=["smart", "random"],
        default="smart",
        help="Strategy used by teams with use_hand=true",
    )
    p.add_argument(
        "--dummy-strategy",
        choices=["dummy", "smart", "random"],
        default="dummy",
        help="Strategy used by teams with use_hand=false",
    )
    p.add_argument(
        "--player-cards",
        type=Path,
        default=Path("data/player_cards.csv"),
        help="Player cards CSV",
    )
    p.add_argument(
        "--teams-csv",
        type=Path,
        default=Path("data/teams.csv"),
        help="Teams CSV",
    )
    p.add_argument(
        "--team-passives-csv",
        type=Path,
        default=Path("data/team_passives.csv"),
        help="Team passives CSV",
    )
    p.add_argument(
        "--set-templates-csv",
        type=Path,
        default=Path("data/set_templates.csv"),
        help="Set templates CSV",
    )
    p.add_argument(
        "--output",
        type=Path,
        default=Path("balance_matrix_results.csv"),
        help="Output CSV for matchup results",
    )
    p.add_argument(
        "--team-a",
        action="append",
        default=[],
        help="Optional Team A name filter (repeatable)",
    )
    p.add_argument(
        "--team-b",
        action="append",
        default=[],
        help="Optional Team B name filter (repeatable)",
    )
    return p.parse_args()


def _strategy_for(name: str, rng: random.Random):
    if name == "smart":
        return SmartStrategy(rng)
    if name == "random":
        return RandomStrategy(rng)
    if name == "dummy":
        return DummyStrategy()
    raise ValueError(f"Unsupported strategy: {name}")


def _top_reasons(reason_counts: Dict[str, int], n: int = 3) -> str:
    ordered = sorted(reason_counts.items(), key=lambda x: -x[1])[:n]
    return " | ".join(f"{k}:{v}" for k, v in ordered)


def _iter_matchups(
    team_names: List[str],
    team_use_hand: Dict[str, bool],
    mode: str,
) -> Iterable[Tuple[str, str]]:
    for a in team_names:
        for b in team_names:
            if a == b:
                continue
            if mode in {"all", "pvp"} and team_use_hand[a] and team_use_hand[b]:
                yield (a, b)
            if mode in {"all", "pvd"} and team_use_hand[a] and not team_use_hand[b]:
                yield (a, b)


def main() -> None:
    args = parse_args()

    for pth in (args.player_cards, args.teams_csv, args.team_passives_csv, args.set_templates_csv):
        if not pth.exists():
            raise FileNotFoundError(f"Required file not found: {pth}")

    raw_teams = load_team_configs(args.teams_csv, args.team_passives_csv)
    if not raw_teams:
        raise RuntimeError("No teams loaded from teams CSV.")

    all_team_names = [cfg.team_name for cfg in raw_teams.values()]
    name_lookup = {name.lower(): name for name in all_team_names}

    selected_a = {name_lookup.get(t.lower(), t) for t in args.team_a} if args.team_a else set(all_team_names)
    selected_b = {name_lookup.get(t.lower(), t) for t in args.team_b} if args.team_b else set(all_team_names)

    team_use_hand = {cfg.team_name: cfg.use_hand for cfg in raw_teams.values()}

    player_cards = load_player_cards(args.player_cards)

    results: List[Dict[str, object]] = []
    matchup_index = 0
    for team_a_name, team_b_name in _iter_matchups(all_team_names, team_use_hand, args.mode):
        if team_a_name not in selected_a or team_b_name not in selected_b:
            continue

        cfg_a = resolve_team_runtime_config(
            roster_path=None,
            team_name=team_a_name,
            teams_csv=args.teams_csv,
            passives_csv=args.team_passives_csv,
            set_templates_csv=args.set_templates_csv,
        )
        cfg_b = resolve_team_runtime_config(
            roster_path=None,
            team_name=team_b_name,
            teams_csv=args.teams_csv,
            passives_csv=args.team_passives_csv,
            set_templates_csv=args.set_templates_csv,
        )

        engine_a = build_ability_engine(cfg_a.roster_path, player_cards)
        engine_b = build_ability_engine(cfg_b.roster_path, player_cards)

        sub_seed = args.seed + matchup_index
        rng_a = random.Random(sub_seed ^ 0xAAAA_AAAA)
        rng_b = random.Random(sub_seed ^ 0x5555_5555)
        strat_a_name = args.player_strategy if cfg_a.use_hand else args.dummy_strategy
        strat_b_name = args.player_strategy if cfg_b.use_hand else args.dummy_strategy
        strategy_a = _strategy_for(strat_a_name, rng_a)
        strategy_b = _strategy_for(strat_b_name, rng_b)

        sim = Simulation(
            strategy_a,
            strategy_b,
            n_games=args.games,
            seed=sub_seed,
            engine_a=engine_a,
            engine_b=engine_b,
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
        row = {
            "team_a": cfg_a.team_name,
            "team_b": cfg_b.team_name,
            "mode": "pvp" if cfg_a.use_hand and cfg_b.use_hand else "pvd",
            "games": args.games,
            "seed": sub_seed,
            "strategy_a": strat_a_name,
            "strategy_b": strat_b_name,
            "deck_type_a": cfg_a.deck_type,
            "deck_type_b": cfg_b.deck_type,
            "passive_a": cfg_a.passive_ability or "",
            "passive_b": cfg_b.passive_ability or "",
            "wins_a": wins_a,
            "wins_b": wins_b,
            "win_rate_a": round(wins_a / args.games * 100, 2),
            "win_rate_b": round(wins_b / args.games * 100, 2),
            "avg_rallies": round(stats.avg_rallies_per_game, 3),
            "avg_exchanges": round(stats.avg_exchanges_per_rally, 3),
            "top_endings": _top_reasons(stats.reason_counts),
        }
        results.append(row)
        matchup_index += 1
        print(
            f"{cfg_a.team_name:>8} vs {cfg_b.team_name:<8} "
            f"-> {row['win_rate_a']:>6.2f}% / {row['win_rate_b']:>6.2f}%"
        )

    if not results:
        print("No matchups selected. Check --mode/--team-a/--team-b filters.")
        return

    args.output.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = [
        "team_a", "team_b", "mode", "games", "seed",
        "strategy_a", "strategy_b",
        "deck_type_a", "deck_type_b",
        "passive_a", "passive_b",
        "wins_a", "wins_b", "win_rate_a", "win_rate_b",
        "avg_rallies", "avg_exchanges", "top_endings",
    ]
    with open(args.output, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(results)

    print(f"\nSaved {len(results)} matchup rows to {args.output}")


if __name__ == "__main__":
    main()
