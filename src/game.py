from __future__ import annotations

import random
from typing import Dict, List, Tuple

from .cards import Card
from .players import Team, GridPlayer, SET_ELIGIBLE_LANES, LANE_TO_ROLE, PlayerRole
from .game_state import AttackOutcomeType, ChaseOutcome, ChaseResult, RallyResult, GameResult
from .strategies import BaseStrategy

POINTS_TO_WIN = 15
MAX_EXCHANGES = 200  # safety cap to prevent infinite rallies


def resolve_attack(attack_value: int, block_value: int) -> AttackOutcomeType:
    """Determine attack outcome purely from numeric values."""
    if attack_value > block_value:
        return AttackOutcomeType.KILL
    diff = block_value - attack_value
    if diff <= 2:
        return AttackOutcomeType.DEFLECT
    return AttackOutcomeType.STUFFED


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
    ) -> None:
        self._srv = serving_team
        self._rcv = receiving_team
        self._srv_strat = serving_strategy
        self._rcv_strat = receiving_strategy
        self._rng = rng

    def play(self) -> RallyResult:
        exchange = 0
        next_attack_is_armed = False
        armed_lane: int = 0

        # ── SERVE ────────────────────────────────────────────────────────────
        serve_card, _target = self._phase_serve()
        # Effective serve value (Setter serve bonus, if any)
        serve_value = serve_card.value + (
            self._srv.ability_engine.serve_value_bonus()
            if self._srv.ability_engine else 0
        )

        # ── RECEIVE ──────────────────────────────────────────────────────────
        receive_card = self._phase_receive(serve_value)

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

            # SET + HIT  — or ARMED ATTACK (no set, single OH/OPP lane)
            is_armed = next_attack_is_armed
            if is_armed:
                next_attack_is_armed = False
                attack_cards = self._phase_armed_attack(attacker, atk_strat, armed_lane)
            else:
                # Consume any pending set delta from a previous dig success
                set_delta = (
                    attacker.ability_engine.consume_set_delta()
                    if attacker.ability_engine else 0
                )
                set_card, eligible_lanes = self._phase_set(attacker, atk_strat, set_delta)
                attack_cards = self._phase_hit(attacker, atk_strat, eligible_lanes)
                # Fire on_quick_set if MB lane is among the eligible lanes
                if 2 in eligible_lanes and attacker.ability_engine:
                    attacker.ability_engine.activate_quick_set()
                # Notify engine of set card value (for tip_threshold_delta)
                if attacker.ability_engine:
                    attacker.ability_engine.activate_tip_threshold(set_card.value)

            # BLOCK COMMIT  (defender places cards blind to lane choice)
            block_layout, block_max = self._phase_block_commit(defender, def_strat, list(attack_cards.keys()))

            # LANE CHOICE  (skipped for armed attack — lane already fixed)
            if is_armed:
                attack_lane = armed_lane
            else:
                attack_lane = atk_strat.choose_attack_lane(attack_cards, block_layout)
            attack_card = attack_cards[attack_lane]
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
                if attacker.ability_engine.pierce_block(attacker_role, attack_card.value):
                    effective_block = 0
                elif attacker.ability_engine.single_block_only(attacker_role, attack_card.value):
                    # Only the highest single blocker card in this lane counts
                    effective_block = block_max.get(attack_lane, 0)
            # MB quick-set bonus only applies if MB is actually the attacker
            if attack_lane == 2:
                effective_attack += mb_qs_bonus

            # Hold card check: retain chosen attack card if ability fires
            cards_to_discard = list(attack_cards.values())
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
                    effective_block = 0  # roll shot bypasses the block
                elif attacker.ability_engine.seam_shot(attacker_role, attack_card.value):
                    shot = "seam"
            if shot == "hit" and effective_attack <= tip_threshold:
                shot = atk_strat.choose_tip_or_hit(effective_attack, effective_block)

            if shot == "tip":
                # ── TIP ──────────────────────────────────────────────────────
                # Attacker tip bonus (on_tip trigger)
                effective_tip = effective_attack
                if attacker.ability_engine and attacker_role:
                    effective_tip += attacker.ability_engine.tip_value_bonus(attacker_role)
                dig_card = self._phase_dig(defender, def_strat, effective_tip, "tip")
                # Defender Libero tip-dig threshold bonus (positive = easier to dig)
                effective_tip_dig = dig_card.value
                if defender.ability_engine:
                    effective_tip_dig += defender.ability_engine.tip_dig_threshold(
                        PlayerRole.LIBERO
                    )
                if effective_tip_dig <= effective_tip:
                    attacker, defender = defender, attacker
                    atk_strat, def_strat = def_strat, atk_strat
                    continue
                else:
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
                # A failed dig is an instant point — no chase.
                dig_target = effective_attack
                if attacker.ability_engine and attacker_role:
                    dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
                dig_card = self._phase_dig(defender, def_strat, dig_target, "normal")
                effective_dig = dig_card.value
                if defender.ability_engine:
                    effective_dig += defender.ability_engine.defender_dig_threshold(
                        PlayerRole.LIBERO
                    )
                    effective_dig += defender.ability_engine.defender_dig_threshold(
                        PlayerRole.DS
                    )
                if effective_dig >= dig_target:
                    if defender.ability_engine:
                        defender.ability_engine.record_dig_success(PlayerRole.LIBERO, "normal")
                    attacker, defender = defender, attacker
                    atk_strat, def_strat = def_strat, atk_strat
                    continue
                else:
                    return RallyResult(
                        winner_name=attacker.name,
                        reason=(
                            f"Roll shot not dug, no chase "
                            f"(attack={effective_attack}, dig={dig_card.value})"
                        ),
                        rally_length=exchange,
                    )

            else:
                # ── HIT / SEAM ────────────────────────────────────────────────
                outcome = resolve_attack(effective_attack, effective_block)

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
                    # Normal deflect: attacker must dig the redirected ball
                    deflect_penalty = (
                        defender.ability_engine.deflect_dig_threshold()
                        if defender.ability_engine else 0
                    )
                    # Deflect value = highest single block card in the lane (not full attack)
                    deflect_target = max(1, block_max.get(attack_lane, 0) - deflect_penalty)
                    dig_card = self._phase_dig(
                        attacker, atk_strat, deflect_target, "tip"
                    )
                    if dig_card.value <= deflect_target:
                        continue
                    else:
                        return RallyResult(
                            winner_name=defender.name,
                            reason=(
                                f"Deflect not dug "
                                f"(deflect_val={deflect_target}, dig={dig_card.value})"
                            ),
                            rally_length=exchange,
                        )

                else:  # KILL
                    # dig_threshold abilities shift the attack level the dig must beat
                    dig_target = effective_attack
                    if attacker.ability_engine and attacker_role:
                        dig_target += attacker.ability_engine.attack_dig_threshold(attacker_role)
                    dig_card = self._phase_dig(
                        defender, def_strat, dig_target, "normal"
                    )
                    # Defender Libero/DS dig-threshold bonus adds to effective dig value
                    effective_dig = dig_card.value
                    if defender.ability_engine:
                        effective_dig += defender.ability_engine.defender_dig_threshold(
                            PlayerRole.LIBERO
                        )
                        effective_dig += defender.ability_engine.defender_dig_threshold(
                            PlayerRole.DS
                        )
                    if effective_dig >= dig_target:
                        # Successful dig → record ability trigger, then transition
                        if defender.ability_engine:
                            defender.ability_engine.record_dig_success(
                                PlayerRole.LIBERO, "normal"
                            )
                        attacker, defender = defender, attacker
                        atk_strat, def_strat = def_strat, atk_strat
                        continue
                    else:
                        # Check no_chase before starting chase
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
        # Attempt 1
        if team.hand:
            card1 = strat.choose_chase_card(team.hand, running_total, target_value)
            team.play_card(card1)
        else:
            card1 = team.blind_draw()
        running_total += card1.value

        if running_total >= target_value:
            lane = strat.choose_armed_attack_lane(team.hand)
            return ChaseResult(outcome=ChaseOutcome.ARMED_ATTACK, armed_lane=lane)

        # Attempt 2
        if team.hand:
            card2 = strat.choose_chase_card(team.hand, running_total, target_value)
            team.play_card(card2)
        else:
            card2 = team.blind_draw()
        running_total += card2.value

        if running_total >= target_value:
            return ChaseResult(outcome=ChaseOutcome.FREE_BALL)

        return ChaseResult(outcome=ChaseOutcome.FAILED)

    def _phase_armed_attack(
        self, team: Team, strat: BaseStrategy, lane: int
    ) -> Dict[int, Card]:
        """
        Armed attack after a successful first chase.
        No set card is played; attack is limited to a single OH (lane 1) or
        OPP (lane 3) lane, leaving the attacker exposed to a double block.
        Returns {lane: card} with the card committed (not yet discarded).
        Refill happens in the main loop after discard_many (ball crossed the net).
        """
        if team.hand:
            attack_cards = strat.choose_hit_cards(team.hand, [lane])
            team.commit_cards(list(attack_cards.values()))
        else:
            card = team.deck.draw()
            attack_cards = {lane: card}
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
    ) -> Tuple[Card, List[int]]:
        if team.hand:
            card = strat.choose_set_card(team.hand)
            team.play_card(card)
        else:
            card = team.deck.draw()
            team.deck.discard(card)
        # No refill here: hand replenishes only when the ball crosses the net.
        # Apply set_value_delta (dig-success ability) + Setter's passive on_set bonus.
        set_bonus = team.ability_engine.on_set_bonus(card.value) if team.ability_engine else 0
        effective_value = min(10, max(1, card.value + set_value_delta + set_bonus))
        eligible = SET_ELIGIBLE_LANES[effective_value]
        return card, eligible

    def _phase_hit(
        self, team: Team, strat: BaseStrategy, eligible_lanes: List[int]
    ) -> Dict[int, Card]:
        if team.hand:
            attack_cards = strat.choose_hit_cards(team.hand, eligible_lanes)
            team.commit_cards(list(attack_cards.values()))
        else:
            # No hand: blindly draw one card and place it on the first eligible lane
            card = team.deck.draw()
            attack_cards = {eligible_lanes[0]: card}
            # card is already outside the hand, no commit needed
        # No refill here: attacker refills in the main loop after discard_many
        # (when the ball crosses the net on the hit).
        return attack_cards

    def _phase_block_commit(
        self, team: Team, strat: BaseStrategy, attack_lanes: List[int]
    ) -> Tuple[Dict[int, int], Dict[int, int]]:
        """
        Returns (block_layout, block_max) where:
          block_layout : {lane: total_block_value}  — unblocked lanes absent (value=0)
          block_max    : {lane: highest_single_card} — max individual card per lane
        """
        if team.hand:
            placement = strat.choose_block_cards(team.hand, attack_lanes)
            all_block_cards = [c for cards in placement.values() for c in cards]
            team.commit_cards(all_block_cards)
        else:
            placement = {}
            all_block_cards = []

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
            # MB adjacent block bonus: adds to lanes 1 and 3 when MB is blocking
            adj = team.ability_engine.adjacent_block_bonus(mb_is_blocking=2 in block_layout)
            if adj:
                for adj_lane in (1, 3):
                    block_layout[adj_lane] = block_layout.get(adj_lane, 0) + adj

        return block_layout, block_max

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
