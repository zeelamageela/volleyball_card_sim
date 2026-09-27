from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List


class AttackOutcomeType(Enum):
    KILL    = "kill"     # attack > block (ball breaks through to defender)
    DEFLECT = "deflect"  # attack == block, exact tie (ball falls to attacker's own side)
    STUFFED = "stuffed"  # block >= attack, not tied (clean block, defense wins rally)


class ChaseOutcome(Enum):
    SUCCESS = "success"  # running total reached target within the attempt cap
    FAILED  = "failed"   # attempts exhausted without reaching target → opponent wins the point


@dataclass
class ChaseResult:
    outcome: ChaseOutcome


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
