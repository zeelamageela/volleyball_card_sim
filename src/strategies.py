from __future__ import annotations

import random
from abc import ABC, abstractmethod
from typing import Dict, List, Optional, Tuple, TYPE_CHECKING

from .cards import Card
from .players import GridPlayer

if TYPE_CHECKING:
    from .players import SetTemplate


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
    def choose_set_card(self, hand: List[Card], broken_play: bool = False) -> Card:
        """Return card to play as the set. Value determines eligible hitter lanes.
        broken_play=True means a non-setter is setting (restricted templates).
        """

    @abstractmethod
    def choose_hit_cards(
        self, hand: List[Card], template: "SetTemplate"
    ) -> List[Tuple[int, Card, str]]:
        """
        Return list of (lane, card, position) tuples for attack placement.
        
        - template.front_lanes: Available lanes for front-row attackers
        - template.back_lanes: Available lanes for back-row attackers (DS/Setter, NOT Libero)
        - template.max_attackers: Maximum number of (lane, card, position) tuples to return
        - position: Must be "front" or "back"
        
        Rules:
        - Can place multiple cards on same lane (one front, one back)
        - Cannot exceed template.max_attackers total cards
        - All cards must be distinct and from hand
        - For set=10, can place up to 3 cards but max 2 per lane
        """

    @abstractmethod
    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int], wild_threshold: int = 0,
        wide_spread_threshold: int = 0
    ) -> Dict[int, List[Card]]:
        """
        Return {lane: [cards]} for block placement across lanes 1–3.
        - Blocker knows which lanes were attacked (attack_lanes).
        - Block value for a lane = sum of cards on that lane.
        - May double a lane (2 cards → summed value).
        - May leave lanes unblocked.
        - wild_threshold > 0: cards with value <= wild_threshold may go to any lane.
        - wide_spread_threshold > 0: if |card1-card2| >= threshold, block gets +threshold bonus.
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

    @abstractmethod
    def choose_exchange_card(
        self, hand: List[Card], deck_top: Card
    ) -> Optional[Card]:
        """
        Called before hit placement when the team has exchange_card ability.
        Return a card from hand to discard in exchange for deck_top, or None
        to decline the exchange.
        """

    @abstractmethod
    def choose_cover_attempt(self, hand: List[Card], threshold: int) -> Optional[Card]:
        """
        Called when a kill or roll-shot dig lands in the setter's zone and the
        team has an eligible Libero or DS who could intercept.

        Return a card to use for the cover attempt — that same card also serves
        as the dig card.  Return None to skip coverage (setter digs, broken play).

        Cover succeeds if the returned card's value >= threshold.
        Only called when cover_draws_from_deck() returns False.
        """

    def cover_draws_from_deck(self) -> bool:
        """
        Return True if this strategy flips the top card of the deck for cover
        instead of selecting from the hand.  Use for strategies that do not
        maintain or optimize a hand (e.g. Dummy).  Default: False.
        """
        return False


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

    def choose_set_card(self, hand: List[Card], broken_play: bool = False) -> Card:
        return self._rng.choice(hand)

    def choose_hit_cards(
        self, hand: List[Card], template: "SetTemplate"
    ) -> List[Tuple[int, Card, str]]:
        # Phase 3: Random back-row attack usage
        available = hand[:]
        result: List[Tuple[int, Card, str]] = []
        max_cards = min(template.max_attackers, len(available))
        
        # Randomly decide how many cards to place (1 to max_cards)
        num_to_place = self._rng.randint(1, max_cards)
        
        # Build pool of available (lane, position) slots
        slots: List[Tuple[int, str]] = []
        for lane in template.front_lanes:
            slots.append((lane, "front"))
        for lane in template.back_lanes:
            slots.append((lane, "back"))
        
        # Randomly pick slots and assign cards
        if slots and num_to_place > 0:
            chosen_slots = self._rng.sample(slots, min(num_to_place, len(slots)))
            for lane, position in chosen_slots:
                if not available:
                    break
                card = self._rng.choice(available)
                result.append((lane, card, position))
                available.remove(card)
        
        return result

    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int], wild_threshold: int = 0,
        wide_spread_threshold: int = 0
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

    def choose_exchange_card(
        self, hand: List[Card], deck_top: Card
    ) -> Optional[Card]:
        return None  # Random strategy never exchanges

    def choose_cover_attempt(self, hand: List[Card], threshold: int) -> Optional[Card]:
        # 50/50 whether to attempt; if yes, pick a random card
        if not hand or not self._rng.choice([True, False]):
            return None
        return self._rng.choice(hand)


class DummyStrategy(BaseStrategy):
    """
    Truly blind opponent — no hand optimization whatsoever.

    Every card decision uses hand[0] (the top card as drawn, no sorting or
    selection).  Non-card decisions (lane, tip/hit) use the top card's parity
    as a tie-breaker so behavior remains deterministic and testable.

    Cover attempts draw the top card of the deck directly
    (cover_draws_from_deck() == True), bypassing the hand entirely.
    """

    # ── Serve ──────────────────────────────────────────────────────────────
    def choose_serve(self, hand: List[Card], eligible_receivers: List[GridPlayer]) -> Tuple[Card, GridPlayer]:
        card = hand[0]
        receiver = (eligible_receivers[0] if card.value % 2 == 0
                    else eligible_receivers[-1])
        return card, receiver

    # ── Receive ────────────────────────────────────────────────────────────
    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        return hand[0]

    # ── Set ────────────────────────────────────────────────────────────────
    def choose_set_card(self, hand: List[Card], broken_play: bool = False) -> Card:
        return hand[0]

    # ── Hit ────────────────────────────────────────────────────────────────
    def choose_hit_cards(self, hand: List[Card], template: "SetTemplate") -> List[Tuple[int, Card, str]]:
        # Blind: use the top card on the first available lane
        if template.front_lanes and hand:
            return [(template.front_lanes[0], hand[0], "front")]
        if template.back_lanes and hand:
            return [(template.back_lanes[0], hand[0], "back")]
        return []

    # ── Block ──────────────────────────────────────────────────────────────
    def choose_block_cards(self, hand: List[Card], attack_lanes: List[int], wild_threshold: int = 0,
                           wide_spread_threshold: int = 0) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}
        # Blind: put the top card on the first attacked lane
        return {attack_lanes[0]: [hand[0]]}

    # ── Attack lane choice ─────────────────────────────────────────────────
    def choose_attack_lane(self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]) -> int:
        # Blind: pick the first lane available
        return list(attack_cards.keys())[0]

    # ── Tip or hit ─────────────────────────────────────────────────────────
    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        return "tip"

    # ── Dig ────────────────────────────────────────────────────────────────
    def choose_dig_card(self, hand: List[Card], target_value: int, dig_type: str) -> Card:
        return hand[0]

    # ── Chase ──────────────────────────────────────────────────────────────
    def choose_chase_card(self, hand: List[Card], running_total: int, target_value: int) -> Card:
        return hand[0]

    # ── Armed attack ───────────────────────────────────────────────────────
    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        return 1

    # ── Exchange ───────────────────────────────────────────────────────────
    def choose_exchange_card(
        self, hand: List[Card], deck_top: Card
    ) -> Optional[Card]:
        return None  # Dummy never exchanges

    # ── Cover ──────────────────────────────────────────────────────────────
    def cover_draws_from_deck(self) -> bool:
        return True  # Blind deck flip — no hand selection

    def choose_cover_attempt(self, hand: List[Card], threshold: int) -> Optional[Card]:
        return None  # Not called when cover_draws_from_deck() is True


class SmartStrategy(BaseStrategy):
    """
    Optimized strategy that exploits opponent patterns and makes intelligent decisions.
    
    Key tactics:
    1. Plays high cards on important plays (blocking, digging)
    2. Saves strong cards for critical moments
    3. Exploits DummyStrategy patterns (always doubles lane 2, predictable attacks)
    4. Makes optimal lane choices (attack weak blocks, avoid strong ones)
    """

    def __init__(self, rng: random.Random) -> None:
        self._rng = rng

    # ── Serve ──────────────────────────────────────────────────────────────
    def choose_serve(
        self, hand: List[Card], eligible_receivers: List[GridPlayer]
    ) -> Tuple[Card, GridPlayer]:
        # Serve with highest card to maximize ace potential
        card = max(hand, key=lambda c: c.value)
        # Target a random receiver (no pattern to exploit here)
        receiver = self._rng.choice(eligible_receivers)
        return card, receiver

    # ── Receive ────────────────────────────────────────────────────────────
    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        # Play the minimum card that can succeed (save high cards)
        # Success if card.value >= serve_value
        candidates = [c for c in hand if c.value >= serve_value]
        if candidates:
            return min(candidates, key=lambda c: c.value)
        # If no card can succeed, play lowest to minimize loss
        return min(hand, key=lambda c: c.value)

    # ── Set ────────────────────────────────────────────────────────────────
    def choose_set_card(self, hand: List[Card], broken_play: bool = False) -> Card:
        if broken_play:
            # Broken play templates:
            #   1-3  → max_attackers=2  (decent)
            #   4-7  → max_attackers=1  (worst: single attacker only)
            #   8-10 → max_attackers=2  (decent)
            # Strongly avoid mid-range cards; prefer high (8-10) then low (1-3).
            high = [c for c in hand if c.value >= 8]
            if high:
                return max(high, key=lambda c: c.value)
            low = [c for c in hand if c.value <= 3]
            if low:
                return max(low, key=lambda c: c.value)
            # Stuck with 4-7 — play lowest to minimise disruption
            return min(hand, key=lambda c: c.value)
        # Normal play: prefer mid-high cards (6-9) for good lane options
        mid_high = [c for c in hand if 6 <= c.value <= 9]
        if mid_high:
            return max(mid_high, key=lambda c: c.value)
        decent = [c for c in hand if 4 <= c.value <= 9]
        if decent:
            return max(decent, key=lambda c: c.value)
        return min(hand, key=lambda c: c.value)

    # ── Hit ────────────────────────────────────────────────────────────────
    def choose_hit_cards(
        self, hand: List[Card], template: "SetTemplate"
    ) -> List[Tuple[int, Card, str]]:
        # Phase 4: Tactical matching + multi-lane pressure + setter targeting
        result: List[Tuple[int, Card, str]] = []
        available = sorted(hand, key=lambda c: -c.value)
        
        # Phase 4 TACTIC: Check for duplicate card values to intentionally match
        # Matching cards (front+back same value on same lane) cancel before blocks
        # This wastes defender's block cards on that lane - tactical sacrifice
        # CONSERVATIVE: Only use when we have spare cards and high-value duplicates
        value_counts = {}
        for card in available:
            value_counts[card.value] = value_counts.get(card.value, 0) + 1
        
        # Find best duplicate to match (ONLY high values 8+, and only if we have spare cards)
        best_match_value = None
        if len(available) >= 4 and template.max_attackers >= 3:  # Need at least 4 cards total
            for value in sorted(value_counts.keys(), reverse=True):
                if value_counts[value] >= 2 and value >= 8:  # Only high-value matches
                    best_match_value = value
                    break
        
        # If we have a match opportunity AND both front/back available on same lane
        matching_lane = None
        if best_match_value and template.front_lanes and template.back_lanes:
            # Find a lane that exists in both front and back
            common_lanes = set(template.front_lanes) & set(template.back_lanes)
            if common_lanes:
                # Prefer lane 2 for matching (dummy always double-blocks it)
                if 2 in common_lanes:
                    matching_lane = 2
                else:
                    matching_lane = min(common_lanes)  # Prefer lower lanes
        
        # Execute tactical matching if possible (still have room for other attacks)
        if matching_lane and best_match_value and len(result) + 2 <= template.max_attackers:
            matching_cards = [c for c in available if c.value == best_match_value]
            if len(matching_cards) >= 2 and len(available) - 2 >= 1:  # Ensure we have other cards left
                # Place both cards on same lane to force cancellation
                result.append((matching_lane, matching_cards[0], "front"))
                result.append((matching_lane, matching_cards[1], "back"))
                available.remove(matching_cards[0])
                available.remove(matching_cards[1])
        
        # Now fill remaining slots with normal strategy
        # EXPLOIT: DummyStrategy always double-blocks lane 2
        # So attack lanes 1 and 3 preferentially if available
        preferred_front = [l for l in template.front_lanes if l != 2]
        if not preferred_front:
            preferred_front = list(template.front_lanes)
        
        # Build attack plan: front-row first, then back-row
        # Prioritize multi-lane attacks to spread defense
        attack_plan: List[Tuple[int, str]] = []
        
        # Add front-row lanes (prefer sides over middle)
        for lane in sorted(preferred_front):
            attack_plan.append((lane, "front"))
        # Add middle if available and not excluded
        if 2 in template.front_lanes and 2 not in preferred_front:
            attack_plan.append((2, "front"))
        
        # Add back-row lanes — new distinct lanes first, then shared lanes
        front_lane_set = set(template.front_lanes)
        for lane in sorted(l for l in template.back_lanes if l not in front_lane_set):
            attack_plan.append((lane, "back"))
        for lane in sorted(l for l in template.back_lanes if l in front_lane_set):
            attack_plan.append((lane, "back"))
        
        # Filter out already-used slots from tactical matching
        used_slots = {(t[0], t[2]) for t in result}  # (lane, position) pairs
        attack_plan = [(l, p) for l, p in attack_plan if (l, p) not in used_slots]
        
        # Execute plan up to max_attackers
        for lane, position in attack_plan:
            if len(result) >= template.max_attackers or not available:
                break

            # MATCHING GUARD: never place same value front+back on same lane
            # (attacker-attacker match = defender wins — costs us the rally)
            front_val_on_lane = next(
                (t[1].value for t in result if t[0] == lane and t[2] == "front"),
                None
            )

            # TACTICAL: Target opponent's setter with odd cards on lanes 1/2
            if lane in (1, 2):
                # Prefer odd cards to force setter dig
                candidates = [c for c in available if c.value % 2 == 1]
                if position == "back" and front_val_on_lane is not None:
                    candidates = [c for c in candidates if c.value != front_val_on_lane]
                if not candidates:
                    # Fall back to any available card, still avoiding the match value
                    candidates = [c for c in available
                                  if position != "back" or front_val_on_lane is None
                                  or c.value != front_val_on_lane]
                card = candidates[0] if candidates else available[0]
            else:  # lane 3
                if position == "back" and front_val_on_lane is not None:
                    safe = [c for c in available if c.value != front_val_on_lane]
                    card = safe[0] if safe else available[0]
                else:
                    card = available[0]

            result.append((lane, card, position))
            available.remove(card)
        
        return result

    # ── Block ──────────────────────────────────────────────────────────────
    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int], wild_threshold: int = 0,
        wide_spread_threshold: int = 0
    ) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}

        sorted_hand = sorted(hand, key=lambda c: -c.value)
        result: Dict[int, List[Card]] = {}

        # DIVERSE BLOCK TACTIC: avoid same-value pairs (they cancel).
        # When wide_spread_threshold > 0, also try to find a pair that differs
        # by at least the threshold to earn the wide_spread_bonus.
        available = list(range(len(sorted_hand)))  # indices into sorted_hand

        for lane in attack_lanes:
            if not available:
                break
            first_idx = available[0]
            first_val = sorted_hand[first_idx].value

            if wide_spread_threshold > 0:
                # Look for a pair that earns the spread bonus.
                # Compare effective value: (sum + bonus) vs pure sum.
                spread_idx = next(
                    (i for i in available[1:]
                     if abs(sorted_hand[i].value - first_val) >= wide_spread_threshold),
                    None
                )
                diverse_idx = next(
                    (i for i in available[1:] if sorted_hand[i].value != first_val),
                    None
                )
                if spread_idx is not None and diverse_idx is not None:
                    spread_eff = first_val + sorted_hand[spread_idx].value + wide_spread_threshold
                    plain_eff  = first_val + sorted_hand[diverse_idx].value
                    if spread_eff >= plain_eff:
                        result[lane] = [sorted_hand[first_idx], sorted_hand[spread_idx]]
                        available.remove(first_idx); available.remove(spread_idx)
                    else:
                        result[lane] = [sorted_hand[first_idx], sorted_hand[diverse_idx]]
                        available.remove(first_idx); available.remove(diverse_idx)
                elif spread_idx is not None:
                    result[lane] = [sorted_hand[first_idx], sorted_hand[spread_idx]]
                    available.remove(first_idx); available.remove(spread_idx)
                elif diverse_idx is not None:
                    result[lane] = [sorted_hand[first_idx], sorted_hand[diverse_idx]]
                    available.remove(first_idx); available.remove(diverse_idx)
                else:
                    result[lane] = [sorted_hand[first_idx]]
                    available.remove(first_idx)
            else:
                diverse_idx = next(
                    (i for i in available[1:] if sorted_hand[i].value != first_val),
                    None
                )
                if diverse_idx is not None:
                    result[lane] = [sorted_hand[first_idx], sorted_hand[diverse_idx]]
                    available.remove(first_idx)
                    available.remove(diverse_idx)
                else:
                    # All remaining cards share a value — single block to avoid cancellation
                    result[lane] = [sorted_hand[first_idx]]
                    available.remove(first_idx)

        return result

    # ── Attack lane choice ─────────────────────────────────────────────────
    def choose_attack_lane(
        self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]
    ) -> int:
        def score(lane: int) -> int:
            return attack_cards[lane].value - block_layout.get(lane, 0)

        # Prefer lanes with known values; blind draws have value=0
        known = {l: c for l, c in attack_cards.items() if c.value > 0}
        pool = known if known else attack_cards
        return max(pool, key=score)

    # ── Tip or hit ─────────────────────────────────────────────────────────
    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        # Tip only if it gives an advantage
        if attack_value <= 4:
            # Tip is valid. Tip if block is strong (block > attack + 2)
            if block_value > attack_value + 2:
                return "tip"
        return "hit"

    # ── Dig ────────────────────────────────────────────────────────────────
    def choose_dig_card(
        self, hand: List[Card], target_value: int, dig_type: str
    ) -> Card:
        if dig_type == "tip":
            # Need card <= target. Play highest card that qualifies
            candidates = [c for c in hand if c.value <= target_value]
            if candidates:
                return max(candidates, key=lambda c: c.value)
            # If no card works, play lowest to minimize waste
            return min(hand, key=lambda c: c.value)
        else:
            # Normal dig: need card >= target. Play lowest card that qualifies
            candidates = [c for c in hand if c.value >= target_value]
            if candidates:
                return min(candidates, key=lambda c: c.value)
            # If no card works, play highest to maximize chance
            return max(hand, key=lambda c: c.value)

    # ── Chase ──────────────────────────────────────────────────────────────
    def choose_chase_card(
        self, hand: List[Card], running_total: int, target_value: int
    ) -> Card:
        # Play the minimum card that gets us to target
        needed = target_value - running_total
        candidates = [c for c in hand if c.value >= needed]
        if candidates:
            return min(candidates, key=lambda c: c.value)
        # If we can't reach target, play highest to get closest
        return max(hand, key=lambda c: c.value)

    # ── Armed attack ───────────────────────────────────────────────────────
    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        # Armed attack on sides only. Pick randomly to avoid pattern
        return self._rng.choice([1, 3])

    # ── Exchange card ───────────────────────────────────────────────────
    def choose_exchange_card(
        self, hand: List[Card], deck_top: Card
    ) -> Optional[Card]:
        # Swap out the worst hand card if deck top is significantly better
        worst = min(hand, key=lambda c: c.value)
        if deck_top.value >= worst.value + 3:
            return worst
        return None

    def choose_cover_attempt(self, hand: List[Card], threshold: int) -> Optional[Card]:
        # Smart always tries when a qualifying card exists — use the lowest one
        # (keeps high cards available for attack/block on the next exchange).
        # If no card clears the threshold, skip coverage and let choose_dig_card
        # pick the optimal dig card instead.
        if not hand:
            return None
        qualifying = [c for c in hand if c.value >= threshold]
        if qualifying:
            return min(qualifying, key=lambda c: c.value)
        return None
