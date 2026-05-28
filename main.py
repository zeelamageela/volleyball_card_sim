"""
volleyball_card_sim — CLI entry point

Usage examples:
  python main.py
  python main.py --games 10000 --seed 42
  python main.py --games 500 --seed 7 --verbose
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

from src.strategies import RandomStrategy, DummyStrategy, SmartStrategy
from src.simulation import Simulation
from src.abilities import load_player_cards, build_ability_engine
from src.runtime_config import resolve_team_runtime_config


STRATEGIES = {
    "random": RandomStrategy,
    "smart": SmartStrategy,
}

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Volleyball card game simulation engine",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--games", "-n",
        type=int,
        default=1000,
        help="Number of games to simulate (default: 1000)",
    )
    parser.add_argument(
        "--strategy-a",
        default="random",
        choices=list(STRATEGIES),
        help="Strategy for Team A (default: random)",
    )
    parser.add_argument(
        "--strategy-b",
        default="random",
        choices=list(STRATEGIES),
        help="Strategy for Team B (default: random)",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=None,
        help="RNG seed for reproducible results (default: random)",
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Print score distribution after the summary",
    )
    parser.add_argument(
        "--player-cards",
        type=Path,
        default=Path("data/player_cards.csv"),
        metavar="CSV",
        help="CSV file defining player cards and abilities (e.g. data/player_cards.csv)",
    )
    parser.add_argument(
        "--roster-a",
        type=Path,
        default=None,
        metavar="CSV",
        help="Team A roster CSV (player_name, role).  Requires --player-cards.",
    )
    parser.add_argument(
        "--roster-b",
        type=Path,
        default=None,
        metavar="CSV",
        help="Team B roster CSV (player_name, role).  Requires --player-cards.",
    )
    parser.add_argument(
        "--mode",
        choices=["pvp", "pvd"],
        default="pvp",
        help="pvp = player vs player (default); pvd = player vs dummy",
    )
    parser.add_argument(
        "--team-a-name",
        type=str,
        default="Blitz",
        help="Team A name from data/teams.csv (used when roster-a is omitted or for label/config lookup)",
    )
    parser.add_argument(
        "--team-b-name",
        type=str,
        default=None,
        help="Team B name from data/teams.csv (defaults to Grind for pvp, Easy for pvd)",
    )
    parser.add_argument(
        "--teams-csv",
        type=Path,
        default=Path("data/teams.csv"),
        help="Team configuration CSV",
    )
    parser.add_argument(
        "--team-passives-csv",
        type=Path,
        default=Path("data/team_passives.csv"),
        help="Team passives CSV",
    )
    parser.add_argument(
        "--set-templates-csv",
        type=Path,
        default=Path("data/set_templates.csv"),
        help="Set template CSV",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> None:
    args = parse_args(argv)

    seed = args.seed
    if seed is None:
        seed = random.randint(0, 2**31 - 1)
        print(f"Using random seed: {seed}  (pass --seed {seed} to reproduce)")

    is_pvd = (args.mode == "pvd")
    team_b_name = args.team_b_name or ("Easy" if is_pvd else "Grind")

    # Resolve runtime team settings from CSV source-of-truth files.
    cfg_a = resolve_team_runtime_config(
        roster_path=args.roster_a,
        team_name=args.team_a_name,
        teams_csv=args.teams_csv,
        passives_csv=args.team_passives_csv,
        set_templates_csv=args.set_templates_csv,
    )
    cfg_b = resolve_team_runtime_config(
        roster_path=args.roster_b,
        team_name=team_b_name,
        teams_csv=args.teams_csv,
        passives_csv=args.team_passives_csv,
        set_templates_csv=args.set_templates_csv,
    )

    strat_rng_a = random.Random(seed ^ 0xAAAA_AAAA)

    strategy_a = STRATEGIES[args.strategy_a](strat_rng_a)
    if is_pvd:
        strategy_b = DummyStrategy()
    else:
        strat_rng_b = random.Random(seed ^ 0x5555_5555)
        strategy_b = STRATEGIES[args.strategy_b](strat_rng_b)

    # Load optional ability engines
    engine_a = engine_b = None
    if args.player_cards:
        if not args.player_cards.exists():
            sys.exit(f"Error: player-cards file not found: {args.player_cards}")
        player_cards = load_player_cards(args.player_cards)
        if cfg_a.roster_path is not None and not cfg_a.roster_path.exists():
            sys.exit(f"Error: Team A roster file not found: {cfg_a.roster_path}")
        if cfg_b.roster_path is not None and not cfg_b.roster_path.exists():
            sys.exit(f"Error: Team B roster file not found: {cfg_b.roster_path}")
        engine_a = build_ability_engine(cfg_a.roster_path, player_cards)
        engine_b = build_ability_engine(cfg_b.roster_path, player_cards)
        if engine_a:
            print(f"Team A abilities loaded from {cfg_a.roster_path}")
        if engine_b:
            print(f"Team B abilities loaded from {cfg_b.roster_path}")

    sim = Simulation(
        strategy_a, strategy_b,
        n_games=args.games,
        seed=seed,
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

    mode_label = f"{args.strategy_a} vs dummy" if is_pvd else f"{args.strategy_a} vs {args.strategy_b}"
    print(f"\nRunning {args.games:,} games  ({mode_label})...\n")

    stats = sim.run()
    print(stats.summary())

    if args.verbose:
        from collections import Counter
        score_counter: Counter = Counter(stats.score_distribution)
        print("\nScore distribution (top 15):")
        for (a, b), count in score_counter.most_common(15):
            print(f"  {a:>2}–{b:<2}  {count:>6}x")


if __name__ == "__main__":
    main()
