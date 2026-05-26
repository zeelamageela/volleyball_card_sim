from __future__ import annotations

import random
from dataclasses import dataclass
from typing import Dict, List, Tuple, Optional

from .cards import Card
from .players import (
    Team, GridPlayer, SET_ELIGIBLE_LANES, LANE_TO_ROLE, PlayerRole,
    SETTER_TEMPLATES, BROKEN_PLAY_TEMPLATES, SetTemplate
)
from .game_state import AttackOutcomeType, ChaseOutcome, ChaseResult, RallyResult, GameResult
from .strategies import BaseStrategy

POINTS_TO_WIN = 15
MAX_EXCHANGES = 200  # safety cap to prevent infinite rallies
_LANE_NAMES = {1: "OH", 2: "MB", 3: "OPP"}   # short lane labels for narrative


@dataclass
class AttackCard:
    """Represents a single attack card with position information."""
    card: Card
    position: str  # "front" or "back"
    blind: bool = False   # True = drawn from deck face-down; value hidden during block/lane decisions

    def is_back_row(self) -> bool:
        return self.position == "back"


def resolve_attack(attack_value: int, block_value: int) -> AttackOutcomeType:
    """Determine attack outcome purely from numeric values.
    
    Attack > Block → KILL
    Block > Attack by 0-2 → DEFLECT (to blocker's side)
    Block > Attack by 3-4 → DEFLECT (to attacker's side)
    Block > Attack by 5+ → STUFFED
    """
    if attack_value > block_value:
        return AttackOutcomeType.KILL
    diff = block_value - attack_value
    if diff <= 4:
        return AttackOutcomeType.DEFLECT
    return AttackOutcomeType.STUFFED


# Baseline threshold for the universal setter-cover mechanic.
# Any team with a Libero or DS covers at this level (~50% of draws).
_SETTER_COVER_BASE = 6


def _cover_threshold(team: "Team") -> int:
    """
    Return the dig-card threshold needed for a Libero/DS to cover the setter's zone,
    or 0 if the team has no eligible player (no Libero or DS on roster).
    Baseline: _SETTER_COVER_BASE (~50% of random draws).
    setter_cover ability cards lower the threshold below the baseline.
    """
    from .players import PlayerRole
    has_eligible = any(
        p.role in (PlayerRole.LIBERO, PlayerRole.DS)
        for p in team.players
    )
    if not has_eligible:
        return 0
    threshold = _SETTER_COVER_BASE
    if team.ability_engine:
        ability_threshold = team.ability_engine.setter_cover_threshold()
        if ability_threshold > 0:
            threshold = min(threshold, ability_threshold)
    return threshold


def get_dig_defender_role(attack_lane: int, attack_card_value: int) -> PlayerRole:
    """
    Determine which defender digs based on attack lane and card parity.
    
    Rules:
    - ALL even-value attacks → Libero (universal back-row defender)
    - Odd attacks split by lane:
        - Lane 1 (OH): Setter (line shot left)
        - Lane 2 (MB): Setter (line shot middle-left)
        - Lane 3 (OPP): DS (line shot right)
    """
    if attack_card_value % 2 == 0:
        return PlayerRole.LIBERO
    
    # Odd attacks: split by lane
    if attack_lane in (1, 2):
        return PlayerRole.SETTER
    else:  # lane 3
        return PlayerRole.DS


class Rally:
    """
    Executes one full rally between a serving and receiving team.

    Card lifecycle for attack / block phases:
      commit_card()  — removes from hand (card is 'in play', not yet discarded)
      refill_hand()  — draws back up to HAND_SIZE
      discard_many() — sends committed cards to the discard pile after resolution
    """

    def __init__(
        self,
        serving_team: Team,
        receiving_team: Team,
        serving_strategy: BaseStrategy,
        receiving_strategy: BaseStrategy,
        rng: random.Random,
        narrative: Optional[List[str]] = None,
    ) -> None:
        self._srv = serving_team
        self._rcv = receiving_team
        self._srv_strat = serving_strategy
        self._rcv_strat = receiving_strategy
        self._rng = rng
        self._narrative = narrative
        self._broken_play = False  # Track if setter dug (broken play for next set)

    def _narrate(self, msg: str) -> None:
        """Append a message to the shared narrative list (no-op if none attached)."""
        if self._narrative is not None:
            self._narrative.append(msg)

    def play(self) -> RallyResult:
        exchange = 0
        next_attack_is_armed = False
        armed_lane: int = 0

        # Replenish hands before rally starts
        self._srv.refill_hand()
        self._rcv.refill_hand()

        # ── SERVE ────────────────────────────────────────────────────────────
        serve_card, _target = self._phase_serve()
        # Effective serve value (Setter serve bonus, if any)
        serve_value = serve_card.value + (
            self._srv.ability_engine.serve_value_bonus()
            if self._srv.ability_engine else 0
        )
        self._narrate(
            f"\n  Serve:   {self._srv.name} card {serve_card.value}"
            + (f" (eff {serve_value})" if serve_value != serve_card.value else "")
            + f"  →  targeting {_target.role.value}"
        )

        # ── RECEIVE ──────────────────────────────────────────────────────────
        receive_card = self._phase_receive(serve_value)
        _rcv_ok = receive_card.value >= serve_value
        self._narrate(
            f"  Receive: {self._rcv.name} card {receive_card.value}"
            f"  vs serve {serve_value}"
            f"  →  {'clean pass' if _rcv_ok else 'FAILED — chase needed'}"
        )

        if receive_card.value < serve_value:
            # Failed reception → chase (applies to receptions as well as digs)
            chase = self._phase_chase(
                self._rcv, self._rcv_strat, receive_card.value, serve_card.value
            )
            if chase.outcome == ChaseOutcome.FAILED:
                return RallyResult(
                    winner_name=self._srv.name,
                    reason=(
                        f"Serve ace (serve={serve_value} > "
                        f"receive={receive_card.value}), chase failed"
                    ),
                    rally_length=0,
                )
            elif chase.outcome == ChaseOutcome.ARMED_ATTACK:
                # rcv chased successfully → they attack with one OH/OPP lane
                attacker, defender = self._rcv, self._srv
                atk_strat, def_strat = self._rcv_strat, self._srv_strat
                next_attack_is_armed = True
                armed_lane = chase.armed_lane
            else:  # FREE_BALL
                # rcv sends free ball to srv; rcv refills (sent ball over net)
                self._rcv.refill_hand()
                attacker, defender = self._srv, self._rcv
                atk_strat, def_strat = self._srv_strat, self._rcv_strat
        else:
            # Normal receive succeeded; receiving team attacks first
            attacker, defender = self._rcv, self._srv
            atk_strat, def_strat = self._rcv_strat, self._srv_strat

        # chase also needs serve_value for its target — already captured above
        # ── ATTACK LOOP ───────────────────────────────────────────────────────
        while exchange < MAX_EXCHANGES:
            exchange += 1
            attacker_role = None  # resolved after lane choice; used by ability checks below
            quick_lanes = []  # Track quick set lanes for this exchange

            # SET + HIT  — or ARMED ATTACK (no set, single OH/OPP lane)
            is_armed = next_attack_is_armed
            if is_armed:
                next_attack_is_armed = False
                attack_cards = self._phase_armed_attack(attacker, atk_strat, armed_lane)
                for _ln, _acs in sorted(attack_cards.items()):
                    for _ac in _acs:
                        self._narrate(
                            f"\n  Armed:   {attacker.name} lane {_ln} {_LANE_NAMES.get(_ln, '')}"
                            f"  card {_ac.card.value} ({_ac.position})"
                        )
            else:
                # Consume any pending set delta from a previous dig success
                set_delta = (
                    attacker.ability_engine.consume_set_delta()
                    if attacker.ability_engine else 0
                )
                set_card, template = self._phase_set(attacker, atk_strat, set_delta)
                _front_str = " + ".join(_LANE_NAMES.get(l, str(l)) for l in template.front_lanes)
                _back_str  = (" + ".join(_LANE_NAMES.get(l, str(l)) for l in template.back_lanes)
                               if template.back_lanes else "none")
                self._narrate(
                    f"\n  Set:     {attacker.name} card {set_card.value}"
                    f"  →  front [{_front_str}]  back [{_back_str}]"
                    f"  max {template.max_attackers}"
                )
                attack_cards = self._phase_hit(attacker, atk_strat, template)
                for _ln, _acs in sorted(attack_cards.items()):
                    for _ac in _acs:
                        if _ac.blind:
                            self._narrate(
                                f"  Attack:  {attacker.name} lane {_ln} {_LANE_NAMES.get(_ln, '')}"
                                f"  BLIND DRAW ({_ac.position})"
                            )
                        else:
                            self._narrate(
                                f"  Attack:  {attacker.name} lane {_ln} {_LANE_NAMES.get(_ln, '')}"
                                f"  card {_ac.card.value} ({_ac.position})"
                            )
                
                # Check if all attacks were canceled due to matching
                if not attack_cards:
                    # All lanes matched - automatic point for defender (fake failure)
                    return RallyResult(
                        winner_name=defender.name,
                        reason="All attacks canceled (matching front/back cards)",
                        rally_length=exchange,
                    )
                
                # Determine if this is a quick set (1-3) and which lanes are quick
                is_quick_set = set_card.value <= 3
                if is_quick_set:
                    # Quick set lanes are all front lanes with MB (lane 2) available
                    # This includes gap sets (lane 1), middle quick (lane 2), and slides (lane 3)
                    for lane in template.front_lanes:
                        if lane in attack_cards:
                            # Check if any attacker in this lane is MB
                            for ac in attack_cards[lane]:
                                if ac.position == "front" and lane in [1, 2, 3]:
                                    quick_lanes.append(lane)
                                    break
                
                # Fire on_quick_set if MB lane is among the front lanes
                if 2 in template.front_lanes and attacker.ability_engine:
                    attacker.ability_engine.activate_quick_set()
                # Notify engine of set card value (for tip_threshold_delta)
                if attacker.ability_engine:
                    attacker.ability_engine.activate_tip_threshold(set_card.value)

            # BLOCK COMMIT  (defender places cards blind to lane choice)
            block_layout, block_max, block_cards = self._phase_block_commit(
                defender, def_strat, list(attack_cards.keys()), quick_lanes=quick_lanes
            )

            # Phase 5: COMPREHENSIVE MATCHING SYSTEM
            # Process lanes from high to low by attack card value
            # Check matches in order: blocker-blocker, attacker-attacker, attacker-blocker
            # If all lanes eliminated, last processed lane determines rally winner
            
            # Sort lanes by highest attack card value (descending)
            lanes_by_attack_value = sorted(
                attack_cards.keys(),
                key=lambda ln: max(ac.card.value for ac in attack_cards[ln]),
                reverse=True
            )

            # Capture how many lanes exist when the blocker committed.
            # This determines whether a card-value match is a "draw-in deflect-out"
            # (multiple lanes → attacker lured the blocker) or a "block read"
            # (single lane → blocker had no choice, correctly reading the only attack).
            initial_attack_lane_count = len(attack_cards)

            last_match_result = None  # Track last elimination for multi-lane all-cancel scenario
            lanes_to_remove = []
            
            for lane in lanes_by_attack_value:
                attack_values = [ac.card.value for ac in attack_cards[lane]]
                block_cards_lane = block_cards.get(lane, [])
                block_values = [c.value for c in block_cards_lane]
                num_attackers = len(attack_values)
                num_blockers = len(block_values)
                
                # 1. Check BLOCKER-BLOCKER same value → CANCEL both, lane left unblocked
                # Two blockers of the same value over-commit and cancel each other out.
                # The attack proceeds against an empty block on this lane.
                if num_blockers == 2 and block_values[0] == block_values[1]:
                    block_cards[lane] = []
                    block_layout[lane] = 0
                    block_max[lane] = 0
                    self._narrate(
                        f"  Match:   lane {lane} — blockers both {block_values[0]}"
                        f"  →  block neutralized, attack unblocked"
                    )
                    continue  # skip attacker-blocker checks; lane stays active, unblocked
                
                # 2. Check ATTACKER-ATTACKER match (2+ attackers, any two same value)
                if num_attackers >= 2:
                    # Check for front+back match (most common case)
                    front_attackers = [ac for ac in attack_cards[lane] if ac.position == "front"]
                    back_attackers = [ac for ac in attack_cards[lane] if ac.position == "back"]
                    
                    if front_attackers and back_attackers:
                        front_val = front_attackers[0].card.value
                        back_val = back_attackers[0].card.value
                        if front_val == back_val:
                            lanes_to_remove.append(lane)
                            last_match_result = {
                                'winner': defender.name,
                                'reason': f"Front+back attackers match (lane {lane}, value {front_val}) - offensive confusion"
                            }
                            continue
                    
                    # Also check any other attacker duplicates (rare but possible)
                    for i in range(len(attack_values)):
                        for j in range(i + 1, len(attack_values)):
                            if attack_values[i] == attack_values[j]:
                                match_value = attack_values[i]
                                lanes_to_remove.append(lane)
                                last_match_result = {
                                    'winner': defender.name,
                                    'reason': f"Attacker-attacker match (lane {lane}, value {match_value}) - offensive confusion"
                                }
                                break
                        if lane in lanes_to_remove:
                            break
                    if lane in lanes_to_remove:
                        continue
                
                # 3. Check ATTACKER-BLOCKER matches
                if block_values:
                    if num_attackers == 1:
                        attacker_value = attack_values[0]
                        if attacker_value in block_values:
                            num_matching_blockers = block_values.count(attacker_value)
                            # Lane is eliminated — cannot be chosen for attack
                            lanes_to_remove.append(lane)
                            last_match_result = {
                                'winner': defender.name,
                                'reason': (
                                    f"Attacker-blocker match (lane {lane}, value {attacker_value}) "
                                    f"— lane eliminated"
                                )
                            }
                            self._narrate(
                                f"  Match:   lane {lane} — attacker {attacker_value} "
                                f"matches blocker(s) {attacker_value} ({num_matching_blockers}) "
                                f"→ lane eliminated"
                            )
                            continue
                    else:
                        # Multiple attackers: Remove any attacker-blocker matched cards
                        # Check if any matches exist
                        has_match = any(av in block_values for av in attack_values)
                        if has_match:
                            # Remove matched attack cards
                            matched_values = set(av for av in attack_values if av in block_values)
                            attack_cards[lane] = [ac for ac in attack_cards[lane] 
                                                  if ac.card.value not in matched_values]
                            
                            # Remove matched block cards
                            remaining_blocks = [c for c in block_cards_lane 
                                              if c.value not in matched_values]
                            block_cards[lane] = remaining_blocks
                            
                            # Recalculate block layout
                            if remaining_blocks:
                                block_layout[lane] = sum(c.value for c in remaining_blocks)
                                block_max[lane] = max(c.value for c in remaining_blocks)
                            else:
                                block_layout[lane] = 0
                                block_max[lane] = 0
                            
                            # If all attackers removed, mark lane for elimination
                            if not attack_cards[lane]:
                                lanes_to_remove.append(lane)
                                # No winner assignment - just lane elimination
            
            # Remove eliminated lanes
            for lane in lanes_to_remove:
                if lane in attack_cards:
                    # Discard all attack cards on this lane
                    for ac in attack_cards[lane]:
                        attacker.deck.discard(ac.card)
                    del attack_cards[lane]
                if lane in block_layout:
                    del block_layout[lane]
                if lane in block_max:
                    del block_max[lane]

            # Narrate match eliminations
            if lanes_to_remove and last_match_result:
                self._narrate(f"  Match:   {last_match_result['reason']}")
            
            # If all lanes eliminated, use last match result to determine winner
            if not attack_cards:
                if last_match_result:
                    return RallyResult(
                        winner_name=last_match_result['winner'],
                        reason=last_match_result['reason'],
                        rally_length=exchange,
                    )
                else:
                    # Fallback (should not happen)
                    return RallyResult(
                        winner_name=defender.name,
                        reason="All attack lanes canceled (matching defense)",
                        rally_length=exchange,
                    )

            # LANE CHOICE  (skipped for armed attack — lane already fixed)
            if is_armed:
                attack_lane = armed_lane
            else:
                # Convert attack_cards to simplified view for strategy.
                # Blind-drawn lanes are represented with value=0 so strategies
                # know the card exists but the value is not yet revealed.
                simplified_attacks: Dict[int, Card] = {}
                for lane, cards_list in attack_cards.items():
                    front = [ac for ac in cards_list if ac.position == "front"]
                    back  = [ac for ac in cards_list if ac.position == "back"]
                    known_front = [ac for ac in front if not ac.blind]
                    known_back  = [ac for ac in back  if not ac.blind]
                    if known_front:
                        simplified_attacks[lane] = known_front[0].card
                    elif known_back:
                        simplified_attacks[lane] = known_back[0].card
                    elif front:
                        simplified_attacks[lane] = Card(value=0, color=front[0].card.color)
                    else:
                        simplified_attacks[lane] = Card(value=0, color=back[0].card.color)

                attack_lane = atk_strat.choose_attack_lane(simplified_attacks, block_layout)

            # Phase 4: SLIDE_LANES ability - can shift to adjacent lane with lower block
            # Check if attacker has slide_lanes ability and an adjacent lane is available
            if not is_armed and attacker.ability_engine and attacker_role:
                # Get the card we would use on the current lane
                cards_on_current = attack_cards[attack_lane]
                front = [ac for ac in cards_on_current if ac.position == "front"]
                back = [ac for ac in cards_on_current if ac.position == "back"]
                current_card = front[0].card if front else back[0].card
                
                if attacker.ability_engine.slide_lanes(attacker_role, current_card.value):
                    # Can slide to adjacent lanes if they have attacks placed
                    adjacent_lanes = []
                    if attack_lane > 1 and (attack_lane - 1) in attack_cards:
                        adjacent_lanes.append(attack_lane - 1)
                    if attack_lane < 3 and (attack_lane + 1) in attack_cards:
                        adjacent_lanes.append(attack_lane + 1)
                    
                    # Choose adjacent lane with lowest block
                    if adjacent_lanes:
                        current_block = block_layout.get(attack_lane, 0)
                        best_adjacent = min(adjacent_lanes, key=lambda l: block_layout.get(l, 0))
                        if block_layout.get(best_adjacent, 0) < current_block:
                            # Slide to the better lane!
                            attack_lane = best_adjacent

            # Reveal any blind-drawn cards on the committed lane
            for _bac in attack_cards.get(attack_lane, []):
                if _bac.blind:
                    self._narrate(
                        f"  Reveal:  lane {attack_lane} blind draw"
                        f"  \u2192  card {_bac.card.value} ({_bac.position})"
                    )

            # Select which card to use from the chosen lane (prefer front-row)
            cards_on_lane = attack_cards[attack_lane]
            front_attacks = [ac for ac in cards_on_lane if ac.position == "front"]
            back_attacks = [ac for ac in cards_on_lane if ac.position == "back"]
            
            # Use front-row attack if available, otherwise back-row
            if front_attacks:
                selected_attack = front_attacks[0]
            else:
                selected_attack = back_attacks[0]
            
            attack_card = selected_attack.card
            is_back_row_attack = selected_attack.is_back_row()
            block_value = block_layout.get(attack_lane, 0)

            # Apply attacker abilities (attack bonus, pierce block, quick-set MB bonus)
            attacker_role = LANE_TO_ROLE.get(attack_lane)
            mb_qs_bonus   = (
                attacker.ability_engine.consume_mb_attack_bonus()
                if attacker.ability_engine else 0
            )
            effective_attack = attack_card.value
            effective_block  = block_value
            # Update engine hand size for hand-exhaustion condition checks
            if attacker.ability_engine:
                attacker.ability_engine.set_hand_size(len(attacker.hand))
            # Detect double block (2+ cards in lane) for commitment-read abilities
            is_double_blocked = block_layout.get(attack_lane, 0) > block_max.get(attack_lane, 0)
            if attacker.ability_engine and attacker_role:
                effective_attack += attacker.ability_engine.attack_value_bonus(
                    attacker_role, attack_card.value
                )
                effective_attack += attacker.ability_engine.over_block_bonus(
                    attacker_role, attack_card.value, is_double_blocked
                )
                
                # Phase 5: FORCE_HIGH_BLOCK - Filter out low-value blocks
                force_threshold = attacker.ability_engine.force_high_block_threshold(
                    attacker_role, attack_card.value
                )
                if force_threshold > 0:
                    # Recalculate block value excluding cards <= threshold
                    block_cards_in_lane = block_cards.get(attack_lane, [])
                    high_blocks = [c.value for c in block_cards_in_lane if c.value > force_threshold]
                    effective_block = sum(high_blocks)
                    # Update block_max if needed
                    if high_blocks:
                        block_max[attack_lane] = max(high_blocks)
                    else:
                        block_max[attack_lane] = 0
                
                # Pierce block checks (front or back-row)
                if attacker.ability_engine.pierce_block(attacker_role, attack_card.value):
                    effective_block = 0
                elif is_back_row_attack and attacker.ability_engine.back_row_pierce(attacker_role, attack_card.value):
                    # Phase 4: Back-row attacks can pierce blocks
                    effective_block = 0
                elif attacker.ability_engine.min_blocker_only(attacker_role, attack_card.value):
                    # Phase 4: Only the MINIMUM single blocker card counts
                    # Get all block cards in this lane
                    block_cards_in_lane = block_cards.get(attack_lane, [])
                    if block_cards_in_lane:
                        effective_block = min(c.value for c in block_cards_in_lane)
                    else:
                        effective_block = 0
                elif attacker.ability_engine.single_block_only(attacker_role, attack_card.value):
                    # Only the highest single blocker card in this lane counts
                    effective_block = block_max.get(attack_lane, 0)
            # MB quick-set bonus only applies if MB is actually the attacker
            if attack_lane == 2:
                effective_attack += mb_qs_bonus

            # Hold card check: retain chosen attack card if ability fires
            # Collect all cards from all lanes
            all_attack_cards = []
            for cards_list in attack_cards.values():
                all_attack_cards.extend([ac.card for ac in cards_list])
            
            cards_to_discard = all_attack_cards
            if (attacker.ability_engine and attacker_role
                    and attacker.held_card is None
                    and attacker.ability_engine.hold_card_check(
                        attacker_role, attack_card.value)):
                attacker.held_card = attack_card
                cards_to_discard = [c for c in cards_to_discard if c is not attack_card]

            # Committed attack cards discarded; attacker refills (ball went over net)
            attacker.discard_many(cards_to_discard)
            attacker.refill_hand()

            # ── SHOT SELECTION ────────────────────────────────────────────────
            # Narrate the attack heading before shot resolution
            _atk_eff = (f"{attack_card.value}→{effective_attack}"
                        if effective_attack != attack_card.value else str(effective_attack))
            _blk_eff = (f"{block_value}→{effective_block}"
                        if effective_block != block_value else str(effective_block))
            self._narrate(
                f"  Resolve: {attacker.name} lane {attack_lane} {_LANE_NAMES.get(attack_lane, '')}"
                f"  atk {_atk_eff}  vs  blk {_blk_eff}"
            )

            # Dynamic tip threshold: base 3, expanded by setter ability on high set
            tip_threshold = 3
            if attacker.ability_engine:
                tip_threshold += attacker.ability_engine.consume_tip_threshold_delta()

            # Wipe off the block: card=1 ability, block present → instant point
            if (
                block_value > 0
                and attacker.ability_engine and attacker_role
                and attacker.ability_engine.wipe_block(attacker_role, attack_card.value)
            ):
                return RallyResult(
                    winner_name=attacker.name,
                    reason=(
                        f"Wipe off the block "
                        f"(card={attack_card.value}, block={block_value})"
                    ),
                    rally_length=exchange,
                )

            # Special shot types override tip/hit (checked in priority order)
            shot = "hit"
            if attacker.ability_engine and attacker_role:
                if attacker.ability_engine.roll_shot(attacker_role, attack_card.value):
                    shot = "roll"
                    effective_block = 0  # roll shot goes over block
                elif attacker.ability_engine.heavy_spin(attacker_role, attack_card.value):
                    shot = "heavy_spin"
                    effective_block = 0  # heavy spin bypasses the block
                elif attacker.ability_engine.seam_shot(attacker_role, attack_card.value):
                    shot = "seam"
            
            # Back-row attacks cannot tip
            if shot == "hit" and effective_attack <= tip_threshold and not is_back_row_attack:
                shot = atk_strat.choose_tip_or_hit(effective_attack, effective_block)

            self._narrate(f"  Shot:    {shot.upper()}")

            if shot == "tip":
                # ── TIP ──────────────────────────────────────────────────────
                # Attacker tip bonus (on_tip trigger)
                effective_tip = effective_attack
                if attacker.ability_engine and attacker_role:
                    effective_tip += attacker.ability_engine.tip_value_bonus(attacker_role)
                dig_card = self._phase_dig(defender, def_strat, effective_tip, "tip")
                # Determine which defender digs based on attack lane and card parity
                defender_role = get_dig_defender_role(attack_lane, attack_card.value)
                # Defender tip-dig threshold bonus (positive = easier to dig)
                effective_tip_dig = dig_card.value
                if defender.ability_engine:
                    effective_tip_dig += defender.ability_engine.tip_dig_threshold(
                        defender_role
                    )
                if effective_tip_dig <= effective_tip:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_tip_dig})" if effective_tip_dig != dig_card.value else "")
                        + f"  ≤ tip {effective_tip}  →  DUG"
                    )
                    # Tips are too fast for adjacent coverage — setter dig always breaks play
                    self._broken_play = (defender_role == PlayerRole.SETTER)
                    attacker, defender = defender, attacker
                    atk_strat, def_strat = def_strat, atk_strat
                    continue
                else:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_tip_dig})" if effective_tip_dig != dig_card.value else "")
                        + f"  > tip {effective_tip}  →  NOT DUG"
                    )
                    return RallyResult(
                        winner_name=attacker.name,
                        reason=(
                            f"Tip not dug "
                            f"(tip={effective_tip}, dig={dig_card.value})"
                        ),
                        rally_length=exchange,
                    )

            elif shot == "roll":
                # ── ROLL SHOT ─────────────────────────────────────────────────
                # Block ignored (effective_block already set to 0 above).
                # Dug like a tip — defender gets tip_dig_threshold bonus.
                # If not dug, normal chase rules apply.
                dig_card = self._phase_dig(defender, def_strat, effective_attack, "tip")
                defender_role = get_dig_defender_role(attack_lane, attack_card.value)
                # Defender tip-dig threshold bonus (positive = easier to dig)
                effective_dig = dig_card.value
                if defender.ability_engine:
                    effective_dig += defender.ability_engine.tip_dig_threshold(defender_role)
                if effective_dig <= effective_attack:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                        + f"  ≤ roll {effective_attack}  →  DUG"
                    )
                    # Roll shots are fast — setter dig always breaks play (like tips)
                    self._broken_play = (defender_role == PlayerRole.SETTER)
                    attacker, defender = defender, attacker
                    atk_strat, def_strat = def_strat, atk_strat
                    continue
                else:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                        + f"  > roll {effective_attack}  →  NOT DUG"
                    )
                    # Failed dig → chase sequence (normal kill rules)
                    dig_fail_bonus = (
                        defender.ability_engine.dig_failure_chase_bonus()
                        if defender.ability_engine else 0
                    )
                    chase = self._phase_chase(
                        defender, def_strat, dig_card.value, effective_attack,
                        extra_bonus=dig_fail_bonus
                    )
                    if chase.outcome == ChaseOutcome.FAILED:
                        return RallyResult(
                            winner_name=attacker.name,
                            reason=(
                                f"Roll shot kill, chase failed "
                                f"(roll={effective_attack}, dig={dig_card.value})"
                            ),
                            rally_length=exchange,
                        )
                    elif chase.outcome == ChaseOutcome.ARMED_ATTACK:
                        # Defending team chased → they now attack with armed lane
                        attacker, defender = defender, attacker
                        atk_strat, def_strat = def_strat, atk_strat
                        next_attack_is_armed = True
                        armed_lane = chase.armed_lane
                        continue
                    else:  # FREE_BALL
                        # Defender sends free ball to attacker; attacker stays on offense.
                        # Defender refills (they sent ball over net).
                        defender.refill_hand()
                        continue

            elif shot == "heavy_spin":
                # ── HEAVY SPIN ────────────────────────────────────────────────
                # Block ignored (effective_block already set to 0 above).
                # A failed dig is an instant point — no chase.
                dig_target = effective_attack
                if attacker.ability_engine and attacker_role:
                    dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
                # Determine defender role before cover decision
                defender_role = get_dig_defender_role(attack_lane, attack_card.value)
                # Cover attempt: strategy may send Libero/DS to intercept setter's zone
                cover_threshold = _cover_threshold(defender)
                cover_attempted = False
                if defender_role == PlayerRole.SETTER and cover_threshold > 0:
                    if def_strat.cover_draws_from_deck():
                        # Blind deck flip — strategy draws top card, no hand selection
                        if defender.deck.draw_pile_size > 0:
                            cover_card = defender.deck.draw()
                            defender.deck.discard(cover_card)
                            dig_card = cover_card
                            cover_attempted = True
                        else:
                            dig_card = self._phase_dig(defender, def_strat, dig_target, "normal")
                    elif defender.hand:
                        cover_card = def_strat.choose_cover_attempt(defender.hand, cover_threshold)
                        if cover_card is not None:
                            defender.play_card(cover_card)
                            dig_card = cover_card
                            cover_attempted = True
                        else:
                            dig_card = self._phase_dig(defender, def_strat, dig_target, "normal")
                    else:
                        dig_card = self._phase_dig(defender, def_strat, dig_target, "normal")
                else:
                    dig_card = self._phase_dig(defender, def_strat, dig_target, "normal")
                # Determine which defender digs based on attack lane and card parity
                effective_dig = dig_card.value
                if defender.ability_engine:
                    effective_dig += defender.ability_engine.defender_dig_threshold(
                        defender_role
                    )
                if effective_dig >= dig_target:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                        + f"  ≥ {dig_target}  →  HEAVY SPIN DUG"
                    )
                    if defender.ability_engine:
                        defender.ability_engine.record_dig_success(defender_role, "normal")
                        # Phase 5: DECK_SWAP_OPPONENT - Hand disruption on dig success
                        if defender.ability_engine.deck_swap_opponent_on_dig("normal"):
                            if attacker.hand:
                                # Replace highest card with deck top
                                highest = max(attacker.hand, key=lambda c: c.value)
                                attacker.hand.remove(highest)
                                attacker.deck.discard(highest)
                                if attacker.deck.draw_pile_size > 0:
                                    new_card = attacker.deck.draw()
                                    attacker.hand.append(new_card)
                    # Broken play: setter dig unless cover was attempted and card cleared threshold
                    if defender_role == PlayerRole.SETTER:
                        if cover_attempted and dig_card.value >= cover_threshold:
                            self._narrate(f"  Cover:   adjacent player reached (card {dig_card.value}) — no broken play")
                            self._broken_play = False
                        else:
                            self._broken_play = True
                    else:
                        self._broken_play = False
                    attacker, defender = defender, attacker
                    atk_strat, def_strat = def_strat, atk_strat
                    continue
                else:
                    self._narrate(
                        f"  Dig:     {defender.name} card {dig_card.value}"
                        + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                        + f"  < {dig_target}  →  NOT DUG (no chase)"
                    )
                    return RallyResult(
                        winner_name=attacker.name,
                        reason=(
                            f"Heavy spin not dug, no chase "
                            f"(attack={effective_attack}, dig={dig_card.value})"
                        ),
                        rally_length=exchange,
                    )

            else:
                # ── HIT / SEAM ────────────────────────────────────────────────
                outcome = resolve_attack(effective_attack, effective_block)
                self._narrate(f"  Outcome: {outcome.name}  (atk {effective_attack} vs blk {effective_block})")

                if outcome == AttackOutcomeType.STUFFED:
                    return RallyResult(
                        winner_name=defender.name,
                        reason=(
                            f"Stuffed "
                            f"(attack={effective_attack}, block={effective_block})"
                        ),
                        rally_length=exchange,
                    )

                elif outcome == AttackOutcomeType.DEFLECT:
                    if shot == "seam":
                        # Seam shot: deflect redirects onto defending team's side
                        return RallyResult(
                            winner_name=attacker.name,
                            reason=(
                                f"Seam shot deflect "
                                f"(attack={effective_attack}, block={effective_block})"
                            ),
                            rally_length=exchange,
                        )
                    
                    # Determine which side digs based on block advantage
                    diff = effective_block - effective_attack
                    
                    if diff <= 2:
                        # Soft deflection to defender's side (blocker digs)
                        digger = defender
                        dig_strat = def_strat
                        side_label = "defender"
                    else:  # diff 3-4
                        # Harder deflection to attacker's side (attacker digs)
                        digger = attacker
                        dig_strat = atk_strat
                        side_label = "attacker"
                    
                    deflect_penalty = (
                        defender.ability_engine.deflect_dig_threshold()
                        if defender.ability_engine else 0
                    )
                    # Deflect value = highest single block card in the lane (not full attack)
                    deflect_target = max(1, block_max.get(attack_lane, 0) - deflect_penalty)
                    
                    # Back-row deflections are +2 harder to dig
                    if is_back_row_attack:
                        deflect_target += 2
                    
                    dig_card = self._phase_dig(
                        digger, dig_strat, deflect_target, "tip"
                    )
                    
                    if dig_card.value <= deflect_target:
                        self._narrate(
                            f"  Deflect: {side_label} side, {digger.name} card {dig_card.value}"
                            f"  ≤ {deflect_target}  →  DUG"
                        )
                        # Rally continues - if defender dug, they become attacker
                        if digger is defender:
                            attacker, defender = defender, attacker
                            atk_strat, def_strat = def_strat, atk_strat
                        continue
                    else:
                        self._narrate(
                            f"  Deflect: {side_label} side, {digger.name} card {dig_card.value}"
                            f"  > {deflect_target}  →  NOT DUG"
                        )
                        # If digger failed, opposite team wins
                        winner = defender if digger is attacker else attacker
                        return RallyResult(
                            winner_name=winner.name,
                            reason=(
                                f"Deflect not dug "
                                f"(deflect_val={deflect_target}, dig={dig_card.value}, diff={diff})"
                            ),
                            rally_length=exchange,
                        )

                else:  # KILL
                    # dig_threshold abilities shift the attack level the dig must beat
                    dig_target = effective_attack
                    if attacker.ability_engine and attacker_role:
                        dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
                    # Determine defender role before cover decision
                    defender_role = get_dig_defender_role(attack_lane, attack_card.value)
                    # Cover attempt: strategy may send Libero/DS to intercept setter's zone
                    cover_threshold = _cover_threshold(defender)
                    cover_attempted = False
                    if defender_role == PlayerRole.SETTER and cover_threshold > 0:
                        if def_strat.cover_draws_from_deck():
                            # Blind deck flip — strategy draws top card, no hand selection
                            if defender.deck.draw_pile_size > 0:
                                cover_card = defender.deck.draw()
                                defender.deck.discard(cover_card)
                                dig_card = cover_card
                                cover_attempted = True
                            else:
                                dig_card = self._phase_dig(
                                    defender, def_strat, dig_target, "normal"
                                )
                        elif defender.hand:
                            cover_card = def_strat.choose_cover_attempt(defender.hand, cover_threshold)
                            if cover_card is not None:
                                defender.play_card(cover_card)
                                dig_card = cover_card
                                cover_attempted = True
                            else:
                                dig_card = self._phase_dig(
                                    defender, def_strat, dig_target, "normal"
                                )
                        else:
                            dig_card = self._phase_dig(
                                defender, def_strat, dig_target, "normal"
                            )
                    else:
                        dig_card = self._phase_dig(
                            defender, def_strat, dig_target, "normal"
                        )
                    # Defender dig-threshold bonus adds to effective dig value
                    effective_dig = dig_card.value
                    if defender.ability_engine:
                        effective_dig += defender.ability_engine.defender_dig_threshold(
                            defender_role
                        )
                    if effective_dig >= dig_target:
                        self._narrate(
                            f"  Dig:     {defender.name} card {dig_card.value}"
                            + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                            + f"  ≥ {dig_target}  →  DUG"
                        )
                        # Successful dig → record ability trigger, then transition
                        if defender.ability_engine:
                            defender.ability_engine.record_dig_success(
                                defender_role, "normal"
                            )
                            # Phase 5: DECK_SWAP_OPPONENT - Hand disruption on dig success
                            if defender.ability_engine.deck_swap_opponent_on_dig("normal"):
                                if attacker.hand:
                                    # Replace highest card with deck top
                                    highest = max(attacker.hand, key=lambda c: c.value)
                                    attacker.hand.remove(highest)
                                    attacker.deck.discard(highest)
                                    if attacker.deck.draw_pile_size > 0:
                                        new_card = attacker.deck.draw()
                                        attacker.hand.append(new_card)
                        # Broken play: setter dig unless cover was attempted and card cleared threshold
                        if defender_role == PlayerRole.SETTER:
                            if cover_attempted and dig_card.value >= cover_threshold:
                                self._narrate(f"  Cover:   adjacent player reached (card {dig_card.value}) — no broken play")
                                self._broken_play = False
                            else:
                                self._broken_play = True
                        else:
                            self._broken_play = False
                        attacker, defender = defender, attacker
                        atk_strat, def_strat = def_strat, atk_strat
                        continue
                    else:
                        self._narrate(
                            f"  Dig:     {defender.name} card {dig_card.value}"
                            + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                            + f"  < {dig_target}  →  NOT DUG — chase needed"
                        )
                        # Check if this was a quick set - no chase allowed
                        if attack_lane in quick_lanes:
                            return RallyResult(
                                winner_name=attacker.name,
                                reason=(
                                    f"Quick set kill, no chase "
                                    f"(quick set lane {attack_lane}, dig={dig_card.value})"
                                ),
                                rally_length=exchange,
                            )
                        # Check no_chase ability before starting chase
                        if (
                            attacker.ability_engine and attacker_role
                            and attacker.ability_engine.no_chase(
                                attacker_role, attack_card.value
                            )
                        ):
                            return RallyResult(
                                winner_name=attacker.name,
                                reason=(
                                    f"Kill, no chase "
                                    f"(attack={effective_attack} > "
                                    f"block={effective_block}, dig={dig_card.value})"
                                ),
                                rally_length=exchange,
                            )
                        # Failed dig → CHASE (applies to normal digs)
                        # on_dig_failure bonus stacks on top of the chase's own on_chase bonus
                        dig_fail_bonus = (
                            defender.ability_engine.dig_failure_chase_bonus()
                            if defender.ability_engine else 0
                        )
                        chase = self._phase_chase(
                            defender, def_strat, dig_card.value, dig_target,
                            extra_bonus=dig_fail_bonus
                        )
                        if chase.outcome == ChaseOutcome.FAILED:
                            return RallyResult(
                                winner_name=attacker.name,
                                reason=(
                                    f"Kill (attack={effective_attack} > "
                                    f"block={effective_block}), chase failed "
                                    f"(dig={dig_card.value})"
                                ),
                                rally_length=exchange,
                            )
                        elif chase.outcome == ChaseOutcome.ARMED_ATTACK:
                            # Defending team chased → they now attack with armed lane
                            attacker, defender = defender, attacker
                            atk_strat, def_strat = def_strat, atk_strat
                            next_attack_is_armed = True
                            armed_lane = chase.armed_lane
                            continue
                        else:  # FREE_BALL
                            # Defender sends free ball to attacker; attacker stays on offense.
                            # Defender refills (they sent ball over net).
                            defender.refill_hand()
                            continue

        # Safety: rally hit the exchange cap
        return RallyResult(
            winner_name=self._rng.choice([self._srv.name, self._rcv.name]),
            reason="Rally limit reached",
            rally_length=exchange,
        )

    # ── Phase helpers ─────────────────────────────────────────────────────────

    def _phase_chase(
        self,
        team: Team,
        strat: BaseStrategy,
        running_total: int,
        target_value: int,
        extra_bonus: int = 0,
    ) -> ChaseResult:
        """
        Two-attempt chase after a failed normal dig or failed reception.

        Attempt 1 (adjacent player):
          running_total + card1 >= target  →  ARMED_ATTACK (OH or OPP, player chooses)
        Attempt 2 (player adjacent to attempt-1 player):
          running_total + card1 + card2 >= target  →  FREE_BALL to middle back
        Both fail  →  FAILED (attacking team wins the point)

        Chase does NOT apply to tip digs or deflect digs.
        """
        # Apply on_chase ability bonus + any on_dig_failure extra passed in
        running_total += extra_bonus + (
            team.ability_engine.chase_bonus() if team.ability_engine else 0
        )
        self._narrate(
            f"\n  Chase:   {team.name}  need {target_value}  starting at {running_total}"
        )
        # Attempt 1
        if team.hand:
            card1 = strat.choose_chase_card(team.hand, running_total, target_value)
            team.play_card(card1)
        else:
            card1 = team.blind_draw()
        running_total += card1.value
        self._narrate(f"  Chase 1: card {card1.value}  →  total {running_total} / {target_value}")

        if running_total >= target_value:
            self._narrate(f"  Chase:   SUCCEEDED → armed attack")
            lane = strat.choose_armed_attack_lane(team.hand)
            return ChaseResult(outcome=ChaseOutcome.ARMED_ATTACK, armed_lane=lane)

        # Attempt 2
        if team.hand:
            card2 = strat.choose_chase_card(team.hand, running_total, target_value)
            team.play_card(card2)
        else:
            card2 = team.blind_draw()
        running_total += card2.value
        self._narrate(f"  Chase 2: card {card2.value}  →  total {running_total} / {target_value}")

        if running_total >= target_value:
            self._narrate(f"  Chase:   SUCCEEDED → free ball")
            return ChaseResult(outcome=ChaseOutcome.FREE_BALL)

        self._narrate(f"  Chase:   FAILED  (total {running_total} < {target_value})")
        return ChaseResult(outcome=ChaseOutcome.FAILED)

    def _phase_armed_attack(
        self, team: Team, strat: BaseStrategy, lane: int
    ) -> Dict[int, Card]:
        """
        Armed attack after a successful first chase.
        No set card is played; attack is limited to a single OH (lane 1) or
        OPP (lane 3) lane, leaving the attacker exposed to a double block.
        Returns {lane: [AttackCard]} with cards committed (not yet discarded).
        Refill happens in the main loop after discard_many (ball crossed the net).
        """
        # Create a simple template for armed attack (single front-row lane)
        from .players import SetTemplate
        armed_template = SetTemplate(front_lanes=[lane], back_lanes=[], max_attackers=1)
        
        if team.hand:
            placements = strat.choose_hit_cards(team.hand, armed_template)
            cards_to_commit = [card for _, card, _ in placements]
            team.commit_cards(cards_to_commit)
        else:
            card = team.deck.draw()
            placements = [(lane, card, "front")]
        
        # Organize by lane
        attack_cards: Dict[int, List[AttackCard]] = {}
        for l, card, position in placements:
            if l not in attack_cards:
                attack_cards[l] = []
            attack_cards[l].append(AttackCard(card=card, position=position))
        
        return attack_cards

    def _phase_serve(self) -> Tuple[Card, GridPlayer]:
        eligible = self._rcv.eligible_receivers()
        if self._srv.hand:
            card, target = self._srv_strat.choose_serve(self._srv.hand, eligible)
            self._srv.play_card(card)
        else:
            card = self._srv.deck.draw()
            self._srv.deck.discard(card)
            target = self._rng.choice(eligible)
        self._srv.refill_hand()
        return card, target

    def _phase_receive(self, serve_value: int) -> Card:
        if self._rcv.hand:
            card = self._rcv_strat.choose_receive_card(self._rcv.hand, serve_value)
            self._rcv.play_card(card)
        else:
            card = self._rcv.deck.draw()
            self._rcv.deck.discard(card)
        # No refill here: hand replenishes only when the ball crosses the net.
        return card

    def _phase_set(
        self, team: Team, strat: BaseStrategy, set_value_delta: int = 0
    ) -> Tuple[Card, SetTemplate]:
        if team.hand:
            card = strat.choose_set_card(team.hand, broken_play=self._broken_play)
            team.play_card(card)
        else:
            card = team.deck.draw()
            team.deck.discard(card)
        # No refill here: hand replenishes only when the ball crosses the net.
        
        # Apply set_value_delta (dig-success ability)
        effective_value = card.value + set_value_delta
        
        # Apply Setter's passive on_set bonus ONLY if not broken play
        if not self._broken_play and team.ability_engine:
            set_bonus = team.ability_engine.on_set_bonus(card.value)
            effective_value += set_bonus
        
        effective_value = min(10, max(1, effective_value))
        
        # Select template based on broken play status
        template = BROKEN_PLAY_TEMPLATES[effective_value] if self._broken_play else SETTER_TEMPLATES[effective_value]
        
        # Reset broken play flag after use
        self._broken_play = False
        
        return card, template

    def _phase_hit(
        self, team: Team, strat: BaseStrategy, template: SetTemplate
    ) -> Dict[int, List[AttackCard]]:
        """
        Place attack cards according to template.

        Hand cards are placed first (known values).  If the hand does not fill
        all available slots up to template.max_attackers, the remaining slots
        are filled with blind draws straight from the deck — face-down cards
        whose values are hidden from both strategies during block commit and
        lane commitment.  Values are revealed in the narrative after the
        attacker locks in a lane.

        Returns {lane: [AttackCard]} where each lane can have multiple cards
        (front + back), some potentially with blind=True.
        """
        attack_cards: Dict[int, List[AttackCard]] = {}

        if team.hand:
            # Exchange card ability: before committing, optionally swap worst hand
            # card for the deck top (fires when any front-row player has the ability)
            if team.ability_engine and team.ability_engine.exchange_card_eligible():
                deck_top = team.deck.peek()
                if deck_top is not None:
                    swap_out = strat.choose_exchange_card(team.hand, deck_top)
                    if swap_out is not None and swap_out in team.hand:
                        team.play_card(swap_out)          # removes from hand, discards
                        new_card = team.deck.draw()
                        team.hand.append(new_card)
            placements = strat.choose_hit_cards(team.hand, template)
            cards_to_commit = [card for _, card, _ in placements]
            team.commit_cards(cards_to_commit)
            for lane, card, position in placements:
                attack_cards.setdefault(lane, []).append(
                    AttackCard(card=card, position=position, blind=False)
                )
            placed = len(placements)
        else:
            placed = 0  # no hand — all slots filled with blind draws below

        # ── BLIND DRAWS ────────────────────────────────────────────────────
        # If hand placements didn't reach max_attackers, fill remaining slots
        # with cards drawn directly from the deck (face-down threats).
        # Priority: front lanes first, then back lanes not already covered,
        # then back lanes that share a lane with a front placement.
        if placed < template.max_attackers:
            used_slots: set = {
                (lane, ac.position)
                for lane, acs in attack_cards.items()
                for ac in acs
            }
            front_set = set(template.front_lanes)
            candidates: List[Tuple[int, str]] = []
            for lane in sorted(template.front_lanes):
                if (lane, "front") not in used_slots:
                    candidates.append((lane, "front"))
            for lane in sorted(l for l in template.back_lanes if l not in front_set):
                if (lane, "back") not in used_slots:
                    candidates.append((lane, "back"))
            for lane in sorted(l for l in template.back_lanes if l in front_set):
                if (lane, "back") not in used_slots:
                    candidates.append((lane, "back"))

            for lane, position in candidates[: template.max_attackers - placed]:
                blind_card = team.deck.draw()
                attack_cards.setdefault(lane, []).append(
                    AttackCard(card=blind_card, position=position, blind=True)
                )

        # Front+back matching is handled in the comprehensive matching system
        # (after block commit) — blind cards participate with their actual values.
        return attack_cards

    def _phase_block_commit(
        self, team: Team, strat: BaseStrategy, attack_lanes: List[int],
        quick_lanes: List[int] = None
    ) -> Tuple[Dict[int, int], Dict[int, int], Dict[int, List[Card]]]:
        """
        Returns (block_layout, block_max, block_cards) where:
          block_layout : {lane: total_block_value}  — unblocked lanes absent (value=0)
          block_max    : {lane: highest_single_card} — max individual card per lane
          block_cards  : {lane: [Card]} — actual cards placed for matching checks
          
        Quick set rules (when quick_lanes is provided):
          - Only 1 card allowed per quick lane
          - Blocker MUST draw from deck (blind) for quick lanes
        """
        if quick_lanes is None:
            quick_lanes = []
        
        if team.hand:
            # Compute wild_block threshold (max across all blocking roles)
            wild_threshold = 0
            if team.ability_engine:
                for role in (PlayerRole.OH, PlayerRole.MB, PlayerRole.OPP):
                    wild_threshold = max(
                        wild_threshold,
                        team.ability_engine.wild_block_threshold(role)
                    )
            wide_spread_threshold = 0
            if team.ability_engine:
                for role in (PlayerRole.OH, PlayerRole.MB, PlayerRole.OPP):
                    wide_spread_threshold = max(
                        wide_spread_threshold,
                        team.ability_engine.wide_spread_bonus(role)
                    )
            
            # For quick lanes: MUST draw blind from deck
            placement = {}
            non_quick_lanes = [ln for ln in attack_lanes if ln not in quick_lanes]
            
            if quick_lanes:
                # Handle quick lanes first (blind draws)
                for lane in quick_lanes:
                    if team.deck.draw_pile_size > 0:
                        card = team.deck.draw()
                        placement[lane] = [card]  # Only 1 card allowed
                        self._narrate(f"  Block:   Quick set lane {lane} → blind draw card {card.value}")
                    # If no deck, skip this lane (no block)
            
            # Handle non-quick lanes normally (hand selection)
            if non_quick_lanes:
                hand_placement = strat.choose_block_cards(
                    team.hand, non_quick_lanes, wild_threshold, wide_spread_threshold
                )
                placement.update(hand_placement)
                # Commit hand cards
                hand_cards = [c for ln in non_quick_lanes for c in hand_placement.get(ln, [])]
                team.commit_cards(hand_cards)
            
            # Collect all block cards
            all_block_cards = [c for cards in placement.values() for c in cards]
        else:
            # No-hand team: flip one card per attacked lane, lowest lane to highest.
            placement = {}
            for lane in sorted(attack_lanes):
                # Quick lanes still get only 1 card
                card = team.deck.draw()
                placement[lane] = [card]
            all_block_cards = [c for cards in placement.values() for c in cards]

        # Discard block cards; no refill (blocking does not send ball over net).
        team.discard_many(all_block_cards)

        block_layout = {lane: sum(c.value for c in cards) for lane, cards in placement.items()}
        block_max = {lane: max(c.value for c in cards) for lane, cards in placement.items()}

        # Apply defender block abilities
        if team.ability_engine:
            # Per-player block value bonus (e.g. OH, MB, OPP individual bonuses)
            for lane in list(block_layout):
                role = LANE_TO_ROLE.get(lane)
                if role:
                    bonus = team.ability_engine.block_value_bonus(role)
                    if bonus:
                        block_layout[lane] = block_layout[lane] + bonus
            # Draw and add block: draw N cards from deck, add to block, discard
            for lane in list(block_layout):
                role = LANE_TO_ROLE.get(lane)
                if role:
                    draw_count = team.ability_engine.draw_and_add_block(role)
                    if draw_count > 0:
                        drawn_cards = []
                        for _ in range(draw_count):
                            if team.deck.total_size > 0:
                                card = team.deck.draw()
                                drawn_cards.append(card)
                        
                        # Calculate value to add based on drawn cards
                        drawn_value = 0
                        if drawn_cards:
                            if len(drawn_cards) == 2:
                                # Special rule: keep both if either card is ≤5, otherwise keep highest
                                if drawn_cards[0].value <= 5 or drawn_cards[1].value <= 5:
                                    drawn_value = drawn_cards[0].value + drawn_cards[1].value
                                else:
                                    drawn_value = max(drawn_cards[0].value, drawn_cards[1].value)
                            else:
                                # For 1 card or other counts, add all
                                drawn_value = sum(c.value for c in drawn_cards)
                        
                        if drawn_value > 0:
                            block_layout[lane] = block_layout[lane] + drawn_value
                        team.discard_many(drawn_cards)
            # MB adjacent block bonus: adds to lanes 1 and 3 when MB is blocking
            adj = team.ability_engine.adjacent_block_bonus(mb_is_blocking=2 in block_layout)
            if adj:
                for adj_lane in (1, 3):
                    block_layout[adj_lane] = block_layout.get(adj_lane, 0) + adj
            # Wide spread bonus: fires when |card1-card2| >= N, adds +N to block sum
            for lane in list(block_layout):
                role = LANE_TO_ROLE.get(lane)
                if role:
                    wsp = team.ability_engine.wide_spread_bonus(role)
                    if wsp:
                        lane_cards = placement.get(lane, [])
                        if len(lane_cards) == 2 and abs(lane_cards[0].value - lane_cards[1].value) >= wsp:
                            block_layout[lane] = block_layout[lane] + wsp

        return block_layout, block_max, placement

    def _phase_dig(
        self, team: Team, strat: BaseStrategy, target_value: int, dig_type: str
    ) -> Card:
        if team.hand:
            card = strat.choose_dig_card(team.hand, target_value, dig_type)
            team.play_card(card)
        else:
            card = team.deck.draw()
            team.deck.discard(card)
        # No refill here: hand replenishes only when the ball crosses the net.
        return card


class Game:
    """
    Manages score, serving order, and runs rallies until POINTS_TO_WIN.
    """

    def __init__(
        self,
        team_a: Team,
        team_b: Team,
        strategy_a: BaseStrategy,
        strategy_b: BaseStrategy,
        rng: random.Random,
    ) -> None:
        self._team_a = team_a
        self._team_b = team_b
        self._strat_a = strategy_a
        self._strat_b = strategy_b
        self._rng = rng
        self._scores: Dict[str, int] = {team_a.name: 0, team_b.name: 0}
        # Randomly determine first server
        self._server: Team = rng.choice([team_a, team_b])

    def play(self) -> GameResult:
        self._team_a.draw_starting_hand()
        self._team_b.draw_starting_hand()

        rally_results: List[RallyResult] = []

        while max(self._scores.values()) < POINTS_TO_WIN:
            serving   = self._server
            receiving = self._team_b if serving is self._team_a else self._team_a
            srv_strat = self._strat_a if serving is self._team_a else self._strat_b
            rcv_strat = self._strat_b if serving is self._team_a else self._strat_a

            rally = Rally(serving, receiving, srv_strat, rcv_strat, self._rng)
            result = rally.play()
            rally_results.append(result)

            self._scores[result.winner_name] += 1
            # Winner of the rally earns the serve
            self._server = (
                self._team_a if result.winner_name == self._team_a.name else self._team_b
            )

        winner = max(self._scores, key=lambda k: self._scores[k])
        return GameResult(
            winner_name=winner,
            scores=dict(self._scores),
            rally_results=rally_results,
        )
