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


@dataclass
class SetTemplate:
    """
    Defines which lanes are available for attack after a set.
    
    front_lanes: Lane indices available for front-row attackers
    back_lanes: Lane indices available for back-row attackers (DS/Setter, NOT Libero)
    max_attackers: Maximum number of cards to place (1, 2, or 3)
    """
    front_lanes: List[int]
    back_lanes: List[int]
    max_attackers: int


@dataclass
class SetTemplate:
    """
    Defines which lanes are available for attack after a set.
    
    front_lanes: Lane indices available for front-row attackers
    back_lanes: Lane indices available for back-row attackers (DS/Setter, NOT Libero)
    max_attackers: Maximum number of cards to place (1, 2, or 3)
    """
    front_lanes: List[int]
    back_lanes: List[int]
    max_attackers: int


def _build_setter_templates() -> Dict[int, SetTemplate]:
    """
    Normal play: Setter sets the ball.
    
    Set Template (Phase 4 updated):
    1-7:  Standard Sets - Various lane configurations (3 attackers max)
    8-10: High Sets - More hang time, complex attacks (4 attackers max)
    """
    templates: Dict[int, SetTemplate] = {}
    
    # Quick sets (1-3): No back-row attacks (too fast)
    for v in (1, 2, 3):
        templates[v] = SetTemplate(front_lanes=[1, 2, 3], back_lanes=[], max_attackers=3)
    
    # Strong side (4-5): OH + MB front, any back
    for v in (4, 5):
        templates[v] = SetTemplate(front_lanes=[1, 2], back_lanes=[1, 2, 3], max_attackers=3)
    
    # Weak side (6-7): MB + OPP front, any back
    for v in (6, 7):
        templates[v] = SetTemplate(front_lanes=[2, 3], back_lanes=[1, 2, 3], max_attackers=3)
    
    # High outside (8-9): OH + OPP front, any back (4 attackers!)
    for v in (8, 9):
        templates[v] = SetTemplate(front_lanes=[1, 3], back_lanes=[1, 2, 3], max_attackers=4)
    
    # Free choice (10): All lanes available (4 attackers!)
    templates[10] = SetTemplate(front_lanes=[1, 2, 3], back_lanes=[1, 2, 3], max_attackers=4)
    
    return templates


def _build_broken_play_templates() -> Dict[int, SetTemplate]:
    """
    Broken play: Non-setter (OPP/OH) sets the ball.
    
    Set Template:
    1-3:  Strong Side - Front lanes 1-2, Back lane 2 (choose 2)
    4-7:  Single Lane - Front lane 1 OR 3, Back lane 2 (choose 1)
    8-10: Weak Side - Front lanes 2-3, Back lane 2 (choose 2)
    """
    templates: Dict[int, SetTemplate] = {}
    
    # Strong side (1-3): Limited options
    for v in (1, 2, 3):
        templates[v] = SetTemplate(front_lanes=[1, 2], back_lanes=[2], max_attackers=2)
    
    # Single lane (4-7): Emergency set
    for v in (4, 5, 6, 7):
        templates[v] = SetTemplate(front_lanes=[1, 3], back_lanes=[2], max_attackers=1)
    
    # Weak side (8-10): Limited options
    for v in (8, 9, 10):
        templates[v] = SetTemplate(front_lanes=[2, 3], back_lanes=[2], max_attackers=2)
    
    return templates


# Build both template sets
SETTER_TEMPLATES: Dict[int, SetTemplate] = _build_setter_templates()
BROKEN_PLAY_TEMPLATES: Dict[int, SetTemplate] = _build_broken_play_templates()

# Legacy support: Keep old SET_ELIGIBLE_LANES for backward compatibility during transition
def _build_set_lanes() -> Dict[int, List[int]]:
    """DEPRECATED: Legacy function for backward compatibility."""
    m: Dict[int, List[int]] = {}
    for v in (1, 2, 3):   m[v] = [1, 2]
    for v in (4, 5):      m[v] = [3, 2]
    for v in (6, 7):      m[v] = [1, 2]
    for v in (8, 9):      m[v] = [1, 3]
    m[10] = [1, 2, 3]
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
