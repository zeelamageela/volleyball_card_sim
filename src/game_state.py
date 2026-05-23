from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List


class AttackOutcomeType(Enum):
    KILL    = "kill"     # attack > block (ball breaks through to defender)
    DEFLECT = "deflect"  # block − attack in {1, 2} (ball deflects back to attacker)
    STUFFED = "stuffed"  # block − attack >= 3 (clean block, defense wins rally)


class ChaseOutcome(Enum):
    ARMED_ATTACK = "armed_attack"  # first chase card brings total to target → arm OH/OPP
    FREE_BALL    = "free_ball"     # second chase card brings total to target → free ball over
    FAILED       = "failed"        # both chase cards fail → attacking team wins the point


@dataclass
class ChaseResult:
    outcome: ChaseOutcome
    armed_lane: int = 0  # lane 1 (OH) or 3 (OPP); meaningful only for ARMED_ATTACK


@dataclass
class RallyResult:
    winner_name: str
    reason: str
    rally_length: int   # number of attack exchanges before the point was scored


@dataclass
class GameResult:
    winner_name: str
    scores: Dict[str, int]
    rally_results: List[RallyResult] = field(default_factory=list)

    @property
    def total_rallies(self) -> int:
        return len(self.rally_results)

    @property
    def avg_rally_length(self) -> float:
        if not self.rally_results:
            return 0.0
        return sum(r.rally_length for r in self.rally_results) / len(self.rally_results)
