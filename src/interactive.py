"""..."""
from __future__ import annotations

from typing import Dict, List, Optional, Tuple, TYPE_CHECKING

from .cards import Card
from .players import GridPlayer
from .strategies import BaseStrategy

if TYPE_CHECKING:
    from .abilities import AbilityEngine
    from .players import SetTemplate


LANE_LABEL = {1: "OH (left)", 2: "MB (mid)", 3: "OPP (right)"}


def show_roster(
    your_engine: Optional["AbilityEngine"],
    your_label: str,
    opp_engine: Optional["AbilityEngine"],
    opp_label: str,
) -> None:
    """Print a compact ability reference card for both teams."""
    for engine, label, tag in [
        (your_engine, your_label, "YOUR TEAM"),
        (opp_engine,  opp_label,  "OPPONENT "),
    ]:
        print(f"\n{'='*52}")
        print(f"  {tag}: {label}")
        print(f"{'='*52}")
        if engine is None:
            print("  (no ability cards loaded)")
            continue
        for role, card in sorted(engine._cards.items(), key=lambda x: x[0].value):
            print(f"  {card.player_name}  [{role.value}]")
            if not card.abilities:
                print("    (no abilities)")
                continue
            for a in card.abilities:
                cond = ""
                if a.condition_field:
                    cond = f"  [if {a.condition_field} {a.condition_value}]"
                if a.description:
                    print(f"    {a.ability_name}{cond}: {a.description}")
                else:
                    sign = f"+{a.effect_value}" if a.effect_value > 0 else str(a.effect_value)
                    val  = f" {sign}" if a.effect_value != 0 else ""
                    print(f"    {a.ability_name}{cond}: {a.trigger} → {a.effect}{val}")
    print()


class InteractiveStrategy(BaseStrategy):
    """Strategy where every decision is prompted from stdin."""

    def __init__(
        self,
        your_engine: Optional["AbilityEngine"],
        opp_engine:  Optional["AbilityEngine"],
        verbose: bool = False,
        narrative: Optional[List[str]] = None,
    ) -> None:
        self._your      = your_engine
        self._opp       = opp_engine
        self._verbose   = verbose
        self._narrative = narrative

    # ── Internal helpers ───────────────────────────────────────────────────────

    def _drain(self) -> None:
        """Print any queued narrative events and ability-trigger events."""
        events: List[str] = []
        if self._narrative:
            events.extend(self._narrative)
            self._narrative.clear()
        if self._your:
            events.extend(self._your.drain_events())
        if self._opp:
            events.extend(self._opp.drain_events())
        if events:
            print()
            for e in events:
                print(e)

    def _show_hand(self, hand: List[Card], label: str = "Hand") -> None:
        parts = "  ".join(f"[{i+1}]{c.value}" for i, c in enumerate(hand))
        print(f"  {label}: {parts}")

    def _pick_card(self, hand: List[Card], prompt: str) -> Card:
        while True:
            try:
                raw = input(f"  {prompt} [1-{len(hand)}]: ").strip()
                idx = int(raw) - 1
                if 0 <= idx < len(hand):
                    return hand[idx]
            except (ValueError, EOFError):
                pass
            print(f"  ! Enter a number 1-{len(hand)}")

    def _pick_int(self, prompt: str, valid: List[int]) -> int:
        vs = "/".join(str(v) for v in valid)
        while True:
            try:
                val = int(input(f"  {prompt} [{vs}]: ").strip())
                if val in valid:
                    return val
            except (ValueError, EOFError):
                pass
            print(f"  ! Enter one of: {vs}")

    def _hdr(self, title: str) -> None:
        print(f"\n{'=' * 48}\n  {title}\n{'=' * 48}")

    # ── BaseStrategy implementation ────────────────────────────────────────────

    def choose_serve(
        self, hand: List[Card], eligible_receivers: List[GridPlayer]
    ) -> Tuple[Card, GridPlayer]:
        self._drain()
        self._hdr("YOUR SERVE")
        self._show_hand(hand)
        card = self._pick_card(hand, "Serve card")
        print("  Receiver targets:")
        for i, r in enumerate(eligible_receivers):
            print(f"    [{i+1}] {r.role.value}")
        t = self._pick_int("Target", list(range(1, len(eligible_receivers) + 1)))
        return card, eligible_receivers[t - 1]

    def choose_receive_card(self, hand: List[Card], serve_value: int) -> Card:
        self._drain()
        self._hdr(
            f"RECEIVE  |  incoming serve = {serve_value}  "
            f"(need >= {serve_value} to pass cleanly)"
        )
        self._show_hand(hand)
        return self._pick_card(hand, "Receive card")

    def choose_set_card(self, hand: List[Card], broken_play: bool = False) -> Card:
        self._drain()
        if broken_play:
            self._hdr("SET  *** BROKEN PLAY — non-setter setting ***")
            print(
                "  Set value → open lanes (restricted):\n"
                "    1-3  : front OH + MB   |  back MB   |  max 2 attackers\n"
                "    4-7  : front OH or OPP |  back MB   |  max 1 attacker   ← weakest\n"
                "    8-10 : front MB + OPP  |  back MB   |  max 2 attackers"
            )
        else:
            self._hdr("SET")
            print(
                "  Set value → open lanes:\n"
                "    1-3  : all 3 front only                       |  max 3\n"
                "    4-5  : OH + MB front, any back                |  max 3\n"
                "    6-7  : MB + OPP front, any back               |  max 3\n"
                "    8-9  : OH + OPP front, any back               |  max 4\n"
                "    10   : all front + all back                   |  max 4"
            )
        self._show_hand(hand)
        return self._pick_card(hand, "Set card")

    def choose_hit_cards(
        self, hand: List[Card], template: "SetTemplate"
    ) -> List[Tuple[int, Card, str]]:
        self._drain()
        front = ", ".join(LANE_LABEL[l] for l in template.front_lanes) or "—"
        back  = (", ".join(LANE_LABEL[l] for l in template.back_lanes)
                 if template.back_lanes else "none")
        self._hdr(
            f"ATTACK  |  front: {front}  |  back: {back}  "
            f"|  max {template.max_attackers} card(s)"
        )
        self._show_hand(hand)

        placements: List[Tuple[int, Card, str]] = []
        used: set = set()

        while len(placements) < template.max_attackers:
            avail_cards = [(orig_i, c) for orig_i, c in enumerate(hand) if orig_i not in used]
            if not avail_cards:
                break

            placed_slots = {(l, p) for l, _, p in placements}
            all_slots = ([(l, "front") for l in template.front_lanes]
                         + [(l, "back") for l in template.back_lanes])
            open_slots = [s for s in all_slots if s not in placed_slots]
            if not open_slots:
                break

            print(f"\n  Placement {len(placements) + 1} of up to {template.max_attackers}"
                  f"  (enter 0 when done):")
            for i, (l, pos) in enumerate(open_slots):
                print(f"    [{i+1}] Lane {l} — {LANE_LABEL[l]} ({pos})")

            try:
                si = int(input("  Slot (0 = done placing): ").strip())
            except (ValueError, EOFError):
                si = 0
            if si == 0:
                break
            if not (1 <= si <= len(open_slots)):
                print("  ! Invalid slot")
                continue

            lane, pos = open_slots[si - 1]
            print("  Cards available:")
            for i, (_, c) in enumerate(avail_cards):
                print(f"    [{i+1}] value {c.value}")

            try:
                ci = int(input("  Card: ").strip()) - 1
            except (ValueError, EOFError):
                ci = -1
            if not (0 <= ci < len(avail_cards)):
                print("  ! Invalid card index")
                continue

            orig_i, card = avail_cards[ci]
            used.add(orig_i)
            placements.append((lane, card, pos))
            print(f"  -> card {card.value} placed on lane {lane} ({pos})")

        if not placements:
            # Fallback: must place at least one card
            lane = template.front_lanes[0] if template.front_lanes else template.back_lanes[0]
            pos  = "front" if template.front_lanes else "back"
            card = hand[0]
            placements.append((lane, card, pos))
            print(f"  (Auto-placed card {card.value} on lane {lane})")

        return placements

    def choose_block_cards(
        self,
        hand: List[Card],
        attack_lanes: List[int],
        wild_threshold: int = 0,
        wide_spread_threshold: int = 0,
    ) -> Dict[int, List[Card]]:
        self._drain()
        atk_lbl = ", ".join(f"Lane {l} {LANE_LABEL[l]}" for l in attack_lanes)
        self._hdr(f"BLOCK  |  opponent attacked: {atk_lbl}")
        if wild_threshold:
            print(f"  Wild block: cards <= {wild_threshold} may go to ANY lane (1-3)")
        if wide_spread_threshold:
            print(
                f"  Wide spread: if your 2 block cards differ by >= {wide_spread_threshold} "
                f"you get a +{wide_spread_threshold} bonus"
            )
        self._show_hand(hand)
        print("  Assign cards to lanes — up to 2 per lane — enter 0 when done.")

        valid_base = sorted(set(attack_lanes))
        layout: Dict[int, List[Card]] = {}
        used: set = set()

        while True:
            avail = [(i, c) for i, c in enumerate(hand) if i not in used]
            if not avail:
                break

            blk_str = "  ".join(
                f"L{l}:[{','.join(str(c.value) for c in cs)}]"
                for l, cs in sorted(layout.items())
            ) or "(empty)"
            print(f"\n  Current blocks: {blk_str}")
            print("  Cards to place:")
            for i, (_, c) in enumerate(avail):
                print(f"    [{i+1}] value {c.value}")

            try:
                ci = int(input("  Card to place (0 = done): ").strip()) - 1
            except (ValueError, EOFError):
                ci = -2
            if ci == -1:   # user typed 0
                break
            if not (0 <= ci < len(avail)):
                print("  ! Invalid")
                continue

            orig_i, card = avail[ci]
            valid_lanes = (list(range(1, 4))
                           if wild_threshold and card.value <= wild_threshold
                           else valid_base)
            vs = "/".join(str(l) for l in valid_lanes)

            try:
                lane = int(input(f"  Lane for card {card.value} [{vs}]: ").strip())
            except (ValueError, EOFError):
                lane = -1
            if lane not in valid_lanes:
                print(f"  ! Must be one of: {vs}")
                continue
            if len(layout.get(lane, [])) >= 2:
                print("  ! Lane already has 2 cards (maximum)")
                continue

            layout.setdefault(lane, []).append(card)
            used.add(orig_i)
            print(f"  -> card {card.value} placed on lane {lane}")

        return layout

    def choose_attack_lane(
        self, attack_cards: Dict[int, Card], block_layout: Dict[int, int]
    ) -> int:
        self._drain()
        self._hdr("COMMIT LANE  |  block revealed")
        print("  lane  attack  block  diff  outlook")
        print("  " + "-" * 44)
        for lane, card in sorted(attack_cards.items()):
            blk = block_layout.get(lane, 0)
            if card.value == 0:
                # Blind-drawn lane — value not yet known
                print(f"    {lane}    {'?':<6}  {blk:<5}  {'?':>3}    UNKNOWN (blind draw — revealed after commit)")
            else:
                diff = card.value - blk
                if diff > 0:
                    out = "WIN (kill sequence)"
                elif diff >= -2:
                    out = "DEFLECT (you dig back)"
                else:
                    out = "STUFFED (block wins)"
                print(f"    {lane}    {card.value:<6}  {blk:<5}  {diff:+3d}    {out}")
        return self._pick_int("Commit to lane", sorted(attack_cards.keys()))

    def choose_tip_or_hit(self, attack_value: int, block_value: int) -> str:
        self._drain()
        self._hdr(f"SHOT SELECT  |  attack = {attack_value}  block = {block_value}")
        print("  [1] Hit  — resolve attack vs block normally")
        print("  [2] Tip  — bypass block; defender needs card <= your tip value to dig")
        print("             (only available when attack value <= 4)")
        choice = self._pick_int("Shot type", [1, 2])
        return "hit" if choice == 1 else "tip"

    def choose_dig_card(
        self, hand: List[Card], target_value: int, dig_type: str
    ) -> Card:
        self._drain()
        if dig_type == "tip":
            rule = f"need card <= {target_value} to dig (low card beats the tip)"
        else:
            rule = f"need card >= {target_value} to dig"
        self._hdr(f"DIG  |  {dig_type.upper()}  |  {rule}")
        self._show_hand(hand)
        return self._pick_card(hand, "Dig card")

    def choose_chase_card(
        self, hand: List[Card], running_total: int, target_value: int
    ) -> Card:
        self._drain()
        needed = target_value - running_total
        self._hdr(
            f"CHASE  |  running total: {running_total}  "
            f"|  target: {target_value}  |  need {needed} more"
        )
        self._show_hand(hand)
        return self._pick_card(hand, "Chase card")

    def choose_armed_attack_lane(self, hand: List[Card]) -> int:
        self._drain()
        self._hdr("ARMED ATTACK  |  chase succeeded — single front lane available")
        self._show_hand(hand)
        print("  [1] OH (left lane)    [3] OPP (right lane)")
        return self._pick_int("Lane", [1, 3])

    def choose_exchange_card(
        self, hand: List[Card], deck_top: Card
    ) -> Optional[Card]:
        self._drain()
        self._hdr(f"EXCHANGE  |  deck top = {deck_top.value}")
        self._show_hand(hand)
        print("  [0] Decline exchange")
        while True:
            try:
                idx = int(input(f"  Choice [0-{len(hand)}]: ").strip())
                if idx == 0:
                    return None
                if 1 <= idx <= len(hand):
                    return hand[idx - 1]
            except (ValueError, EOFError):
                pass
            print(f"  ! Enter 0 to decline or 1-{len(hand)}")
