from __future__ import annotations

import random
from dataclasses import dataclass
from enum import Enum
from typing import TYPE_CHECKING, Dict, List, Optional

from .cards import Card, Deck, HAND_SIZE

if TYPE_CHECKING:
    from .abilities import AbilityEngine


class PlayerRole(Enum):
    SETTER = "Setter"   # pos 1 — back row; cannot be served to
    OPP    = "OPP"      # pos 2 — front row Right
    MB     = "MB"       # pos 3 — front row Middle
    OH     = "OH"       # pos 4 — front row Left
    DS     = "DS"       # pos 5 — back row
    LIBERO = "Libero"   # pos 6 — back row; cannot attack


FRONT_ROW_ROLES = frozenset({PlayerRole.OH, PlayerRole.MB, PlayerRole.OPP})
BACK_ROW_ROLES  = frozenset({PlayerRole.SETTER, PlayerRole.DS, PlayerRole.LIBERO})

# Lane index (1=Left/OH, 2=Middle/MB, 3=Right/OPP) → attacking role
LANE_TO_ROLE: Dict[int, PlayerRole] = {
    1: PlayerRole.OH,
    2: PlayerRole.MB,
    3: PlayerRole.OPP,
}


def _build_set_lanes() -> Dict[int, List[int]]:
    """Map each set-card value (1–10) to eligible attack lane indices."""
    m: Dict[int, List[int]] = {}
    for v in (1, 2, 3):   m[v] = [1, 2]      # quick set:    OH + MB
    for v in (4, 5):      m[v] = [3, 2]      # weak side:   OPP + MB
    for v in (6, 7):      m[v] = [1, 2]      # strong side: OH + MB
    for v in (8, 9):      m[v] = [1, 3]      # high outside: OH + OPP
    m[10] = [1, 2, 3]                         # free choice: any two front row
    return m


SET_ELIGIBLE_LANES: Dict[int, List[int]] = _build_set_lanes()


@dataclass
class GridPlayer:
    role: PlayerRole
    position: int  # volleyball position 1–6

    def can_attack(self) -> bool:
        return self.role not in (PlayerRole.LIBERO, PlayerRole.SETTER)

    def can_receive_serve(self) -> bool:
        return self.role != PlayerRole.SETTER

    def is_front_row(self) -> bool:
        return self.role in FRONT_ROW_ROLES

    def is_back_row(self) -> bool:
        return self.role in BACK_ROW_ROLES

    def __repr__(self) -> str:
        return f"GridPlayer({self.role.value}, pos={self.position})"


class Team:
    def __init__(self, name: str, rng: random.Random) -> None:
        self.name = name
        self.deck = Deck(rng)
        self.hand: List[Card] = []
        self.held_card: Optional[Card] = None
        self.ability_engine: Optional[AbilityEngine] = None
        self.players: List[GridPlayer] = [
            GridPlayer(PlayerRole.SETTER, 1),
            GridPlayer(PlayerRole.OPP,    2),
            GridPlayer(PlayerRole.MB,     3),
            GridPlayer(PlayerRole.OH,     4),
            GridPlayer(PlayerRole.DS,     5),
            GridPlayer(PlayerRole.LIBERO, 6),
        ]

    def get_player(self, role: PlayerRole) -> GridPlayer:
        for p in self.players:
            if p.role == role:
                return p
        raise ValueError(f"No player with role {role}")

    def eligible_receivers(self) -> List[GridPlayer]:
        """Back-row players who can receive a serve (excludes Setter)."""
        return [p for p in self.players if p.can_receive_serve() and p.is_back_row()]

    def draw_starting_hand(self) -> None:
        self.hand = [self.deck.draw() for _ in range(HAND_SIZE)]

    def refill_hand(self) -> None:
        """Draw cards until hand is back to HAND_SIZE (plus any ability modifier)."""
        # Return any held card first
        if self.held_card is not None:
            self.hand.append(self.held_card)
            self.held_card = None
        max_size = HAND_SIZE
        if self.ability_engine:
            max_size += self.ability_engine.hand_size_modifier()
        while len(self.hand) < max_size:
            self.hand.append(self.deck.draw())

    def play_card(self, card: Card) -> None:
        """Remove card from hand and send to discard."""
        self.hand.remove(card)
        self.deck.discard(card)

    def play_cards(self, cards: List[Card]) -> None:
        for c in cards:
            self.play_card(c)

    def commit_card(self, card: Card) -> None:
        """Remove card from hand without discarding (HIT / BLOCK placement)."""
        self.hand.remove(card)

    def commit_cards(self, cards: List[Card]) -> None:
        for c in cards:
            self.commit_card(c)

    def discard_card(self, card: Card) -> None:
        """Send a previously committed card to the discard pile."""
        self.deck.discard(card)

    def discard_many(self, cards: List[Card]) -> None:
        for c in cards:
            self.discard_card(c)

    def blind_draw(self) -> Card:
        """Draw the top card directly (used when hand is empty). Immediately discards it."""
        card = self.deck.draw()
        self.deck.discard(card)
        return card

    def __repr__(self) -> str:
        return f"Team({self.name!r}, hand={self.hand})"
