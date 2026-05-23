from __future__ import annotations

import random
from abc import ABC, abstractmethod
from typing import Dict, List, Tuple

from .cards import Card
from .players import GridPlayer


class BaseStrategy(ABC):
    """
    Abstract base: one method per decision point in a rally.
    Implement this to define an AI or human-input strategy.
    """

    @abstractmethod
    def choose_serve(
        self, hand: List[Card], eligible_receivers: List[GridPlayer]
    ) -> Tuple[Card, GridPlayer]:
        """Return (card to serve with, receiver to target)."""

    @abstractmethod
    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        """Return card to play for receive. Success if card.value >= serve_value."""

    @abstractmethod
    def choose_set_card(self, hand: List[Card]) -> Card:
        """Return card to play as the set. Value determines eligible hitter lanes."""

    @abstractmethod
    def choose_hit_cards(
        self, hand: List[Card], eligible_lanes: List[int]
    ) -> Dict[int, Card]:
        """
        Return {lane: card} for attack placement (face-down).
        - eligible_lanes: 2 lanes for set 1–9; all 3 lanes for set 10 (pick any 2).
        - May attack 1 or 2 lanes.
        - All returned cards must be distinct and present in hand.
        """

    @abstractmethod
    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int]
    ) -> Dict[int, List[Card]]:
        """
        Return {lane: [cards]} for block placement across lanes 1–3.
        - Blocker knows which lanes were attacked (attack_lanes).
        - Block value for a lane = sum of cards on that lane.
        - May double a lane (2 cards → summed value).
        - May leave lanes unblocked.
        """

    @abstractmethod
    def choose_attack_lane(
        self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]
    ) -> int:
        """
        Attacker sees the block layout and chooses which lane to commit to.
        Must return a lane key present in attack_cards.
        block_layout: {lane: total_block_value}, unblocked lanes absent (value=0).
        """

    @abstractmethod
    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        """
        Return 'tip' or 'hit'.
        Tip is only valid if attack_value <= 4; otherwise must return 'hit'.
        """

    @abstractmethod
    def choose_dig_card(
        self, hand: List[Card], target_value: int, dig_type: str
    ) -> Card:
        """
        Return card to attempt the dig with.
        dig_type='normal': success if card.value >= target_value (kill dig).
        dig_type='tip':    success if card.value <= target_value (tip/deflect dig).
        """

    @abstractmethod
    def choose_chase_card(
        self, hand: List[Card], running_total: int, target_value: int
    ) -> Card:
        """
        Return a card to add to running_total during a chase attempt.
        Called after a failed normal dig or failed reception.
        """

    @abstractmethod
    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        """
        Return 1 (OH/left) or 3 (OPP/right) for an armed attack after a
        successful first-chase.  No set card is played; only this lane is open.
        """


class RandomStrategy(BaseStrategy):
    """All decisions made uniformly at random within legal constraints."""

    def __init__(self, rng: random.Random) -> None:
        self._rng = rng

    def choose_serve(
        self, hand: List[Card], eligible_receivers: List[GridPlayer]
    ) -> Tuple[Card, GridPlayer]:
        return self._rng.choice(hand), self._rng.choice(eligible_receivers)

    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        return self._rng.choice(hand)

    def choose_set_card(self, hand: List[Card]) -> Card:
        return self._rng.choice(hand)

    def choose_hit_cards(
        self, hand: List[Card], eligible_lanes: List[int]
    ) -> Dict[int, Card]:
        # For set-10, eligible_lanes has 3 entries; restrict to 2
        if len(eligible_lanes) > 2:
            eligible_lanes = self._rng.sample(eligible_lanes, 2)

        # Randomly attack 1 or 2 of the eligible lanes
        n_lanes = min(self._rng.randint(1, len(eligible_lanes)), len(hand))
        chosen_lanes = self._rng.sample(eligible_lanes, n_lanes)

        available = hand[:]
        result: Dict[int, Card] = {}
        for lane in chosen_lanes:
            card = self._rng.choice(available)
            result[lane] = card
            available.remove(card)
        return result

    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int]
    ) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}
        shuffled = list(hand)
        self._rng.shuffle(shuffled)
        # Randomly pick which attack lane to double-block
        primary = self._rng.choice(attack_lanes)
        result: Dict[int, List[Card]] = {primary: shuffled[:min(2, len(shuffled))]}
        # Single-block every other attack lane (1 card each)
        others = [l for l in attack_lanes if l != primary]
        for i, lane in enumerate(others):
            card_idx = 2 + i
            if card_idx < len(shuffled):
                result[lane] = [shuffled[card_idx]]
        return result

    def choose_attack_lane(
        self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]
    ) -> int:
        # Prefer the lane with the least block coverage — avoid the double-block
        return min(attack_cards, key=lambda l: block_layout.get(l, 0))

    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        if attack_value <= 4:
            return self._rng.choice(["tip", "hit"])
        return "hit"

    def choose_dig_card(
        self, hand: List[Card], target_value: int, dig_type: str
    ) -> Card:
        # Tip dig needs card <= target → play lowest
        # Normal/deflect dig needs card >= target → play highest
        if dig_type == "tip":
            return min(hand, key=lambda c: c.value)
        return max(hand, key=lambda c: c.value)

    def choose_chase_card(
        self, hand: List[Card], running_total: int, target_value: int
    ) -> Card:
        return self._rng.choice(hand)

    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        return self._rng.choice([1, 3])


class DummyStrategy(BaseStrategy):
    """
    Fixed deterministic baseline opponent.

    Attack targeting:
      even card value → lane 2 (middle / MB)
      odd  card value → side lane (1 or 3, never 2)

    Serve targeting:
      even card value → first eligible receiver
      odd  card value → last  eligible receiver

    Blocking: always double-commits to lane 2 (middle priority).
    Tipping:  always tips when the game engine offers the choice.
    Receiving/digging: highest card for normal digs; lowest for tip digs.
    """

    # ── Serve ──────────────────────────────────────────────────────────────
    def choose_serve(self, hand: List[Card], eligible_receivers: List[GridPlayer]) -> Tuple[Card, GridPlayer]:
        card = max(hand, key=lambda c: c.value)
        receiver = (eligible_receivers[0] if card.value % 2 == 0
                    else eligible_receivers[-1])
        return card, receiver

    # ── Receive ────────────────────────────────────────────────────────────
    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        return max(hand, key=lambda c: c.value)

    # ── Set ────────────────────────────────────────────────────────────────
    def choose_set_card(self, hand: List[Card]) -> Card:
        return max(hand, key=lambda c: c.value)

    # ── Hit ────────────────────────────────────────────────────────────────
    def choose_hit_cards(self, hand: List[Card], eligible_lanes: List[int]) -> Dict[int, Card]:
        # Restrict to 2 lanes max (set=10 gives 3 eligible)
        if len(eligible_lanes) > 2:
            eligible_lanes = eligible_lanes[:2]

        evens = sorted([c for c in hand if c.value % 2 == 0], key=lambda c: -c.value)
        odds  = sorted([c for c in hand if c.value % 2 == 1], key=lambda c: -c.value)

        assigned: Dict[int, Card] = {}
        for lane in sorted(eligible_lanes):
            if lane == 2:
                pool, fallback = evens, odds
            else:
                pool, fallback = odds, evens
            if pool:
                assigned[lane] = pool.pop(0)
            elif fallback:
                assigned[lane] = fallback.pop(0)

        # Fill any remaining lanes if pools ran dry
        used_ids = {id(c) for c in assigned.values()}
        remaining = sorted([c for c in hand if id(c) not in used_ids], key=lambda c: -c.value)
        for lane in sorted(eligible_lanes):
            if lane not in assigned and remaining:
                assigned[lane] = remaining.pop(0)

        return assigned

    # ── Block ──────────────────────────────────────────────────────────────
    def choose_block_cards(self, hand: List[Card], attack_lanes: List[int]) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}
        sorted_hand = sorted(hand, key=lambda c: -c.value)
        # Priority lane: middle (2) if being attacked, else the lower side lane
        primary = 2 if 2 in attack_lanes else min(attack_lanes)
        # Double-block the primary lane, single-block any remaining attack lanes
        result: Dict[int, List[Card]] = {primary: sorted_hand[:min(2, len(sorted_hand))]}
        others = [l for l in attack_lanes if l != primary]
        for i, lane in enumerate(others):
            card_idx = 2 + i
            if card_idx < len(sorted_hand):
                result[lane] = [sorted_hand[card_idx]]
        return result

    # ── Attack lane choice ─────────────────────────────────────────────────
    def choose_attack_lane(self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]) -> int:
        # Prefer the lane with the least block coverage — avoid the double-block
        return min(attack_cards, key=lambda l: block_layout.get(l, 0))

    # ── Tip or hit ─────────────────────────────────────────────────────────
    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        return "tip"

    # ── Dig ────────────────────────────────────────────────────────────────
    def choose_dig_card(self, hand: List[Card], target_value: int, dig_type: str) -> Card:
        # Tip dig: need card <= target → play lowest
        if dig_type == "tip":
            return min(hand, key=lambda c: c.value)
        # Normal/deflect dig: need card >= target → play highest
        return max(hand, key=lambda c: c.value)

    # ── Chase ──────────────────────────────────────────────────────────────
    def choose_chase_card(self, hand: List[Card], running_total: int, target_value: int) -> Card:
        return max(hand, key=lambda c: c.value)

    # ── Armed attack ───────────────────────────────────────────────────────
    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        return 1  # always OH side
