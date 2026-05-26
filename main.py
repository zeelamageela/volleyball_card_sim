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


STRATEGIES = {
    "random": RandomStrategy,
    "smart": SmartStrategy,
}

DUMMY_ROSTER = Path("data/team_dummy.csv")


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
        default=None,
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
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> None:
    args = parse_args(argv)

    seed = args.seed
    if seed is None:
        seed = random.randint(0, 2**31 - 1)
        print(f"Using random seed: {seed}  (pass --seed {seed} to reproduce)")

    is_pvd = (args.mode == "pvd")

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
        engine_a = build_ability_engine(args.roster_a, player_cards)
        engine_b = build_ability_engine(args.roster_b, player_cards)
        if engine_a:
            print(f"Team A abilities loaded from {args.roster_a}")
        if engine_b:
            print(f"Team B abilities loaded from {args.roster_b}")

    name_b = "Dummy" if is_pvd else "Team B"
    sim = Simulation(
        strategy_a, strategy_b,
        n_games=args.games,
        seed=seed,
        engine_a=engine_a,
        engine_b=engine_b,
        name_a="Team A",
        name_b=name_b,
        use_hand_b=not is_pvd,
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
