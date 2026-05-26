"""
Interactive volleyball card game — play a game from the terminal.

Usage examples:
  python play.py
  python play.py --your-team data/team_a.csv --ai-team data/team_dummy_hard.csv
  python play.py --your-team data/team_a.csv --ai-team data/team_b.csv --verbose
  python play.py --your-team data/team_phase5.csv --ai-team data/team_b.csv --side b
"""
from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

# Allow running from the project root without installing the package
sys.path.insert(0, str(Path(__file__).parent))

from src.abilities import load_player_cards, build_ability_engine
from src.players import Team
from src.game import Rally, POINTS_TO_WIN
from src.strategies import SmartStrategy
from src.interactive import InteractiveStrategy, show_roster

CARDS_CSV = Path("data/player_cards.csv")


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Interactive volleyball card sim — one game at a time",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    p.add_argument(
        "--your-team", type=Path, default=Path("data/team_a.csv"),
        metavar="CSV", help="Your roster CSV (default: data/team_a.csv)",
    )
    p.add_argument(
        "--ai-team", type=Path, default=Path("data/team_b.csv"),
        metavar="CSV", help="AI roster CSV (default: data/team_b.csv)",
    )
    p.add_argument(
        "--side", choices=["a", "b"], default="a",
        help="Which position YOU play: a=left side, b=right side (default: a)",
    )
    p.add_argument(
        "--verbose", "-v", action="store_true",
        help="Show ability-trigger events as they fire during each rally",
    )
    p.add_argument(
        "--seed", type=int, default=None,
        help="RNG seed for reproducibility (default: random)",
    )
    return p.parse_args()


def _label(path: Path) -> str:
    return path.stem


def main() -> None:
    args = parse_args()

    # ── Validate paths ────────────────────────────────────────────────────────
    if not CARDS_CSV.exists():
        sys.exit(f"Error: player-cards file not found: {CARDS_CSV}")
    if not args.your_team.exists():
        sys.exit(f"Error: your team file not found: {args.your_team}")
    if not args.ai_team.exists():
        sys.exit(f"Error: AI team file not found: {args.ai_team}")

    # ── RNG ───────────────────────────────────────────────────────────────────
    seed = args.seed
    if seed is None:
        seed = random.randint(0, 2**31 - 1)
    master_rng = random.Random(seed)

    # ── Ability engines ───────────────────────────────────────────────────────
    player_cards = load_player_cards(CARDS_CSV)
    engine_you = build_ability_engine(args.your_team, player_cards)
    engine_ai  = build_ability_engine(args.ai_team,   player_cards)

    if args.verbose:
        if engine_you:
            engine_you.verbose = True
        if engine_ai:
            engine_ai.verbose = True

    # ── Strategies ────────────────────────────────────────────────────────────
    ai_rng      = random.Random(master_rng.randint(0, 2**31 - 1))
    narrative:  list = []
    human_strat = InteractiveStrategy(
        engine_you, engine_ai, verbose=args.verbose, narrative=narrative
    )
    ai_strat    = SmartStrategy(ai_rng)

    your_label = _label(args.your_team)
    ai_label   = _label(args.ai_team)

    # ── Banner ────────────────────────────────────────────────────────────────
    print()
    print("=" * 52)
    print("  VOLLEYBALL CARD SIM — Interactive Mode")
    print(f"  You    : {your_label}  (side {args.side.upper()})")
    print(f"  AI     : {ai_label}")
    if args.verbose:
        print("  Verbose: ON  — ability events shown as they fire")
    else:
        print("  Verbose: off  (re-run with --verbose/-v to enable)")
    print(f"  Seed   : {seed}")
    print("=" * 52)

    # ── Ability roster ────────────────────────────────────────────────────────
    show_roster(engine_you, your_label, engine_ai, ai_label)
    try:
        input("  [Press Enter to start the game]\n")
    except (KeyboardInterrupt, EOFError):
        return

    # ── Build teams ───────────────────────────────────────────────────────────
    team_rng_you = random.Random(master_rng.randint(0, 2**31 - 1))
    team_rng_ai  = random.Random(master_rng.randint(0, 2**31 - 1))

    team_you = Team(your_label, team_rng_you)
    team_ai  = Team(ai_label,   team_rng_ai)

    if engine_you:
        engine_you.reset()
        team_you.ability_engine = engine_you
    if engine_ai:
        engine_ai.reset()
        team_ai.ability_engine = engine_ai

    # Assign sides
    if args.side == "a":
        team_a, team_b = team_you, team_ai
        strat_a, strat_b = human_strat, ai_strat
    else:
        team_a, team_b = team_ai, team_you
        strat_a, strat_b = ai_strat, human_strat

    # ── Game loop (replicates Game.play() with pauses between rallies) ────────
    scores = {your_label: 0, ai_label: 0}
    game_rng = random.Random(master_rng.randint(0, 2**31 - 1))

    team_a.draw_starting_hand()
    team_b.draw_starting_hand()

    server: Team = game_rng.choice([team_a, team_b])
    rally_num = 0

    while max(scores.values()) < POINTS_TO_WIN:
        rally_num += 1
        receiving = team_b if server is team_a else team_a
        srv_strat = strat_a if server is team_a else strat_b
        rcv_strat = strat_b if server is team_a else strat_a

        rally = Rally(server, receiving, srv_strat, rcv_strat, game_rng,
                      narrative=narrative)
        result = rally.play()

        # Drain any remaining narrative (e.g. the final play that ended the rally)
        if narrative:
            print()
            for msg in narrative:
                print(msg)
            narrative.clear()

        scores[result.winner_name] += 1

        # Winner earns the serve
        server = team_a if result.winner_name == team_a.name else team_b

        you_pts = scores[your_label]
        ai_pts  = scores[ai_label]

        print(f"\n  >>> Rally {rally_num}: {result.reason}")
        print(f"      Score: {your_label} {you_pts}  —  {ai_pts} {ai_label}")

        if max(scores.values()) < POINTS_TO_WIN:
            try:
                input("\n  [Press Enter for next rally  |  Ctrl+C to quit]\n")
            except (KeyboardInterrupt, EOFError):
                print("\n  Game ended early.")
                return

    # ── Result ────────────────────────────────────────────────────────────────
    winner_name = max(scores, key=lambda k: scores[k])
    you_won = (winner_name == your_label)

    print()
    print("=" * 52)
    print(f"  GAME OVER — {'YOU WIN!' if you_won else 'AI wins.'}")
    print(
        f"  Final score: {your_label} {scores[your_label]}"
        f"  —  {scores[ai_label]} {ai_label}"
    )
    print(f"  Total rallies: {rally_num}")
    print("=" * 52)


if __name__ == "__main__":
    main()
