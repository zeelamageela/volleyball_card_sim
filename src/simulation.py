from __future__ import annotations

import random
from collections import Counter
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple, Type

from .players import Team
from .game import Game
from .game_state import GameResult
from .strategies import BaseStrategy, RandomStrategy
from .abilities import AbilityEngine


@dataclass
class SimStats:
    n_games: int
    wins: Dict[str, int]
    total_rallies: int
    total_exchanges: int
    score_distribution: List[Tuple[int, int]]   # (score_a, score_b) per game
    reason_counts: Dict[str, int]               # tally of rally end-reasons

    @property
    def win_rates(self) -> Dict[str, float]:
        return {name: w / self.n_games for name, w in self.wins.items()}

    @property
    def avg_rallies_per_game(self) -> float:
        return self.total_rallies / max(self.n_games, 1)

    @property
    def avg_exchanges_per_rally(self) -> float:
        return self.total_exchanges / max(self.total_rallies, 1)

    def summary(self) -> str:
        lines = [
            f"{'─' * 40}",
            f"  Games played        : {self.n_games}",
        ]
        for name, wins in self.wins.items():
            lines.append(
                f"  {name:<20}: {wins:>5} wins  ({self.win_rates[name]:.1%})"
            )
        lines += [
            f"  Avg rallies/game   : {self.avg_rallies_per_game:.1f}",
            f"  Avg exchanges/rally: {self.avg_exchanges_per_rally:.2f}",
            f"{'─' * 40}",
            "  Rally endings (all types):",
        ]
        for reason, count in sorted(
            self.reason_counts.items(), key=lambda x: -x[1]
        ):
            lines.append(f"    {count:>6}x  {reason}")
        lines.append(f"{'─' * 40}")
        return "\n".join(lines)


class Simulation:
    """
    Runs N games between two strategies and collects statistics.

    A fresh Team (and therefore a fresh Deck) is created for every game.
    All randomness derives from a single seeded master RNG for reproducibility.
    """

    def __init__(
        self,
        strategy_a: BaseStrategy,
        strategy_b: BaseStrategy,
        n_games: int,
        seed: Optional[int] = None,
        engine_a: Optional[AbilityEngine] = None,
        engine_b: Optional[AbilityEngine] = None,
        name_a: str = "Team A",
        name_b: str = "Team B",
    ) -> None:
        self._strat_a   = strategy_a
        self._strat_b   = strategy_b
        self._n_games   = n_games
        self._master_rng = random.Random(seed)
        self._engine_a  = engine_a
        self._engine_b  = engine_b
        self._name_a    = name_a
        self._name_b    = name_b

    def _child_rng(self) -> random.Random:
        return random.Random(self._master_rng.randint(0, 2**31 - 1))

    def run(self) -> SimStats:
        wins: Dict[str, int] = {self._name_a: 0, self._name_b: 0}
        total_rallies   = 0
        total_exchanges = 0
        score_dist: List[Tuple[int, int]] = []
        reason_counts: Counter = Counter()

        for _ in range(self._n_games):
            team_a = Team(self._name_a, self._child_rng())
            team_b = Team(self._name_b, self._child_rng())
            # Attach ability engines (reset per-game state first)
            if self._engine_a:
                self._engine_a.reset()
                team_a.ability_engine = self._engine_a
            if self._engine_b:
                self._engine_b.reset()
                team_b.ability_engine = self._engine_b
            game   = Game(team_a, team_b, self._strat_a, self._strat_b, self._child_rng())

            result: GameResult = game.play()
            wins[result.winner_name] += 1
            score_dist.append((result.scores[self._name_a], result.scores[self._name_b]))
            total_rallies   += result.total_rallies
            total_exchanges += sum(r.rally_length for r in result.rally_results)
            for r in result.rally_results:
                # Bucket the reason to a short category for readability
                reason_counts[_categorise_reason(r.reason)] += 1

        return SimStats(
            n_games=self._n_games,
            wins=wins,
            total_rallies=total_rallies,
            total_exchanges=total_exchanges,
            score_distribution=score_dist,
            reason_counts=dict(reason_counts),
        )


def _categorise_reason(reason: str) -> str:
    """Map a detailed rally reason string to a short category label."""
    lower = reason.lower()
    if "serve ace" in lower and "chase failed" in lower:
        return "Serve ace (chase failed)"
    if "serve ace" in lower:
        return "Serve ace"
    if "stuffed" in lower:
        return "Stuffed"
    if "deflect not dug" in lower:
        return "Deflect not dug"
    if "chase failed" in lower:
        return "Kill (chase failed)"
    if "kill" in lower:
        return "Kill (dig failed)"
    if "tip not dug" in lower:
        return "Tip not dug"
    if "rally limit" in lower:
        return "Rally limit reached"
    return reason
