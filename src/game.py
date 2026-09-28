from __future__ import annotations

import random
from dataclasses import dataclass
from typing import Dict, List, Tuple, Optional

from .cards import Card
from .players import (
    Team, GridPlayer, LANE_TO_ROLE, PlayerRole,
    SETTER_TEMPLATES, BROKEN_PLAY_TEMPLATES, SetTemplate
)
from .game_state import AttackOutcomeType, ChaseOutcome, ChaseResult, RallyResult, GameResult
from .strategies import BaseStrategy

POINTS_TO_WIN = 15
MAX_EXCHANGES = 200  # safety cap to prevent infinite rallies
_LANE_NAMES = {1: "OH", 2: "MB", 3: "OPP"}   # short lane labels for narrative
TIP_THRESHOLD = 5  # any (effective) attack card <= this may be declared a tip, front row only


@dataclass
class AttackCard:
    """Represents a single attack card with position information."""
    card: Card
    position: str  # "front" or "back"
    blind: bool = False   # True = drawn from deck face-down; value hidden during block/lane decisions

    def is_back_row(self) -> bool:
        return self.position == "back"


@dataclass
class _CardOutcome:
    """
    Result of resolving one attack card within a chosen lane.

    result   — set when the rally is over (win/loss decided).
    stuffed  — set when this card was genuinely stuffed and another combo
               card remains to try (never set together with `result`, and
               never set for the last card in the lane — that case resolves
               straight to a STUFFED `result` instead).
    attacker/defender/atk_strat/def_strat — the roles to carry into the next
               exchange when the rally continues (result is None, stuffed is
               False).
    """
    result: Optional[RallyResult] = None
    stuffed: bool = False
    attacker: Optional[Team] = None
    defender: Optional[Team] = None
    atk_strat: Optional[BaseStrategy] = None
    def_strat: Optional[BaseStrategy] = None


def resolve_attack(attack_value: int, block_value: int) -> AttackOutcomeType:
    """Determine attack outcome purely from numeric values.

    Attack > Block  → KILL
    Attack == Block → DEFLECT (exact tie; ball falls to attacker's own side)
    Attack < Block  → STUFFED
    """
    if attack_value > block_value:
        return AttackOutcomeType.KILL
    if attack_value == block_value:
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
            # SUCCESS → reception completed normally; receiving team attacks.
            self._narrate("  Chase:   SUCCEEDED — reception completed")

        # Receiving team attacks first (clean pass or a successfully chased reception)
        attacker, defender = self._rcv, self._srv
        atk_strat, def_strat = self._rcv_strat, self._srv_strat

        # ── ATTACK LOOP ───────────────────────────────────────────────────────
        while exchange < MAX_EXCHANGES:
            exchange += 1
            quick_lanes: List[int] = []  # Track quick set lanes for this exchange

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

            # Determine if this is a quick set (1-3) and which lanes are quick.
            # Quickset lanes force a single blind blocker with no stacking —
            # enforced in _phase_block_commit.
            is_quick_set = set_card.value <= 3
            if is_quick_set:
                for lane in template.front_lanes:
                    if lane in attack_cards:
                        for ac in attack_cards[lane]:
                            if ac.position == "front":
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

            # LANE CHOICE — strategies pick from the committed attack lanes directly
            # (no card-matching elimination pass; that mechanic was retired).
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

            # Resolve role from chosen lane before any role-gated ability checks.
            attacker_role = LANE_TO_ROLE.get(attack_lane)

            # SLIDE_LANES ability - can shift to adjacent lane with lower block
            if attacker.ability_engine and attacker_role:
                cards_on_current = attack_cards[attack_lane]
                front = [ac for ac in cards_on_current if ac.position == "front"]
                back = [ac for ac in cards_on_current if ac.position == "back"]
                current_card = front[0].card if front else back[0].card

                if attacker.ability_engine.slide_lanes(attacker_role, current_card.value):
                    adjacent_lanes = []
                    if attack_lane > 1 and (attack_lane - 1) in attack_cards:
                        adjacent_lanes.append(attack_lane - 1)
                    if attack_lane < 3 and (attack_lane + 1) in attack_cards:
                        adjacent_lanes.append(attack_lane + 1)

                    if adjacent_lanes:
                        current_block = block_layout.get(attack_lane, 0)
                        best_adjacent = min(adjacent_lanes, key=lambda l: block_layout.get(l, 0))
                        if block_layout.get(best_adjacent, 0) < current_block:
                            attack_lane = best_adjacent
                            attacker_role = LANE_TO_ROLE.get(attack_lane)

            # Reveal any blind-drawn cards on the committed lane
            for _bac in attack_cards.get(attack_lane, []):
                if _bac.blind:
                    self._narrate(
                        f"  Reveal:  lane {attack_lane} blind draw"
                        f"  →  card {_bac.card.value} ({_bac.position})"
                    )

            # Declared resolve order for the chosen lane: front-row card first,
            # then back-row card (a "combo" when both are present).
            cards_on_lane = attack_cards[attack_lane]
            front_attacks = [ac for ac in cards_on_lane if ac.position == "front"]
            back_attacks = [ac for ac in cards_on_lane if ac.position == "back"]
            resolve_order = front_attacks + back_attacks
            if len(resolve_order) == 2:
                self._narrate(
                    f"  Combo:   lane {attack_lane} — resolve order "
                    f"{resolve_order[0].card.value} ({resolve_order[0].position}) then "
                    f"{resolve_order[1].card.value} ({resolve_order[1].position})"
                )

            # Apply attacker abilities that are decided once per exchange
            # (attack bonus, pierce block, quick-set MB bonus, hold card).
            mb_qs_bonus = (
                attacker.ability_engine.consume_mb_attack_bonus()
                if attacker.ability_engine else 0
            )
            if attacker.ability_engine:
                attacker.ability_engine.set_hand_size(len(attacker.hand))

            # Hold card check: retain the first card in resolve order if the
            # ability fires (mirrors the pre-combo single-card behavior).
            hold_candidate = resolve_order[0].card
            all_attack_cards = [
                ac.card for cards_list in attack_cards.values() for ac in cards_list
            ]
            cards_to_discard = all_attack_cards
            if (attacker.ability_engine and attacker_role
                    and attacker.held_card is None
                    and attacker.ability_engine.hold_card_check(
                        attacker_role, hold_candidate.value)):
                attacker.held_card = hold_candidate
                cards_to_discard = [c for c in cards_to_discard if c is not hold_candidate]

            # Committed attack cards discarded; attacker refills (ball went over net)
            attacker.discard_many(cards_to_discard)
            attacker.refill_hand()

            # Resolve the lane's card(s) in declared order. A genuinely stuffed
            # non-final card removes the lane's single highest blocker card
            # before the next card resolves against what remains.
            lane_block_cards = list(block_cards.get(attack_lane, []))
            for idx, ac in enumerate(resolve_order):
                is_last_card = idx == len(resolve_order) - 1
                outcome = self._resolve_card(
                    attacker, defender, atk_strat, def_strat, exchange,
                    attack_lane, attacker_role, ac, lane_block_cards,
                    mb_qs_bonus, is_last_card,
                )
                if outcome.result is not None:
                    return outcome.result
                if outcome.stuffed:
                    if lane_block_cards:
                        highest = max(lane_block_cards, key=lambda c: c.value)
                        lane_block_cards.remove(highest)
                        self._narrate(
                            f"  Combo:   lane {attack_lane} card {idx + 1} stuffed"
                            f"  →  highest blocker ({highest.value}) removed,"
                            f" second card resolves"
                        )
                    continue
                attacker, defender = outcome.attacker, outcome.defender
                atk_strat, def_strat = outcome.atk_strat, outcome.def_strat
                break

        # Safety: rally hit the exchange cap
        return RallyResult(
            winner_name=self._rng.choice([self._srv.name, self._rcv.name]),
            reason="Rally limit reached",
            rally_length=exchange,
        )

    # ── Single-card resolution ──────────────────────────────────────────────

    def _resolve_card(
        self,
        attacker: Team,
        defender: Team,
        atk_strat: BaseStrategy,
        def_strat: BaseStrategy,
        exchange: int,
        attack_lane: int,
        attacker_role: Optional[PlayerRole],
        ac: AttackCard,
        lane_block_cards: List[Card],
        mb_qs_bonus: int,
        is_last_card: bool,
    ) -> _CardOutcome:
        """
        Resolve one attack card against the lane's current block state.

        `lane_block_cards` reflects the lane's live block: for the second
        card of a combo whose first card was genuinely stuffed, the caller
        has already removed the lane's single highest blocker card.
        """
        attack_card = ac.card
        is_back_row_attack = ac.is_back_row()
        lane_block_value = sum(c.value for c in lane_block_cards)
        lane_block_max = max((c.value for c in lane_block_cards), default=0)
        is_double_blocked = len(lane_block_cards) >= 2

        effective_attack = attack_card.value
        effective_block = lane_block_value

        if attacker.ability_engine and attacker_role:
            effective_attack += attacker.ability_engine.attack_value_bonus(
                attacker_role, attack_card.value
            )
            effective_attack += attacker.ability_engine.over_block_bonus(
                attacker_role, attack_card.value, is_double_blocked
            )

            # FORCE_HIGH_BLOCK - filter out low-value blocks
            force_threshold = attacker.ability_engine.force_high_block_threshold(
                attacker_role, attack_card.value
            )
            if force_threshold > 0:
                high_blocks = [c.value for c in lane_block_cards if c.value > force_threshold]
                effective_block = sum(high_blocks)
                lane_block_max = max(high_blocks) if high_blocks else 0

            # Pierce block checks (front or back-row)
            if attacker.ability_engine.pierce_block(attacker_role, attack_card.value):
                effective_block = 0
            elif is_back_row_attack and attacker.ability_engine.back_row_pierce(
                attacker_role, attack_card.value
            ):
                effective_block = 0
            # Passive ability: Back Court Threat (Medium team)
            elif is_back_row_attack and attacker.passive_ability == "Back Court Threat":
                if len(lane_block_cards) > 1:
                    sorted_blocks = sorted(c.value for c in lane_block_cards)
                    effective_block = sum(sorted_blocks[1:])
                    self._narrate(
                        f"  Passive: Back Court Threat ignores first blocker "
                        f"({sorted_blocks[0]}), effective block = {effective_block}"
                    )
                else:
                    effective_block = 0
                    if lane_block_cards:
                        self._narrate(
                            f"  Passive: Back Court Threat ignores single blocker "
                            f"({lane_block_cards[0].value})"
                        )
            elif attacker.ability_engine.min_blocker_only(attacker_role, attack_card.value):
                effective_block = min((c.value for c in lane_block_cards), default=0)
            elif attacker.ability_engine.single_block_only(attacker_role, attack_card.value):
                effective_block = lane_block_max

        # MB quick-set bonus only applies when the resolving card is the
        # front-row MB attacker in lane 2.
        if attack_lane == 2 and ac.position == "front":
            effective_attack += mb_qs_bonus

        _atk_eff = (f"{attack_card.value}→{effective_attack}"
                    if effective_attack != attack_card.value else str(effective_attack))
        _blk_eff = (f"{lane_block_value}→{effective_block}"
                    if effective_block != lane_block_value else str(effective_block))
        self._narrate(
            f"  Resolve: {attacker.name} lane {attack_lane} {_LANE_NAMES.get(attack_lane, '')}"
            f"  atk {_atk_eff}  vs  blk {_blk_eff}"
        )

        tip_threshold = TIP_THRESHOLD
        if attacker.ability_engine:
            tip_threshold += attacker.ability_engine.consume_tip_threshold_delta()

        # Wipe off the block: card=1 ability, block present → instant point
        if (
            lane_block_value > 0
            and attacker.ability_engine and attacker_role
            and attacker.ability_engine.wipe_block(attacker_role, attack_card.value)
        ):
            return _CardOutcome(result=RallyResult(
                winner_name=attacker.name,
                reason=f"Wipe off the block (card={attack_card.value}, block={lane_block_value})",
                rally_length=exchange,
            ))

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
            return self._resolve_tip(
                attacker, defender, atk_strat, def_strat, exchange,
                attack_lane, attacker_role, attack_card, effective_attack,
                lane_block_cards, is_last_card,
            )
        elif shot == "roll":
            return self._resolve_roll_shot(
                attacker, defender, atk_strat, def_strat, exchange,
                attack_lane, attack_card, effective_attack,
            )
        elif shot == "heavy_spin":
            return self._resolve_heavy_spin(
                attacker, defender, atk_strat, def_strat, exchange,
                attack_lane, attacker_role, attack_card, effective_attack,
            )
        else:
            return self._resolve_hit(
                attacker, defender, atk_strat, def_strat, exchange,
                attack_lane, attacker_role, attack_card, effective_attack,
                effective_block, shot, lane_block_cards, is_back_row_attack, is_last_card,
            )

    def _resolve_tip(
        self, attacker, defender, atk_strat, def_strat, exchange,
        attack_lane, attacker_role, attack_card, effective_attack,
        lane_block_cards: List[Card], is_last_card: bool,
    ) -> _CardOutcome:
        """
        Tip (any card <=5, front row only): checked against the single lowest
        blocker card in the lane (not the stack total). Tip beats it if that
        card is lower; tie -> deflection. Digging a tip uses the same rule as
        digging a hit (equal or higher). No chase after a failed tip dig.
        """
        effective_tip = effective_attack
        if attacker.ability_engine and attacker_role:
            effective_tip += attacker.ability_engine.tip_value_bonus(attacker_role)

        lowest_blocker = min((c.value for c in lane_block_cards), default=None)

        if lowest_blocker is not None and effective_tip < lowest_blocker:
            self._narrate(
                f"  Tip:     {effective_tip} < lowest blocker {lowest_blocker}  →  STUFFED"
            )
            if not is_last_card:
                return _CardOutcome(stuffed=True)
            return _CardOutcome(result=RallyResult(
                winner_name=defender.name,
                reason=f"Tip stuffed (tip={effective_tip}, blocker={lowest_blocker})",
                rally_length=exchange,
            ))

        if lowest_blocker is not None and effective_tip == lowest_blocker:
            self._narrate(f"  Tip:     {effective_tip} == lowest blocker {lowest_blocker}  →  DEFLECT")
            return self._resolve_own_side_deflect(
                attacker, defender, atk_strat, def_strat, exchange, effective_tip
            )

        # Tip beats the block (or the lane is empty): defender digs,
        # equal-or-higher, no chase on failure.
        dig_card = self._phase_dig(defender, def_strat, effective_tip, "tip")
        defender_role = get_dig_defender_role(attack_lane, attack_card.value)
        effective_tip_dig = dig_card.value
        if defender.ability_engine:
            effective_tip_dig += defender.ability_engine.tip_dig_threshold(defender_role)
        if effective_tip_dig >= effective_tip:
            self._narrate(
                f"  Dig:     {defender.name} card {dig_card.value}"
                + (f" (eff {effective_tip_dig})" if effective_tip_dig != dig_card.value else "")
                + f"  ≥ tip {effective_tip}  →  DUG"
            )
            # Tips are too fast for adjacent coverage — setter dig always breaks play
            self._broken_play = (defender_role == PlayerRole.SETTER)
            return _CardOutcome(attacker=defender, defender=attacker, atk_strat=def_strat, def_strat=atk_strat)

        self._narrate(
            f"  Dig:     {defender.name} card {dig_card.value}"
            + (f" (eff {effective_tip_dig})" if effective_tip_dig != dig_card.value else "")
            + f"  < tip {effective_tip}  →  NOT DUG, no chase"
        )
        return _CardOutcome(result=RallyResult(
            winner_name=attacker.name,
            reason=f"Tip not dug, no chase (tip={effective_tip}, dig={dig_card.value})",
            rally_length=exchange,
        ))

    def _resolve_own_side_deflect(
        self, attacker, defender, atk_strat, def_strat, exchange, tied_value: int,
    ) -> _CardOutcome:
        """
        Exact-tie deflection: the ball falls to the attacker's own side, and
        that team digs it — equal-or-higher required, chase allowed on
        failure. A successful direct dig keeps the same team attacking (the
        ball never crossed the net). A chase-recovered "broken dig" sends a
        mandatory free ball to the opponent, who becomes the new attacker.
        """
        deflect_penalty = (
            defender.ability_engine.deflect_dig_threshold() if defender.ability_engine else 0
        )
        deflect_target = max(1, tied_value - deflect_penalty)

        dig_card = self._phase_dig(attacker, atk_strat, deflect_target, "tip")
        if dig_card.value >= deflect_target:
            self._narrate(
                f"  Deflect: attacker side, {attacker.name} card {dig_card.value}"
                f"  ≥ {deflect_target}  →  DUG"
            )
            return _CardOutcome(attacker=attacker, defender=defender, atk_strat=atk_strat, def_strat=def_strat)

        self._narrate(
            f"  Deflect: attacker side, {attacker.name} card {dig_card.value}"
            f"  < {deflect_target}  →  NOT DUG"
        )
        dig_fail_bonus = (
            attacker.ability_engine.dig_failure_chase_bonus() if attacker.ability_engine else 0
        )
        chase = self._phase_chase(attacker, atk_strat, dig_card.value, deflect_target, extra_bonus=dig_fail_bonus)
        if chase.outcome == ChaseOutcome.FAILED:
            return _CardOutcome(result=RallyResult(
                winner_name=defender.name,
                reason=f"Deflect not dug, chase failed (target={deflect_target}, dig={dig_card.value})",
                rally_length=exchange,
            ))
        # Broken dig recovered: mandatory free ball crosses to the opponent.
        attacker.refill_hand()
        self._narrate(f"  Chase:   broken dig recovered — mandatory free ball to {defender.name}")
        return _CardOutcome(attacker=defender, defender=attacker, atk_strat=def_strat, def_strat=atk_strat)

    def _resolve_hit(
        self, attacker, defender, atk_strat, def_strat, exchange,
        attack_lane, attacker_role, attack_card, effective_attack, effective_block,
        shot: str, lane_block_cards: List[Card], is_back_row_attack: bool, is_last_card: bool,
    ) -> _CardOutcome:
        outcome = resolve_attack(effective_attack, effective_block)
        self._narrate(f"  Outcome: {outcome.name}  (atk {effective_attack} vs blk {effective_block})")

        if outcome == AttackOutcomeType.STUFFED:
            if not is_last_card:
                return _CardOutcome(stuffed=True)
            return _CardOutcome(result=RallyResult(
                winner_name=defender.name,
                reason=f"Stuffed (attack={effective_attack}, block={effective_block})",
                rally_length=exchange,
            ))

        elif outcome == AttackOutcomeType.DEFLECT:
            if shot == "seam":
                # Seam shot: deflect redirects onto defending team's side instead
                return _CardOutcome(result=RallyResult(
                    winner_name=attacker.name,
                    reason=f"Seam shot deflect (attack={effective_attack}, block={effective_block})",
                    rally_length=exchange,
                ))
            return self._resolve_own_side_deflect(
                attacker, defender, atk_strat, def_strat, exchange, effective_attack
            )

        else:  # KILL
            dig_target = effective_attack
            if attacker.ability_engine and attacker_role:
                dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
            defender_role = get_dig_defender_role(attack_lane, attack_card.value)
            cover_threshold = _cover_threshold(defender)
            cover_attempted = False
            if defender_role == PlayerRole.SETTER and cover_threshold > 0:
                if def_strat.cover_draws_from_deck():
                    if defender.deck.draw_pile_size > 0:
                        cover_card = defender.draw_for_action()
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

            effective_dig = dig_card.value
            if defender.ability_engine:
                effective_dig += defender.ability_engine.defender_dig_threshold(defender_role)

            if effective_dig >= dig_target:
                self._narrate(
                    f"  Dig:     {defender.name} card {dig_card.value}"
                    + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                    + f"  ≥ {dig_target}  →  DUG"
                )
                if defender.ability_engine:
                    defender.ability_engine.record_dig_success(defender_role, "normal")
                    if defender.ability_engine.deck_swap_opponent_on_dig("normal"):
                        if attacker.hand:
                            highest = max(attacker.hand, key=lambda c: c.value)
                            attacker.hand.remove(highest)
                            attacker.deck.discard(highest)
                            if attacker.deck.draw_pile_size > 0:
                                new_card = attacker.draw_for_action()
                                attacker.hand.append(new_card)
                if defender_role == PlayerRole.SETTER:
                    if cover_attempted and dig_card.value >= cover_threshold:
                        self._narrate(
                            f"  Cover:   adjacent player reached (card {dig_card.value}) — no broken play"
                        )
                        self._broken_play = False
                    elif defender.passive_ability == "Safe Setter":
                        self._narrate("  Passive: Safe Setter prevents broken play")
                        self._broken_play = False
                    else:
                        self._broken_play = True
                else:
                    self._broken_play = False
                return _CardOutcome(attacker=defender, defender=attacker, atk_strat=def_strat, def_strat=atk_strat)

            self._narrate(
                f"  Dig:     {defender.name} card {dig_card.value}"
                + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                + f"  < {dig_target}  →  NOT DUG"
            )
            if not lane_block_cards:
                return _CardOutcome(result=RallyResult(
                    winner_name=attacker.name,
                    reason=f"Unblocked kill, no chase (attack={effective_attack}, dig={dig_card.value})",
                    rally_length=exchange,
                ))
            if (
                attacker.ability_engine and attacker_role
                and attacker.ability_engine.no_chase(attacker_role, attack_card.value)
            ):
                return _CardOutcome(result=RallyResult(
                    winner_name=attacker.name,
                    reason=(
                        f"Kill, no chase (attack={effective_attack} > "
                        f"block={effective_block}, dig={dig_card.value})"
                    ),
                    rally_length=exchange,
                ))
            dig_fail_bonus = (
                defender.ability_engine.dig_failure_chase_bonus() if defender.ability_engine else 0
            )
            chase = self._phase_chase(defender, def_strat, dig_card.value, dig_target, extra_bonus=dig_fail_bonus)
            if chase.outcome == ChaseOutcome.FAILED:
                return _CardOutcome(result=RallyResult(
                    winner_name=attacker.name,
                    reason=(
                        f"Kill (attack={effective_attack} > block={effective_block}), "
                        f"chase failed (dig={dig_card.value})"
                    ),
                    rally_length=exchange,
                ))
            # Broken dig recovered: mandatory free ball back to the attacker.
            defender.refill_hand()
            self._narrate(f"  Chase:   broken dig recovered — mandatory free ball to {attacker.name}")
            return _CardOutcome(attacker=attacker, defender=defender, atk_strat=atk_strat, def_strat=def_strat)

    def _resolve_roll_shot(
        self, attacker, defender, atk_strat, def_strat, exchange,
        attack_lane, attack_card, effective_attack,
    ) -> _CardOutcome:
        """Dormant ability-gated shot: block ignored, dug like a tip, normal chase on failure."""
        dig_card = self._phase_dig(defender, def_strat, effective_attack, "tip")
        defender_role = get_dig_defender_role(attack_lane, attack_card.value)
        effective_dig = dig_card.value
        if defender.ability_engine:
            effective_dig += defender.ability_engine.tip_dig_threshold(defender_role)
        if effective_dig <= effective_attack:
            self._narrate(
                f"  Dig:     {defender.name} card {dig_card.value}"
                + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                + f"  ≤ roll {effective_attack}  →  DUG"
            )
            self._broken_play = (defender_role == PlayerRole.SETTER)
            return _CardOutcome(attacker=defender, defender=attacker, atk_strat=def_strat, def_strat=atk_strat)

        self._narrate(
            f"  Dig:     {defender.name} card {dig_card.value}"
            + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
            + f"  > roll {effective_attack}  →  NOT DUG"
        )
        dig_fail_bonus = (
            defender.ability_engine.dig_failure_chase_bonus() if defender.ability_engine else 0
        )
        chase = self._phase_chase(defender, def_strat, dig_card.value, effective_attack, extra_bonus=dig_fail_bonus)
        if chase.outcome == ChaseOutcome.FAILED:
            return _CardOutcome(result=RallyResult(
                winner_name=attacker.name,
                reason=f"Roll shot kill, chase failed (roll={effective_attack}, dig={dig_card.value})",
                rally_length=exchange,
            ))
        defender.refill_hand()
        self._narrate(f"  Chase:   broken dig recovered — mandatory free ball to {attacker.name}")
        return _CardOutcome(attacker=attacker, defender=defender, atk_strat=atk_strat, def_strat=def_strat)

    def _resolve_heavy_spin(
        self, attacker, defender, atk_strat, def_strat, exchange,
        attack_lane, attacker_role, attack_card, effective_attack,
    ) -> _CardOutcome:
        """Dormant ability-gated shot: block ignored, failed dig is an instant point, no chase."""
        dig_target = effective_attack
        if attacker.ability_engine and attacker_role:
            dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
        defender_role = get_dig_defender_role(attack_lane, attack_card.value)
        cover_threshold = _cover_threshold(defender)
        cover_attempted = False
        if defender_role == PlayerRole.SETTER and cover_threshold > 0:
            if def_strat.cover_draws_from_deck():
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

        effective_dig = dig_card.value
        if defender.ability_engine:
            effective_dig += defender.ability_engine.defender_dig_threshold(defender_role)

        if effective_dig >= dig_target:
            self._narrate(
                f"  Dig:     {defender.name} card {dig_card.value}"
                + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
                + f"  ≥ {dig_target}  →  HEAVY SPIN DUG"
            )
            if defender.ability_engine:
                defender.ability_engine.record_dig_success(defender_role, "normal")
                if defender.ability_engine.deck_swap_opponent_on_dig("normal"):
                    if attacker.hand:
                        highest = max(attacker.hand, key=lambda c: c.value)
                        attacker.hand.remove(highest)
                        attacker.deck.discard(highest)
                        if attacker.deck.draw_pile_size > 0:
                            new_card = attacker.deck.draw()
                            attacker.hand.append(new_card)
            if defender_role == PlayerRole.SETTER:
                if cover_attempted and dig_card.value >= cover_threshold:
                    self._narrate(
                        f"  Cover:   adjacent player reached (card {dig_card.value}) — no broken play"
                    )
                    self._broken_play = False
                else:
                    self._broken_play = True
            else:
                self._broken_play = False
            return _CardOutcome(attacker=defender, defender=attacker, atk_strat=def_strat, def_strat=atk_strat)

        self._narrate(
            f"  Dig:     {defender.name} card {dig_card.value}"
            + (f" (eff {effective_dig})" if effective_dig != dig_card.value else "")
            + f"  < {dig_target}  →  NOT DUG (no chase)"
        )
        return _CardOutcome(result=RallyResult(
            winner_name=attacker.name,
            reason=f"Heavy spin not dug, no chase (attack={effective_attack}, dig={dig_card.value})",
            rally_length=exchange,
        ))

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
        Chase: keep adding cards (up to 2 attempts) until the running total
        reaches the target, or fail. A SUCCESS just reports that the target
        was reached — what happens next (normal attack resumes, or a
        mandatory broken-dig free ball) is decided by the caller based on
        context. FAILED means the opposing team wins the point.
        """
        running_total += extra_bonus + (
            team.ability_engine.chase_bonus() if team.ability_engine else 0
        )
        self._narrate(
            f"\n  Chase:   {team.name}  need {target_value}  starting at {running_total}"
        )
        for attempt in (1, 2):
            if team.hand:
                card = strat.choose_chase_card(team.hand, running_total, target_value)
                team.play_card(card)
            else:
                card = team.blind_draw()
            running_total += card.value
            self._narrate(f"  Chase {attempt}: card {card.value}  →  total {running_total} / {target_value}")

            if running_total >= target_value:
                self._narrate("  Chase:   SUCCEEDED")
                return ChaseResult(outcome=ChaseOutcome.SUCCESS)

        self._narrate(f"  Chase:   FAILED  (total {running_total} < {target_value})")
        return ChaseResult(outcome=ChaseOutcome.FAILED)

    def _phase_serve(self) -> Tuple[Card, GridPlayer]:
        eligible = self._rcv.eligible_receivers()
        if self._srv.hand:
            card, target = self._srv_strat.choose_serve(self._srv.hand, eligible)
            self._srv.play_card(card)
        else:
            card = self._srv.draw_for_action()
            self._srv.deck.discard(card)
            target = self._rng.choice(eligible)
        self._srv.refill_hand()
        return card, target

    def _phase_receive(self, serve_value: int) -> Card:
        if self._rcv.hand:
            card = self._rcv_strat.choose_receive_card(self._rcv.hand, serve_value)
            self._rcv.play_card(card)
        else:
            card = self._rcv.draw_for_action()
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
            card = team.draw_for_action()
            team.deck.discard(card)
        # No refill here: hand replenishes only when the ball crosses the net.

        # Apply set_value_delta (dig-success ability)
        effective_value = card.value + set_value_delta

        # Apply Setter's passive on_set bonus ONLY if not broken play
        if not self._broken_play and team.ability_engine:
            set_bonus = team.ability_engine.on_set_bonus(card.value)
            effective_value += set_bonus

        effective_value = min(10, max(1, effective_value))

        # Select template based on broken play status.
        # Team-specific template maps are injected from CSV runtime config.
        template_map = team.broken_play_templates if self._broken_play else team.setter_templates
        template = template_map[effective_value]

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
                        new_card = team.draw_for_action()
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
                blind_card = team.draw_for_action()
                attack_cards.setdefault(lane, []).append(
                    AttackCard(card=blind_card, position=position, blind=True)
                )

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
                        card = team.draw_for_action()
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
                card = team.draw_for_action()
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
                                card = team.draw_for_action()
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
            card = team.draw_for_action()
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
