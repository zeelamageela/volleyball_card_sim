from __future__ import annotations

import csv
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional

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
    Standard 28-card deck or modified "dummy" 28-card deck.

    Standard counts (28 cards):
      1×1  2×2  3×3  4×4  4×5  4×6  4×7  3×8  2×9  1×10
      Distribution: 46% low (1-4), 29% mid (5-6), 36% high (7-10)

    Dummy counts (28 cards) - Gradual shift to extremes:
      1×2  2×2  3×3  4×4  5×3  6×3  7×4  8×3  9×3  10×2
      Distribution: 39% low (1-4), 21% mid (5-6), 39% high (7-10)
      (Reduces mediocre middle, adds finesse at bottom and power at top)
    """

    _STANDARD_COUNTS: Dict[int, int] = {1: 1, 2: 2, 3: 3, 4: 4, 5: 4, 6: 4, 7: 4, 8: 3, 9: 2, 10: 1}
    _DUMMY_COUNTS: Dict[int, int] = {1: 2, 2: 2, 3: 2, 4: 3, 5: 2, 6: 2, 7: 4, 8: 5, 9: 4, 10: 2}
    _COLORS = ("red", "black")
    _DECK_TYPES_CSV = Path("data/deck_types.csv")
    _COUNTS_CACHE: Optional[Dict[str, Dict[int, int]]] = None

    def __init__(self, rng: random.Random, deck_type: str = "standard") -> None:
        self._rng = rng
        self._draw_pile: List[Card] = []
        self._discard_pile: List[Card] = []
        self._deck_type = deck_type.strip().lower() if deck_type else "standard"
        self._build_and_shuffle()

    @classmethod
    def _load_counts(cls) -> Dict[str, Dict[int, int]]:
        if cls._COUNTS_CACHE is not None:
            return cls._COUNTS_CACHE

        counts_by_type: Dict[str, Dict[int, int]] = {
            "standard": dict(cls._STANDARD_COUNTS),
            "dummy": dict(cls._DUMMY_COUNTS),
        }

        csv_path = cls._DECK_TYPES_CSV
        if csv_path.exists():
            try:
                with open(csv_path, newline="", encoding="utf-8-sig") as f:
                    for row in csv.DictReader(f):
                        deck_type = (row.get("deck_type") or "").strip().lower()
                        if not deck_type:
                            continue
                        try:
                            value = int((row.get("card_value") or "").strip())
                            count = int((row.get("count") or "").strip())
                        except ValueError:
                            continue
                        if value < 1 or value > 10:
                            continue
                        if deck_type not in counts_by_type:
                            counts_by_type[deck_type] = {}
                        if count <= 0:
                            counts_by_type[deck_type].pop(value, None)
                        else:
                            counts_by_type[deck_type][value] = count
            except OSError:
                pass

        # Drop invalid empty deck definitions.
        counts_by_type = {k: v for k, v in counts_by_type.items() if v}
        cls._COUNTS_CACHE = counts_by_type
        return counts_by_type

    def _build_and_shuffle(self) -> None:
        counts_by_type = self._load_counts()
        counts = counts_by_type.get(self._deck_type, counts_by_type.get("standard", self._STANDARD_COUNTS))
        self._draw_pile = [
            Card(v, self._COLORS[i % 2])
            for v, count in counts.items()
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
