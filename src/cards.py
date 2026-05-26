from __future__ import annotations

import random
from dataclasses import dataclass
from typing import List, Optional

HAND_SIZE = 5


@dataclass(frozen=True)
class Card:
    value: int   # 1–10
    color: str   # 'red' | 'black'

    def __str__(self) -> str:
        return f"{self.value}{self.color[0].upper()}"

    def __repr__(self) -> str:
        return f"Card({self.value}, {self.color!r})"


class Deck:
    """
    28-card buffed deck.

    Counts per value:
      1×1  2×2  3×3  4×4  4×5  4×6  4×7  3×8  2×9  1×10

    Rationale: extra mid-range cards (4–7) reduce tippable low cards; rare aces
    and a single 10 keep extreme values meaningful without dominating.
    """

    _COUNTS: dict = {1: 1, 2: 2, 3: 3, 4: 4, 5: 4, 6: 4, 7: 4, 8: 3, 9: 2, 10: 1}
    _COLORS = ("red", "black")

    def __init__(self, rng: random.Random) -> None:
        self._rng = rng
        self._draw_pile: List[Card] = []
        self._discard_pile: List[Card] = []
        self._build_and_shuffle()

    def _build_and_shuffle(self) -> None:
        self._draw_pile = [
            Card(v, self._COLORS[i % 2])
            for v, count in self._COUNTS.items()
            for i in range(count)
        ]
        self._rng.shuffle(self._draw_pile)

    def draw(self) -> Card:
        if not self._draw_pile:
            if not self._discard_pile:
                raise RuntimeError("Both draw pile and discard pile are empty.")
            self._reshuffle_discard()
        return self._draw_pile.pop()

    def discard(self, card: Card) -> None:
        self._discard_pile.append(card)

    def _reshuffle_discard(self) -> None:
        self._draw_pile = self._discard_pile[:]
        self._discard_pile = []
        self._rng.shuffle(self._draw_pile)

    def peek(self) -> Optional[Card]:
        """Return the top card without drawing it. Returns None if deck is empty."""
        if not self._draw_pile:
            if not self._discard_pile:
                return None
            self._reshuffle_discard()
        return self._draw_pile[-1]

    @property
    def draw_pile_size(self) -> int:
        return len(self._draw_pile)

    @property
    def discard_pile_size(self) -> int:
        return len(self._discard_pile)

    @property
    def total_size(self) -> int:
        return len(self._draw_pile) + len(self._discard_pile)
