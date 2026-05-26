"""
Player card ability system.

Each player card defines one or more abilities.  Abilities fire on a trigger
event (e.g. on_attack, on_block) and optionally only when a condition is met
(e.g. attack_card_value == 5).  They then apply a numeric effect to the game.

── Valid triggers ─────────────────────────────────────────────────────────────
  on_serve              Setter serves the ball
  on_receive            Libero / back-row receives
  on_set                Setter sets (passive bonus, fires every set)
  on_quick_set          Setter sets to a lane that includes MB
  on_attack             Front-row player attacks (OH=lane1, MB=lane2, OPP=lane3)
  on_block              Front-row player blocks
  on_block_deflection   Block causes a deflection (affects dig difficulty)
  on_dig                Any kill-dig attempt — use for Libero/DS passive bonuses
  on_dig_success        Back-row player successfully digs a kill ball
  on_dig_failure        Dig attempt fails (before chase — e.g. recovery bonus)
  on_chase              Ball is being chased after a failed dig or receive
  on_tip                Attacker tips (card ≤ 3)

── Notes on trigger side ──────────────────────────────────────────────────────
  on_attack triggers fire on the ATTACKING player (OH, MB, OPP).
  on_block / on_block_deflection fire on the DEFENDING front row.
  on_dig / on_dig_success / on_dig_failure fire on the DEFENDING Libero or DS.

── Valid condition_field values ───────────────────────────────────────────────
  attack_card_value   The value of the attack card (int)
  dig_type            "normal" or "tip"
  (leave blank for no condition — ability always fires)

── Condition operators (numeric fields only) ───────────────────────────────────
  >=N  fires when field value >= N  (e.g. attack_card_value >=7)
  <=N  fires when field value <= N
  >N   fires when field value >  N
  <N   fires when field value <  N
  N    fires when field value == N  (plain equality, default)

── Valid effect values ─────────────────────────────────────────────────────────
  attack_value_bonus      Add effect_value to the attacker's card value
                          Also used with on_quick_set for MB quick-set bonus
  block_value_bonus       Add effect_value to this player's block contribution
  adjacent_block_bonus    Add effect_value to the two lanes adjacent to MB
                          (only fires if MB is actually blocking)
  pierce_block            Ignore block entirely; effect_value ignored (set to 1)
  set_value_delta         Add effect_value to set card's effective value (±)
                          Works with on_set (Setter passive) and on_dig_success
  serve_value_bonus       Add effect_value to serve card value
  chase_card_bonus        Add effect_value to chase running total
                          Works with on_chase and on_dig_failure
  deflect_dig_threshold   Positive = deflection harder for attacker to dig
  tip_value_bonus         Add effect_value to the attacker's tip card value
  tip_dig_threshold       Add effect_value to defender's dig value vs tips
  single_block_only       Attacker's lane is limited to one blocker's highest
                          card — double/triple blocks do NOT stack in that lane
  dig_threshold           Modifier for dig comparison only (not block or tip).
                          On ATTACKER (on_attack): positive = harder to dig.
                          On DEFENDER Libero/DS (on_dig): positive = easier to dig.
  wipe_block              on_attack only.  When the attack card matches the
                          condition AND a physical block exists in the lane,
                          the attacker wins the rally instantly (no dig attempt).
                          Typical condition: attack_card_value == 1.
  no_chase                on_attack only.  When a kill dig fails, skip the
                          chase entirely — instant attacker point.
  roll_shot               on_attack only.  Block is ignored for this attack AND
                          a failed dig cannot be chased (instant point).
                          Activates when attack_card_value matches the condition.
  seam_shot               on_attack only.  If the attack produces a DEFLECT
                          outcome, the ball redirects to the defending team's
                          side — attacker wins instantly.  KILL is a normal
                          kill sequence; STUFFED is a normal stuff.
                          Activates when attack_card_value matches the condition.
  tip_threshold_delta     on_set only.  When the setter plays a high card
                          (condition: set_card_value >= N), the tip threshold
                          for the upcoming attack is expanded by effect_value.
                          Base tip threshold is 3; delta is consumed once.
  over_block_bonus    on_attack only.  When the defending team committed 2+
                          block cards to the chosen lane, add effect_value to
                          the attacker's card value.  Rewards reading the block.
  hold_card           on_attack only.  After the attack resolves, the chosen
                          attack card is returned to the attacker's hand instead
                          of being discarded.  Only one card held at a time.
  hand_peek           on_attack only.  Before committing, attacker may look at
                          effect_value top cards of their deck.
                          (Future: requires strategy AI support.)
  exchange_card       on_attack only.  Before committing, attacker may swap one
                          hand card for the top of their deck.
                          (Future: requires strategy AI support.)
  hand_size_mod       on_roster only.  Adds effect_value to this team's
                          effective HAND_SIZE for the entire game (+1 = 6-card
                          hand, −1 = 4-card hand with stronger abilities).
  slide_lanes         on_attack only.  After blocks are revealed, attacker may
                          shift to an adjacent lane (lane±1).  effect_value=1
                          to enable.  Allows tactical repositioning to find
                          weaker blocks.  (Phase 4)
  back_row_pierce     on_attack only.  Back-row attacks from this player
                          ignore all blocks.  effect_value=1 to enable.
                          Only applies when attacking from back row.  (Phase 4)
  min_blocker_only    on_attack only.  Defending team's block in this lane is
                          limited to the MINIMUM card value placed (not the sum).
                          Punishes over-committing blocks.  (Phase 4)
  wild_block          on_block only.  Block cards at or below effect_value can
                          be placed on ANY attacked lane (choose after seeing
                          attacks).  Creates flexible defense.  (Phase 5)
  force_high_block    on_attack only.  Blocks at or below effect_value are
                          ignored for this attack.  Forces quality defense.  (Phase 5)
  deck_swap_opponent  on_dig_success only.  Replace opponent's highest card
                          with the top card of their deck.  Hand disruption.  (Phase 5)
  setter_cover        on_dig only (Libero or DS).  When a dig would otherwise
                          be resolved by the Setter (odd-value attacks on lanes
                          1 or 2), this player can intercept if their dig card
                          value >= effect_value.  Success: no broken play.
                          Failure: dig card still too low, setter had to reach
                          — broken play fires normally.  No condition_field
                          needed; the threshold IS effect_value.
                          Example: effect_value=5 means a 5+ dig card covers.

── Valid condition_field values ───────────────────────────────────────────────
  attack_card_value   The raw value of the attack card (int) — on_attack
  set_card_value      The raw value of the setter's card (int) — on_set only
  dig_type            "normal" or "tip"
  hand_size           Attacker's hand size at attack resolution time (after set
                          and hit cards are committed).  Use <=N for exhaustion
                          checks, e.g. hand_size <=2 fires a desperation bonus.
  (leave blank for no condition — ability always fires)

── is_active ──────────────────────────────────────────────────────────────────
  false  Passive — fires automatically when the trigger + condition match
  true   Active  — fires only when the controlling strategy chooses to activate
                   (not yet wired into RandomStrategy; reserved for future use)
"""

from __future__ import annotations

import csv
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, TYPE_CHECKING

if TYPE_CHECKING:
    from .players import PlayerRole


# ── Constants (kept as plain strings so the CSV is human-readable) ─────────────

class Trigger:
    ON_SERVE              = "on_serve"
    ON_RECEIVE            = "on_receive"
    ON_SET                = "on_set"
    ON_QUICK_SET          = "on_quick_set"
    ON_ATTACK             = "on_attack"
    ON_BLOCK              = "on_block"
    ON_BLOCK_DEFLECTION   = "on_block_deflection"
    ON_DIG                = "on_dig"
    ON_DIG_SUCCESS        = "on_dig_success"
    ON_DIG_FAILURE        = "on_dig_failure"
    ON_CHASE              = "on_chase"
    ON_TIP                = "on_tip"
    ON_ROSTER             = "on_roster"   # persistent team-level passive

class EffectType:
    ATTACK_VALUE_BONUS    = "attack_value_bonus"
    BLOCK_VALUE_BONUS     = "block_value_bonus"
    ADJACENT_BLOCK_BONUS  = "adjacent_block_bonus"
    PIERCE_BLOCK          = "pierce_block"
    SET_VALUE_DELTA       = "set_value_delta"
    SERVE_VALUE_BONUS     = "serve_value_bonus"
    CHASE_CARD_BONUS      = "chase_card_bonus"
    DEFLECT_DIG_THRESHOLD = "deflect_dig_threshold"
    TIP_VALUE_BONUS       = "tip_value_bonus"
    TIP_DIG_THRESHOLD     = "tip_dig_threshold"
    SINGLE_BLOCK_ONLY     = "single_block_only"
    DIG_THRESHOLD         = "dig_threshold"
    WIPE_BLOCK            = "wipe_block"
    NO_CHASE              = "no_chase"
    ROLL_SHOT             = "roll_shot"
    SEAM_SHOT             = "seam_shot"
    TIP_THRESHOLD_DELTA   = "tip_threshold_delta"
    OVER_BLOCK_BONUS      = "over_block_bonus"
    HOLD_CARD             = "hold_card"
    HAND_PEEK             = "hand_peek"
    EXCHANGE_CARD         = "exchange_card"
    HAND_SIZE_MOD         = "hand_size_mod"
    SLIDE_LANES           = "slide_lanes"          # Phase 4: Attack can shift to adjacent lane
    BACK_ROW_PIERCE       = "back_row_pierce"      # Phase 4: Back-row attacks pierce blocks
    MIN_BLOCKER_ONLY      = "min_blocker_only"     # Phase 4: Only minimum block card counts
    WILD_BLOCK            = "wild_block"           # Phase 5: Low cards can block any lane (legacy)
    FORCE_HIGH_BLOCK      = "force_high_block"     # Phase 5: Blocks below threshold ignored
    DECK_SWAP_OPPONENT    = "deck_swap_opponent"   # Phase 5: Replace opponent card with deck top
    WIDE_SPREAD_BONUS     = "wide_spread_bonus"    # Phase 6: Bonus when 2 blockers differ by >= effect_value
    SETTER_COVER         = "setter_cover"          # Libero/DS: intercept setter's dig zone if card >= threshold


# ── Data model ─────────────────────────────────────────────────────────────────

@dataclass
class Ability:
    ability_name:    str
    trigger:         str
    condition_field: str   # e.g. "attack_card_value", "" = always fires
    condition_value: str   # e.g. "5", "" = always fires
    effect:          str
    effect_value:    int
    is_active:       bool = False
    description:     str  = ""   # human-readable summary (optional)

    def condition_matches(self, context: Dict[str, Any]) -> bool:
        """
        Return True if this ability's condition is satisfied by context.

        condition_value supports comparison prefixes for numeric fields:
          >=N  e.g. attack_card_value  >=7  (fires on card 7, 8, 9, 10)
          <=N  e.g. attack_card_value  <=3
          >N   e.g. attack_card_value  >6
          <N   e.g. attack_card_value  <4
          N    plain equality (default)
        String fields (e.g. dig_type) always use case-insensitive equality.
        """
        if not self.condition_field:
            return True
        raw = context.get(self.condition_field)
        if raw is None:
            return False
        try:
            val  = int(raw)
            cond = self.condition_value.strip()
            if cond.startswith(">="):
                return val >= int(cond[2:])
            if cond.startswith("<="):
                return val <= int(cond[2:])
            if cond.startswith(">"):
                return val > int(cond[1:])
            if cond.startswith("<"):
                return val < int(cond[1:])
            return val == int(cond)
        except (ValueError, TypeError):
            return str(raw).lower() == self.condition_value.lower()


@dataclass
class PlayerCard:
    player_name: str
    role_name:   str                              # string role, e.g. "OH"
    abilities:   List[Ability] = field(default_factory=list)


# ── Ability engine (per-team, per-game) ────────────────────────────────────────

class AbilityEngine:
    """
    Resolves passive ability effects for one team during a game.

    Keyed by PlayerRole enum so game.py can look up the right player for each
    phase without any string comparisons in the hot path.
    """

    def __init__(self, cards: "Dict[PlayerRole, PlayerCard]") -> None:
        self._cards = cards
        self._pending_set_delta:           int = 0
        self._pending_mb_attack_bonus:     int = 0
        self._pending_tip_threshold_delta: int = 0
        self._current_hand_size:           int = 5   # updated per attack phase
        self._events:                      List[str] = []
        self.verbose:                      bool = False

    def reset(self) -> None:
        """Reset per-game state.  Call at the start of each new game."""
        self._pending_set_delta           = 0
        self._pending_mb_attack_bonus     = 0
        self._pending_tip_threshold_delta = 0

    def drain_events(self) -> List[str]:
        """Return and clear all queued ability-trigger messages."""
        evts = list(self._events)
        self._events.clear()
        return evts

    def _log(self, msg: str) -> None:
        self._events.append(msg)

    def _log_fired(self, player_name: str, ability_name: str, desc: str) -> None:
        """Log a boolean ability that just triggered."""
        self._log(f"  * [{player_name}] {ability_name}: {desc}")

    # ── Phase queries ──────────────────────────────────────────────────────────

    def serve_value_bonus(self) -> int:
        """Bonus to add to the Setter's serve card value."""
        from .players import PlayerRole
        return self._sum(PlayerRole.SETTER, Trigger.ON_SERVE,
                         EffectType.SERVE_VALUE_BONUS, {})

    def attack_value_bonus(self, role: "PlayerRole", attack_card_value: int) -> int:
        """Flat bonus to add to the attacker's card value."""
        return self._sum(role, Trigger.ON_ATTACK, EffectType.ATTACK_VALUE_BONUS,
                         {"attack_card_value": attack_card_value,
                          "hand_size": self._current_hand_size})

    def pierce_block(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if this attack ignores the block value entirely."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.PIERCE_BLOCK
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "pierce block (block ignored)")
                return True
        return False

    def block_value_bonus(self, role: "PlayerRole") -> int:
        """Flat bonus to add to this player's block card sum."""
        return self._sum(role, Trigger.ON_BLOCK, EffectType.BLOCK_VALUE_BONUS, {})

    def adjacent_block_bonus(self, mb_is_blocking: bool) -> int:
        """
        Bonus added to the two lanes adjacent to the MB when MB is blocking.
        Only fires if mb_is_blocking is True.
        """
        if not mb_is_blocking:
            return 0
        from .players import PlayerRole
        return self._sum(PlayerRole.MB, Trigger.ON_BLOCK,
                         EffectType.ADJACENT_BLOCK_BONUS, {})

    def record_dig_success(self, role: "PlayerRole", dig_type: str) -> None:
        """
        Call after a successful normal dig.  Accumulates any pending
        set_value_delta so the next SET phase can consume it.
        """
        delta = self._sum(role, Trigger.ON_DIG_SUCCESS,
                          EffectType.SET_VALUE_DELTA, {"dig_type": dig_type})
        self._pending_set_delta += delta

    def consume_set_delta(self) -> int:
        """Return (and clear) the pending set value modifier."""
        delta = self._pending_set_delta
        self._pending_set_delta = 0
        return delta

    def on_set_bonus(self, set_card_value: int = 0) -> int:
        """Setter's set bonus (on_set trigger); passes card value for conditional abilities."""
        from .players import PlayerRole
        return self._sum(PlayerRole.SETTER, Trigger.ON_SET, EffectType.SET_VALUE_DELTA,
                         {"set_card_value": set_card_value})

    def activate_quick_set(self) -> None:
        """Store MB quick-set attack bonus when MB is in the eligible lanes."""
        from .players import PlayerRole
        bonus = self._sum(PlayerRole.MB, Trigger.ON_QUICK_SET,
                          EffectType.ATTACK_VALUE_BONUS, {})
        self._pending_mb_attack_bonus += bonus

    def consume_mb_attack_bonus(self) -> int:
        """Consume and return the pending MB quick-set attack bonus."""
        b = self._pending_mb_attack_bonus
        self._pending_mb_attack_bonus = 0
        return b

    def chase_bonus(self) -> int:
        """Flat bonus added to chase running total (on_chase, Libero + DS)."""
        from .players import PlayerRole
        return (
            self._sum(PlayerRole.LIBERO, Trigger.ON_CHASE, EffectType.CHASE_CARD_BONUS, {})
            + self._sum(PlayerRole.DS,    Trigger.ON_CHASE, EffectType.CHASE_CARD_BONUS, {})
        )

    def dig_failure_chase_bonus(self) -> int:
        """Extra chase bonus that fires only on dig failure (on_dig_failure)."""
        from .players import PlayerRole
        return (
            self._sum(PlayerRole.LIBERO, Trigger.ON_DIG_FAILURE, EffectType.CHASE_CARD_BONUS, {})
            + self._sum(PlayerRole.DS,   Trigger.ON_DIG_FAILURE, EffectType.CHASE_CARD_BONUS, {})
        )

    def deflect_dig_threshold(self) -> int:
        """
        MB blocker makes deflections harder(+) or easier(-) for the attacker
        to control.  Applied as a penalty to the attacker’s dig target.
        """
        from .players import PlayerRole
        return self._sum(PlayerRole.MB, Trigger.ON_BLOCK_DEFLECTION,
                         EffectType.DEFLECT_DIG_THRESHOLD, {})

    def tip_value_bonus(self, role: "PlayerRole") -> int:
        """Bonus added to the attacker’s effective tip value."""
        return self._sum(role, Trigger.ON_TIP, EffectType.TIP_VALUE_BONUS, {})

    def tip_dig_threshold(self, role: "PlayerRole") -> int:
        """Bonus added to the defender’s dig card value vs incoming tips."""
        return self._sum(role, Trigger.ON_TIP, EffectType.TIP_DIG_THRESHOLD, {})

    def single_block_only(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """
        True if this attacker’s lane should be limited to one blocker’s
        highest card (ignores additional cards stacked in the same lane).
        """
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.SINGLE_BLOCK_ONLY
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "single block only (double block ignored)")
                return True
        return False

    def attack_dig_threshold(self, role: "PlayerRole") -> int:
        """
        Bonus added to the effective attack value for dig comparison only.
        Positive = attack is harder to dig (on_attack trigger, attacker side).
        """
        return self._sum(role, Trigger.ON_ATTACK, EffectType.DIG_THRESHOLD, {})

    def wipe_block(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if the attacker can wipe off a physical block on this card value."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.WIPE_BLOCK
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "wipe off block (instant point)")
                return True
        return False

    def no_chase(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if a failed kill dig cannot be chased (instant attacker point)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.NO_CHASE
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "no chase (instant point on failed dig)")
                return True
        return False

    def roll_shot(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if this attack fires as a roll shot (block ignored, no chase on fail)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.ROLL_SHOT
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "roll shot (block ignored, no chase)")
                return True
        return False

    def seam_shot(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if this attack fires as a seam shot (deflect outcome = attacker wins)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.SEAM_SHOT
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "seam shot (deflect = instant win)")
                return True
        return False

    def slide_lanes(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if attacker can shift to adjacent lane after blocks revealed (Phase 4)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.SLIDE_LANES
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "slide lanes eligible")
                return True
        return False

    def back_row_pierce(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if back-row attacks from this player ignore blocks (Phase 4)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.BACK_ROW_PIERCE
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "back-row pierce (block ignored)")
                return True
        return False

    def min_blocker_only(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if defending block is limited to minimum card (not sum) (Phase 4)."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.MIN_BLOCKER_ONLY
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "min blocker only (worst block card counts)")
                return True
        return False

    def wild_block_threshold(self, role: "PlayerRole") -> int:
        """Return threshold for wild block (0 if not available). Cards <= threshold can block any lane."""
        card = self._cards.get(role)
        if not card:
            return 0
        for a in card.abilities:
            if (a.effect == EffectType.WILD_BLOCK
                    and a.trigger == Trigger.ON_BLOCK
                    and not a.is_active):
                return a.effect_value
        return 0

    def wide_spread_bonus(self, role: "PlayerRole") -> int:
        """Return N when player has wide_spread_bonus N: fires when |card1-card2| >= N, adding +N to block."""
        card = self._cards.get(role)
        if not card:
            return 0
        for a in card.abilities:
            if (a.effect == EffectType.WIDE_SPREAD_BONUS
                    and a.trigger == Trigger.ON_BLOCK
                    and not a.is_active):
                return a.effect_value
        return 0

    def force_high_block_threshold(self, role: "PlayerRole", attack_card_value: int) -> int:
        """Return threshold for force_high_block (0 if not active). Blocks <= threshold are ignored."""
        card = self._cards.get(role)
        if not card:
            return 0
        ctx = {"attack_card_value": attack_card_value}
        for a in card.abilities:
            if (a.effect == EffectType.FORCE_HIGH_BLOCK
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                return a.effect_value
        return 0

    def deck_swap_opponent_on_dig(self, dig_type: str) -> bool:
        """True if this dig success triggers opponent hand disruption."""
        ctx = {"dig_type": dig_type}
        for card in self._cards.values():
            for a in card.abilities:
                if (a.effect == EffectType.DECK_SWAP_OPPONENT
                        and a.trigger == Trigger.ON_DIG_SUCCESS
                        and not a.is_active
                        and a.condition_matches(ctx)):
                    if self.verbose:
                        self._log_fired(card.player_name, a.ability_name,
                                        "deck swap (opponent discards hand)")
                    return True
        return False

    def activate_tip_threshold(self, set_card_value: int) -> None:
        """Call after the setter plays their card.  Accumulates tip_threshold_delta."""
        from .players import PlayerRole
        delta = self._sum(
            PlayerRole.SETTER, Trigger.ON_SET, EffectType.TIP_THRESHOLD_DELTA,
            {"set_card_value": set_card_value},
        )
        self._pending_tip_threshold_delta += delta

    def consume_tip_threshold_delta(self) -> int:
        """Return and clear the pending tip-threshold expansion for this exchange."""
        delta = self._pending_tip_threshold_delta
        self._pending_tip_threshold_delta = 0
        return delta

    def defender_dig_threshold(self, role: "PlayerRole") -> int:
        """
        Bonus added to the defender’s effective dig card value.
        Positive = dig target is reduced by this much (easier to dig).
        (on_dig trigger, Libero / DS side.)
        """
        return self._sum(role, Trigger.ON_DIG, EffectType.DIG_THRESHOLD, {})

    def setter_cover_threshold(self) -> int:
        """
        Return the minimum dig card value needed for an adjacent player
        (Libero or DS) to intercept a dig that would otherwise go to the Setter.
        Returns 0 if no such ability is present (setter digs normally).
        When multiple players have the ability, the easiest (lowest) threshold wins.
        """
        from .players import PlayerRole
        thresholds = []
        for role in (PlayerRole.LIBERO, PlayerRole.DS):
            card = self._cards.get(role)
            if not card:
                continue
            for a in card.abilities:
                if (a.effect == EffectType.SETTER_COVER
                        and a.trigger == Trigger.ON_DIG
                        and not a.is_active):
                    thresholds.append(a.effect_value)
                    if self.verbose:
                        self._log_fired(
                            card.player_name, a.ability_name,
                            f"setter cover active (threshold={a.effect_value})"
                        )
        return min(thresholds) if thresholds else 0

    def set_hand_size(self, n: int) -> None:
        """Update current attacker hand size for hand-exhaustion condition checks."""
        self._current_hand_size = n

    def over_block_bonus(self, role: "PlayerRole", attack_card_value: int,
                         is_double_blocked: bool) -> int:
        """Bonus to attack value when the lane was double-blocked."""
        if not is_double_blocked:
            return 0
        return self._sum(role, Trigger.ON_ATTACK, EffectType.OVER_BLOCK_BONUS,
                         {"attack_card_value": attack_card_value,
                          "hand_size": self._current_hand_size})

    def hold_card_check(self, role: "PlayerRole", attack_card_value: int) -> bool:
        """True if the chosen attack card should be retained in hand after this rally."""
        card = self._cards.get(role)
        if not card:
            return False
        ctx = {"attack_card_value": attack_card_value,
               "hand_size": self._current_hand_size}
        for a in card.abilities:
            if (a.effect == EffectType.HOLD_CARD
                    and a.trigger == Trigger.ON_ATTACK
                    and not a.is_active
                    and a.condition_matches(ctx)):
                if self.verbose:
                    self._log_fired(card.player_name, a.ability_name, "hold card (attack card returned to hand)")
                return True
        return False

    def hand_peek_count(self) -> int:
        """Cards to peek from top of deck before committing attack cards."""
        from .players import PlayerRole
        total = 0
        for role in (PlayerRole.OH, PlayerRole.MB, PlayerRole.OPP):
            total += self._sum(role, Trigger.ON_ATTACK, EffectType.HAND_PEEK,
                               {"hand_size": self._current_hand_size})
        return total

    def exchange_card_eligible(self) -> bool:
        """True if any attacking player on this team has the exchange_card ability."""
        from .players import PlayerRole
        for role in (PlayerRole.OH, PlayerRole.MB, PlayerRole.OPP):
            card = self._cards.get(role)
            if not card:
                continue
            if any(
                a.effect == EffectType.EXCHANGE_CARD
                and a.trigger == Trigger.ON_ATTACK
                and not a.is_active
                for a in card.abilities
            ):
                return True
        return False

    def hand_size_modifier(self) -> int:
        """Net change to HAND_SIZE for this team (scans all on_roster abilities)."""
        return sum(
            a.effect_value
            for pc in self._cards.values()
            for a in pc.abilities
            if a.effect == EffectType.HAND_SIZE_MOD
            and a.trigger == Trigger.ON_ROSTER
            and not a.is_active
        )

    # ── Internal helper ────────────────────────────────────────────────────────

    def _sum(
        self,
        role:    "PlayerRole",
        trigger: str,
        effect:  str,
        context: Dict[str, Any],
    ) -> int:
        card = self._cards.get(role)
        if not card:
            return 0
        total = 0
        for a in card.abilities:
            if (a.trigger == trigger
                    and a.effect == effect
                    and not a.is_active
                    and a.condition_matches(context)):
                total += a.effect_value
                if self.verbose and a.effect_value != 0:
                    sign = f"+{a.effect_value}" if a.effect_value > 0 else str(a.effect_value)
                    self._log(
                        f"  * [{card.player_name}] {a.ability_name}: "
                        f"{sign} {effect.replace('_', ' ')}"
                    )
        return total


# ── CSV loaders ────────────────────────────────────────────────────────────────

_ROLE_MAP: Dict[str, str] = {
    "setter": "SETTER",
    "opp":    "OPP",
    "mb":     "MB",
    "oh":     "OH",
    "ds":     "DS",
    "libero": "LIBERO",
}


def load_player_cards(path: Path) -> Dict[str, PlayerCard]:
    """
    Load all player-ability definitions from a CSV.
    Returns {player_name: PlayerCard}.

    Required columns:
      player_name, role, ability_name, trigger, condition_field,
      condition_value, effect, effect_value

    Optional column:
      is_active   (default: false)

    A player may appear on multiple rows — one row per ability.
    To define a player with no abilities, include a single row and leave
    ability_name blank.
    """
    cards: Dict[str, PlayerCard] = {}
    with open(path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            name = row["player_name"].strip()
            if not name:
                continue
            if name not in cards:
                cards[name] = PlayerCard(
                    player_name=name,
                    role_name=row["role"].strip(),
                )
            ability_name = row.get("ability_name", "").strip()
            if not ability_name:
                continue  # plain player, no ability on this row
            cards[name].abilities.append(Ability(
                ability_name    = ability_name,
                trigger         = row["trigger"].strip(),
                condition_field = row.get("condition_field", "").strip(),
                condition_value = row.get("condition_value", "").strip(),
                effect          = row["effect"].strip(),
                effect_value    = int(row.get("effect_value", 0) or 0),
                is_active       = row.get("is_active", "false").strip().lower() == "true",
                description     = row.get("description", "").strip(),
            ))
    return cards


def load_roster(
    roster_path: Path,
    player_cards: Dict[str, PlayerCard],
) -> "Dict[PlayerRole, PlayerCard]":
    """
    Build {PlayerRole: PlayerCard} from a roster CSV.

    Required columns: player_name, role
    Players not found in player_cards get a plain card with no abilities.
    """
    from .players import PlayerRole

    role_enum_map: Dict[str, PlayerRole] = {
        "SETTER": PlayerRole.SETTER,
        "OPP":    PlayerRole.OPP,
        "MB":     PlayerRole.MB,
        "OH":     PlayerRole.OH,
        "DS":     PlayerRole.DS,
        "LIBERO": PlayerRole.LIBERO,
    }

    roster: Dict[PlayerRole, PlayerCard] = {}
    with open(roster_path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            name     = row["player_name"].strip()
            role_str = row["role"].strip()
            role_key = _ROLE_MAP.get(role_str.lower(), role_str.upper())
            role     = role_enum_map.get(role_key)
            if role is None:
                raise ValueError(
                    f"Unknown role '{role_str}' for player '{name}' "
                    f"in {roster_path}"
                )
            card = player_cards.get(
                name, PlayerCard(player_name=name, role_name=role_str)
            )
            roster[role] = card
    return roster


def build_ability_engine(
    roster_path: Optional[Path],
    player_cards: Dict[str, PlayerCard],
) -> Optional[AbilityEngine]:
    """Build an AbilityEngine from a roster CSV, or None if not provided."""
    if roster_path is None:
        return None
    roster = load_roster(roster_path, player_cards)
    return AbilityEngine(roster)
