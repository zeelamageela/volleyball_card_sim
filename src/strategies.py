from __future__ import annotations

import random
from abc import ABC, abstractmethod
from typing import Dict, List, Tuple, TYPE_CHECKING

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
    def choose_set_card(self, hand: List[Card]) -> Card:
        """Return card to play as the set. Value determines eligible hitter lanes."""

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
    def choose_hit_cards(self, hand: List[Card], template: "SetTemplate") -> List[Tuple[int, Card, str]]:
        # Phase 3: Deterministic front + back-row attacks with odd/even logic
        evens = sorted([c for c in hand if c.value % 2 == 0], key=lambda c: -c.value)
        odds  = sorted([c for c in hand if c.value % 2 == 1], key=lambda c: -c.value)

        result: List[Tuple[int, Card, str]] = []
        
        # First, fill front-row lanes using odd/even logic
        for lane in sorted(template.front_lanes):
            if len(result) >= template.max_attackers:
                break
            
            if lane == 2:
                pool, fallback = evens, odds
            else:
                pool, fallback = odds, evens
            
            if pool:
                result.append((lane, pool.pop(0), "front"))
            elif fallback:
                result.append((lane, fallback.pop(0), "front"))
        
        # Then, fill back-row lanes with remaining high cards (if space available)
        for lane in sorted(template.back_lanes):
            if len(result) >= template.max_attackers:
                break
            
            # Use same logic: lane 2 prefers evens, sides prefer odds
            if lane == 2:
                pool, fallback = evens, odds
            else:
                pool, fallback = odds, evens
            
            if pool:
                result.append((lane, pool.pop(0), "back"))
            elif fallback:
                result.append((lane, fallback.pop(0), "back"))
        
        # If still not at max, fill any remaining slots with leftover cards
        if len(result) < template.max_attackers:
            used_ids = {id(t[1]) for t in result}
            remaining = sorted([c for c in hand if id(c) not in used_ids], key=lambda c: -c.value)
            
            # Try to add more back-row attacks if available
            assigned_lanes = {(t[0], t[2]) for t in result}  # (lane, position) pairs
            for lane in template.back_lanes:
                if len(result) >= template.max_attackers or not remaining:
                    break
                if (lane, "back") not in assigned_lanes:
                    result.append((lane, remaining.pop(0), "back"))

        return result

    # ── Block ──────────────────────────────────────────────────────────────
    def choose_block_cards(self, hand: List[Card], attack_lanes: List[int]) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}
        sorted_hand = sorted(hand, key=lambda c: -c.value)
        
        # Determine priority lane for double-block
        if len(attack_lanes) == 1:
            # Only 1 lane attacked: always double-block it
            primary = attack_lanes[0]
        else:
            # 2 lanes attacked: use top 3 block cards' odd/even majority
            top_three = sorted_hand[:min(3, len(sorted_hand))]
            odd_count = sum(1 for c in top_three if c.value % 2 == 1)
            even_count = len(top_three) - odd_count
            
            if odd_count > even_count:
                # Majority odd: double-block lane 2 if attacked, else lowest lane
                primary = 2 if 2 in attack_lanes else min(attack_lanes)
            else:
                # Majority even (or tie): double-block lowest attacked lane
                primary = min(attack_lanes)
        
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
        # Choose lane with best differential (attack value - block value)
        def score_lane(lane: int) -> int:
            attack_val = attack_cards[lane].value
            block_val = block_layout.get(lane, 0)
            return attack_val - block_val
        
        return max(attack_cards, key=score_lane)

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
    def choose_set_card(self, hand: List[Card]) -> Card:
        # Prefer setting with mid-high cards (6-9) for good lane options
        # Avoid wasting 10s on sets; save for attacks/blocks
        # Avoid low cards (1-3) that limit attack options
        mid_high = [c for c in hand if 6 <= c.value <= 9]
        if mid_high:
            return max(mid_high, key=lambda c: c.value)
        # If no mid-high, prefer any card 4-9
        decent = [c for c in hand if 4 <= c.value <= 9]
        if decent:
            return max(decent, key=lambda c: c.value)
        # Otherwise play lowest to save better cards
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
        
        # Add back-row lanes (create multi-lane pressure)
        for lane in sorted(template.back_lanes):
            attack_plan.append((lane, "back"))
        
        # Filter out already-used slots from tactical matching
        used_slots = {(t[0], t[2]) for t in result}  # (lane, position) pairs
        attack_plan = [(l, p) for l, p in attack_plan if (l, p) not in used_slots]
        
        # Execute plan up to max_attackers
        for lane, position in attack_plan:
            if len(result) >= template.max_attackers or not available:
                break
            
            # TACTICAL: Target opponent's setter with odd cards on lanes 1/2
            if lane in (1, 2):
                # Prefer odd cards to force setter dig
                odd_cards = [c for c in available if c.value % 2 == 1]
                if odd_cards:
                    card = odd_cards[0]
                else:
                    card = available[0]
            else:  # lane 3
                # Any strong card works (odd→DS, even→Libero)
                card = available[0]
            
            result.append((lane, card, position))
            available.remove(card)
        
        return result

    # ── Block ──────────────────────────────────────────────────────────────
    def choose_block_cards(
        self, hand: List[Card], attack_lanes: List[int]
    ) -> Dict[int, List[Card]]:
        if not hand or not attack_lanes:
            return {}
        
        sorted_hand = sorted(hand, key=lambda c: -c.value)
        
        # TACTICAL: Protect our setter from odd attacks on lanes 1/2
        # Check our top 3 block cards to anticipate opponent's likely attack targets
        top_three = sorted_hand[:min(3, len(sorted_hand))]
        odd_count = sum(1 for c in top_three if c.value % 2 == 1)
        
        # Identify setter-vulnerable lanes (1 and 2 for odd attacks)
        setter_lanes = [l for l in attack_lanes if l in (1, 2)]
        
        result: Dict[int, List[Card]] = {}
        
        if len(attack_lanes) == 1:
            # Single lane attacked: double-block it
            result[attack_lanes[0]] = sorted_hand[:min(2, len(sorted_hand))]
        elif len(attack_lanes) == 2:
            # Prioritize setter-vulnerable lanes if we hold odd cards
            if setter_lanes and odd_count > 0:
                # Protect setter: double-block setter-vulnerable lane
                primary = setter_lanes[0]
            else:
                # Standard: double-block first lane
                primary = attack_lanes[0]
            
            result[primary] = sorted_hand[:min(2, len(sorted_hand))]
            other = [l for l in attack_lanes if l != primary][0]
            if len(sorted_hand) > 2:
                result[other] = sorted_hand[2:min(4, len(sorted_hand))]
        else:
            # Three lanes: prioritize setter protection if we hold odd cards
            if setter_lanes and odd_count >= 2:
                # Strong odd presence: protect setter lanes
                primary = min(setter_lanes)  # lanes 1 or 2
                result[primary] = sorted_hand[:min(2, len(sorted_hand))]
                remaining = [l for l in attack_lanes if l != primary]
                if len(sorted_hand) > 2:
                    result[remaining[0]] = [sorted_hand[2]]
                if len(sorted_hand) > 3:
                    result[remaining[1]] = [sorted_hand[3]]
            else:
                # Spread blocks evenly
                result[attack_lanes[0]] = sorted_hand[:min(2, len(sorted_hand))]
                if len(sorted_hand) > 2:
                    result[attack_lanes[1]] = [sorted_hand[2]]
                if len(sorted_hand) > 3:
                    result[attack_lanes[2]] = [sorted_hand[3]]
        
        return result

    # ── Attack lane choice ─────────────────────────────────────────────────
    def choose_attack_lane(
        self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]
    ) -> int:
        # Choose lane with best (attack - block) differential
        best_lane = max(
            attack_cards.keys(),
            key=lambda l: attack_cards[l].value - block_layout.get(l, 0)
        )
        return best_lane

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
