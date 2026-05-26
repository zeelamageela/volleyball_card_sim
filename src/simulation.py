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
    score_distribution: List[Tuple[int, int]]     # (score_a, score_b) per game
    reason_counts: Dict[str, int]                 # tally of rally end-reasons
    scored_by: Dict[str, Dict[str, int]] = field(default_factory=dict)
    # scored_by[team][category] = rally-win count
    # Categories: hit_kills, tip_kills, aces, stuffs, deflections,
    #             block_reads, opp_errors, other

    @property
    def win_rates(self) -> Dict[str, float]:
        return {name: w / self.n_games for name, w in self.wins.items()}

    @property
    def avg_rallies_per_game(self) -> float:
        return self.total_rallies / max(self.n_games, 1)

    @property
    def avg_exchanges_per_rally(self) -> float:
        return self.total_exchanges / max(self.total_rallies, 1)

    def rally_wins(self, team: str) -> int:
        return sum(self.scored_by.get(team, {}).values())

    def rally_losses(self, team: str) -> int:
        return self.total_rallies - self.rally_wins(team)

    def point_scoring_efficiency(self, team: str) -> float:
        """(Points Scored - Points Lost) / Total Attempts  (article formula)."""
        won  = self.rally_wins(team)
        lost = self.rally_losses(team)
        total = won + lost
        return (won - lost) / total if total > 0 else 0.0

    def win_loss_efficiency_ratio(self, team: str) -> float:
        """Points Scored / Points Lost  (article formula)."""
        won  = self.rally_wins(team)
        lost = self.rally_losses(team)
        return won / lost if lost > 0 else float('inf')

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
        ]

        # ── Point-Scoring Efficiency (article metrics) ───────────────────────
        _CAT_LABELS = [
            ("hit_kills",      "Hit kills"),
            ("tip_kills",      "Tip kills"),
            ("aces",           "Aces"),
            ("stuffs",         "Stuffs"),
            ("deflections",    "Deflections"),
            ("block_reads",    "Block reads"),
            ("opp_errors",     "Opp. errors"),
            ("other",          "Other"),
        ]
        lines.append("  Point-Scoring Efficiency (per rally):")
        for name in self.wins:
            won    = self.rally_wins(name)
            lost   = self.rally_losses(name)
            pse    = self.point_scoring_efficiency(name)
            ratio  = self.win_loss_efficiency_ratio(name)
            pse_str  = f"{pse:+.1%}"
            ratio_str = f"{ratio:.2f}x" if ratio != float('inf') else "∞"
            lines.append(
                f"    {name:<20}  {won:>6} scored / {lost:>6} lost"
                f"   PSE {pse_str}   eff ratio {ratio_str}"
            )
            cats = self.scored_by.get(name, {})
            total_scored = max(won, 1)
            for key, label in _CAT_LABELS:
                cnt = cats.get(key, 0)
                if cnt:
                    lines.append(f"      {label:<25}: {cnt:>6}  ({cnt/total_scored:.1%})")
        lines += [
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
        use_hand_a: bool = True,
        use_hand_b: bool = True,
    ) -> None:
        self._strat_a   = strategy_a
        self._strat_b   = strategy_b
        self._n_games   = n_games
        self._master_rng = random.Random(seed)
        self._engine_a  = engine_a
        self._engine_b  = engine_b
        self._name_a    = name_a
        self._name_b    = name_b
        self._use_hand_a = use_hand_a
        self._use_hand_b = use_hand_b

    def _child_rng(self) -> random.Random:
        return random.Random(self._master_rng.randint(0, 2**31 - 1))

    def run(self) -> SimStats:
        wins: Dict[str, int] = {self._name_a: 0, self._name_b: 0}
        total_rallies   = 0
        total_exchanges = 0
        score_dist: List[Tuple[int, int]] = []
        reason_counts: Counter = Counter()

        scored_by: Dict[str, Dict[str, int]] = {
            self._name_a: Counter(),
            self._name_b: Counter(),
        }

        for _ in range(self._n_games):
            # Use "dummy" deck for teams without hands (blind deck flips)
            deck_type_a = "dummy" if not self._use_hand_a else "standard"
            deck_type_b = "dummy" if not self._use_hand_b else "standard"
            team_a = Team(self._name_a, self._child_rng(), use_hand=self._use_hand_a, deck_type=deck_type_a)
            team_b = Team(self._name_b, self._child_rng(), use_hand=self._use_hand_b, deck_type=deck_type_b)
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
                reason_counts[_categorise_reason(r.reason)] += 1
                cat = _score_category(r.reason)
                if r.winner_name in scored_by:
                    scored_by[r.winner_name][cat] += 1

        return SimStats(
            n_games=self._n_games,
            wins=wins,
            total_rallies=total_rallies,
            total_exchanges=total_exchanges,
            score_distribution=score_dist,
            reason_counts=dict(reason_counts),
            scored_by={k: dict(v) for k, v in scored_by.items()},
        )


def _categorise_reason(reason: str) -> str:
    """Map a detailed rally reason string to a short display label."""
    lower = reason.lower()
    if "serve ace" in lower and "chase failed" in lower:
        return "Serve ace (chase failed)"
    if "serve ace" in lower:
        return "Serve ace"
    if "block read single attack lane" in lower:
        return "Block read (single lane)"
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


def _score_category(reason: str) -> str:
    """Map a rally reason to a point-scoring category.
    
    Attacking points: hit_kills, tip_kills
    Defensive points: stuffs, deflections
    """
    lower = reason.lower()
    if "serve ace" in lower:
        return "aces"
    if "block read single attack lane" in lower:
        return "block_reads"
    if "stuffed" in lower:
        return "stuffs"
    if "deflect not dug" in lower:
        return "deflections"
    if "tip not dug" in lower or "roll shot" in lower or "seam shot" in lower:
        return "tip_kills"
    if "wipe" in lower or "no chase" in lower or "kill" in lower or "dig failed" in lower:
        return "hit_kills"
    if "offensive confusion" in lower or "attacker-attacker" in lower \
            or "front+back" in lower or "all attacks canceled" in lower:
        return "opp_errors"
    return "other"
