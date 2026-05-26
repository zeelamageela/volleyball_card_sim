"""
Tests for rally-ending distribution, matching mechanics, and ability effectiveness.

Coverage areas:
  - Rally outcome frequencies (stuffed, matching deflections, tips, rolls, wipes)
  - SmartStrategy blocking correctness (never places same-value pairs)
  - AbilityEngine unit tests: wide_spread_bonus, wipe_block, roll_shot,
    pierce_block, no_chase
  - Wide-spread bonus integration via _phase_block_commit

Run with: python -m pytest tests/test_rally_endings.py -v
"""

from __future__ import annotations

import random
import unittest
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from src.cards import Card
from src.players import Team, PlayerRole
from src.game import Rally, Game
from src.game_state import RallyResult
from src.strategies import BaseStrategy, RandomStrategy, SmartStrategy
from src.abilities import (
    Ability,
    AbilityEngine,
    EffectType,
    PlayerCard,
    Trigger,
    load_player_cards,
    build_ability_engine,
)

# ── Paths ─────────────────────────────────────────────────────────────────────

_CARDS_CSV  = Path(__file__).parent.parent / "data" / "player_cards.csv"
_PHASE5_CSV = Path(__file__).parent.parent / "data" / "team_phase5.csv"
_DUMMY_MED  = Path(__file__).parent.parent / "data" / "team_dummy_medium.csv"
_TEST2_CSV  = Path(__file__).parent.parent / "data" / "team_test2.csv"

# ── Helpers ───────────────────────────────────────────────────────────────────

def _run_games_smart(
    roster_a: Optional[Path] = None,
    roster_b: Optional[Path] = None,
    n_games: int = 300,
    seed: int = 42,
) -> List[RallyResult]:
    """Run n_games with SmartStrategy on both sides; return all RallyResults."""
    player_cards = load_player_cards(_CARDS_CSV) if (roster_a or roster_b) else {}
    results: List[RallyResult] = []
    rng = random.Random(seed)
    for _ in range(n_games):
        sub = random.Random(rng.randint(0, 2 ** 31))
        team_a = Team("A", random.Random(sub.randint(0, 2 ** 31)))
        team_b = Team("B", random.Random(sub.randint(0, 2 ** 31)))
        if roster_a:
            team_a.ability_engine = build_ability_engine(roster_a, player_cards)
        if roster_b:
            team_b.ability_engine = build_ability_engine(roster_b, player_cards)
        game = Game(
            team_a, team_b,
            SmartStrategy(random.Random(sub.randint(0, 2 ** 31))),
            SmartStrategy(random.Random(sub.randint(0, 2 ** 31))),
            sub,
        )
        results.extend(game.play().rally_results)
    return results


def _engine_for(*player_names: str) -> AbilityEngine:
    """Build an AbilityEngine containing the named players at their natural roles."""
    all_cards = load_player_cards(_CARDS_CSV)
    roster: Dict[PlayerRole, PlayerCard] = {}
    for name in player_names:
        pc = all_cards[name]
        roster[PlayerRole[pc.role_name]] = pc
    return AbilityEngine(roster)


class _FixedBlockStrategy(BaseStrategy):
    """Places a predetermined block; all other decisions pick highest available card."""

    def __init__(self, placement: Dict[int, List[Card]]) -> None:
        self._p = placement

    def choose_serve(self, hand, eligible_receivers):
        return hand[0], eligible_receivers[0]

    def choose_receive_card(self, hand, serve_value):
        return max(hand, key=lambda c: c.value)

    def choose_set_card(self, hand, broken_play=False):
        return max(hand, key=lambda c: c.value)

    def choose_hit_cards(self, hand, template):
        if template.front_lanes and hand:
            return [(template.front_lanes[0], max(hand, key=lambda c: c.value), "front")]
        return []

    def choose_block_cards(self, hand, attack_lanes, wild_threshold=0, wide_spread_threshold=0):
        return {lane: self._p[lane] for lane in attack_lanes if lane in self._p}

    def choose_attack_lane(self, attack_cards, block_layout):
        return list(attack_cards.keys())[0]

    def choose_tip_or_hit(self, attack_value, block_value):
        return "hit"

    def choose_dig_card(self, hand, target_value, dig_type):
        return max(hand, key=lambda c: c.value)

    def choose_chase_card(self, hand, running_total, target_value):
        return max(hand, key=lambda c: c.value)

    def choose_armed_attack_lane(self, hand):
        return 1

    def choose_exchange_card(self, hand, deck_top):
        return None

    def choose_cover_attempt(self, hand, threshold):
        return None  # test stub: never attempts cover


def _make_rally_for_block(
    blocker_engine: AbilityEngine,
    block_cards: List[Card],
    block_lane: int,
    seed: int = 0,
) -> Tuple["Rally", "Team"]:
    """Create a Rally with blocker_engine on team_a; put block_cards in team_a.hand."""
    rng = random.Random(seed)
    team_a = Team("A", random.Random(rng.randint(0, 2 ** 31)))
    team_b = Team("B", random.Random(rng.randint(0, 2 ** 31)))
    team_a.ability_engine = blocker_engine
    # Inject specific cards directly into the hand
    team_a.hand = list(block_cards)
    team_b.draw_starting_hand()
    fixed = _FixedBlockStrategy({block_lane: block_cards})
    rally = Rally(team_a, team_b, fixed, RandomStrategy(random.Random(seed)), rng)
    return rally, team_a


# ═══════════════════════════════════════════════════════════════════════════════
#  1. Rally ending distribution (statistical)
# ═══════════════════════════════════════════════════════════════════════════════

class TestRallyEndingDistribution(unittest.TestCase):
    """Verify the distribution of rally ending types over many games."""

    @classmethod
    def setUpClass(cls):
        cls._rallies = _run_games_smart(n_games=300, seed=7)

    def _pct(self, pred) -> float:
        matched = sum(1 for r in self._rallies if pred(r))
        return matched / len(self._rallies)

    def test_stuffed_is_most_common_ending(self):
        """Stuffed blocks should be 35–70% of all rallies."""
        rate = self._pct(lambda r: r.reason.startswith("Stuffed"))
        self.assertGreater(rate, 0.35, f"Stuffed only {rate:.1%} — blocks seem too weak")
        self.assertLess(rate, 0.70, f"Stuffed {rate:.1%} — blocks seem too dominant")

    def test_matching_deflections_are_common(self):
        """Deflect-outs (draw-in) should still occur on multi-lane attacks (>0.5%)."""
        rate = self._pct(lambda r: "deflection out" in r.reason)
        self.assertGreater(rate, 0.005, f"Matching deflections only {rate:.1%}")

    def test_tips_occur(self):
        """Tip-not-dug endings should be present (>3%)."""
        rate = self._pct(lambda r: r.reason.startswith("Tip not dug"))
        self.assertGreater(rate, 0.03, f"Tips only {rate:.1%}")

    def test_kills_occur(self):
        """Kills (attack breaks through, dig fails) should be present (>0.5%)."""
        rate = self._pct(lambda r: "Kill" in r.reason or "chase" in r.reason)
        self.assertGreater(rate, 0.005, f"Kills only {rate:.1%}")

    def test_all_rallies_have_a_winner(self):
        for r in self._rallies:
            self.assertIn(r.winner_name, {"A", "B"})

    def test_all_rallies_have_a_reason(self):
        for r in self._rallies:
            self.assertTrue(r.reason, "Rally result has empty reason string")


# ═══════════════════════════════════════════════════════════════════════════════
#  2. SmartStrategy blocking mechanics
# ═══════════════════════════════════════════════════════════════════════════════

class TestSmartBlockStrategy(unittest.TestCase):
    """Verify SmartStrategy never places same-value pairs and respects wide_spread_threshold."""

    def setUp(self):
        self._strat = SmartStrategy(random.Random(0))

    def _hand(self, *values: int) -> List[Card]:
        colors = ["red", "black", "red", "black", "red", "black"]
        return [Card(v, colors[i % 2]) for i, v in enumerate(values)]

    # ── No same-value pairs ────────────────────────────────────────────────

    def test_never_same_value_pair_single_lane(self):
        hand = self._hand(7, 7, 5, 3, 1)
        placement = self._strat.choose_block_cards(hand, [1])
        cards = placement.get(1, [])
        if len(cards) == 2:
            self.assertNotEqual(cards[0].value, cards[1].value)

    def test_never_same_value_pair_two_lanes(self):
        hand = self._hand(8, 8, 6, 6, 3)
        for lane_set in ([1, 2], [2, 3], [1, 3]):
            placement = self._strat.choose_block_cards(hand, lane_set)
            for lane, cards in placement.items():
                if len(cards) == 2:
                    self.assertNotEqual(
                        cards[0].value, cards[1].value,
                        f"Same-value pair on lane {lane} with hand {[c.value for c in hand]}",
                    )

    def test_places_single_card_when_all_values_identical(self):
        """If every card in hand shares the same value, only place 1 card per lane."""
        hand = self._hand(5, 5, 5, 5, 5)
        placement = self._strat.choose_block_cards(hand, [2])
        cards = placement.get(2, [])
        self.assertEqual(len(cards), 1, "Should single-block when all cards are identical")

    # ── Wide-spread threshold awareness ───────────────────────────────────

    def test_prefers_spread_pair_when_sum_is_better(self):
        """[10, 4] with threshold=4 earns bonus: 10+4+4=18 beats [10, 9]=19? No — 19 wins.
        But [9, 5] with threshold=4: 9+5+4=18 vs [9, 8]=17 → spread wins."""
        hand = self._hand(9, 8, 5, 3, 1)
        placement = self._strat.choose_block_cards(hand, [2], wide_spread_threshold=4)
        cards = placement.get(2, [])
        if len(cards) == 2:
            spread = abs(cards[0].value - cards[1].value)
            values = sorted((cards[0].value, cards[1].value), reverse=True)
            effective_with_spread = values[0] + values[1] + 4 if spread >= 4 else 0
            effective_plain = values[0] + values[1]
            # Strategy should have picked the better effective value
            best_possible_spread = abs(9 - 5)  # = 4, exactly at threshold
            best_possible_plain  = 9 + 8        # = 17
            best_possible_with   = 9 + 5 + 4    # = 18
            if best_possible_with >= best_possible_plain:
                self.assertGreaterEqual(spread, 4,
                    "SmartStrategy should seek spread pair when it's worth more")

    def test_keeps_plain_pair_when_spread_is_worse(self):
        """[9, 8]=17 beats [9, 3]+3=15 → strategy keeps the plain top-2 pair."""
        hand = self._hand(9, 8, 3, 2, 1)
        placement = self._strat.choose_block_cards(hand, [2], wide_spread_threshold=3)
        cards = placement.get(2, [])
        if len(cards) == 2:
            hi_val = max(c.value for c in cards)
            lo_val = min(c.value for c in cards)
            # spread=3 pair would be [9,3]=12+3=15; plain pair [9,8]=17
            # Strategy should prefer [9,8] since 17 > 15
            self.assertGreater(lo_val, 3,
                "Strategy should prefer [9,8] over [9,3] when plain sum is higher")


# ═══════════════════════════════════════════════════════════════════════════════
#  3. Wipe block ability
# ═══════════════════════════════════════════════════════════════════════════════

class TestWipeBlockAbility(unittest.TestCase):

    def setUp(self):
        self._engine = _engine_for("Trickster")  # roll_shot >=6, wipe_block card=1

    def test_fires_on_card_value_1(self):
        self.assertTrue(self._engine.wipe_block(PlayerRole.OH, 1))

    def test_off_for_card_value_2(self):
        self.assertFalse(self._engine.wipe_block(PlayerRole.OH, 2))

    def test_off_for_card_value_10(self):
        self.assertFalse(self._engine.wipe_block(PlayerRole.OH, 10))

    def test_off_for_wrong_role(self):
        self.assertFalse(self._engine.wipe_block(PlayerRole.OPP, 1))

    def test_wipe_endings_appear_with_trickster_team(self):
        """With Trickster on Team A, 'Wipe off the block' endings should appear (>0.5%)."""
        rallies = _run_games_smart(roster_a=_DUMMY_MED, n_games=200, seed=12)
        wipe_count = sum(1 for r in rallies if r.reason.startswith("Wipe off the block"))
        rate = wipe_count / len(rallies)
        self.assertGreater(rate, 0.005,
            f"Expected wipe endings with Trickster team, got {rate:.1%}")


# ═══════════════════════════════════════════════════════════════════════════════
#  4. Roll shot ability
# ═══════════════════════════════════════════════════════════════════════════════

class TestRollShotAbility(unittest.TestCase):

    def setUp(self):
        self._engine = _engine_for("Trickster")  # roll_shot >=6

    def test_fires_at_threshold(self):
        self.assertTrue(self._engine.roll_shot(PlayerRole.OH, 6))

    def test_fires_above_threshold(self):
        self.assertTrue(self._engine.roll_shot(PlayerRole.OH, 10))

    def test_off_below_threshold(self):
        self.assertFalse(self._engine.roll_shot(PlayerRole.OH, 5))

    def test_off_for_wrong_role(self):
        self.assertFalse(self._engine.roll_shot(PlayerRole.MB, 8))

    def test_roller_fires_at_6(self):
        engine = _engine_for("Roller")
        self.assertTrue(engine.roll_shot(PlayerRole.OH, 6))
        self.assertFalse(engine.roll_shot(PlayerRole.OH, 5))

    def test_roll_shot_endings_appear_with_roll_team(self):
        """With Trickster (roll_shot >=6) on Team A, roll-shot endings should appear (>1%)."""
        rallies = _run_games_smart(roster_a=_DUMMY_MED, n_games=200, seed=17)
        roll_count = sum(1 for r in rallies if r.reason.startswith("Roll shot"))
        rate = roll_count / len(rallies)
        self.assertGreater(rate, 0.01,
            f"Expected roll-shot endings with Trickster team, got {rate:.1%}")


# ═══════════════════════════════════════════════════════════════════════════════
#  5. Pierce block ability
# ═══════════════════════════════════════════════════════════════════════════════

class TestPierceBlockAbility(unittest.TestCase):

    def test_titan_pierce_fires_at_9(self):
        engine = _engine_for("Titan")
        self.assertTrue(engine.pierce_block(PlayerRole.OPP, 9))

    def test_titan_pierce_fires_at_10(self):
        engine = _engine_for("Titan")
        self.assertTrue(engine.pierce_block(PlayerRole.OPP, 10))

    def test_titan_pierce_off_at_8(self):
        engine = _engine_for("Titan")
        self.assertFalse(engine.pierce_block(PlayerRole.OPP, 8))

    def test_blade_pierce_fires_at_8(self):
        engine = _engine_for("Blade")
        self.assertTrue(engine.pierce_block(PlayerRole.OH, 8))

    def test_blade_pierce_off_at_7(self):
        engine = _engine_for("Blade")
        self.assertFalse(engine.pierce_block(PlayerRole.OH, 7))

    def test_phantom_pierce_fires_at_7(self):
        engine = _engine_for("Phantom")
        self.assertTrue(engine.pierce_block(PlayerRole.OPP, 7))

    def test_pierce_off_for_wrong_role(self):
        engine = _engine_for("Titan")
        # Titan is OPP; pierce should not trigger for OH role
        self.assertFalse(engine.pierce_block(PlayerRole.OH, 9))


# ═══════════════════════════════════════════════════════════════════════════════
#  6. No-chase ability (Breaker OPP)
# ═══════════════════════════════════════════════════════════════════════════════

class TestNoChaseAbility(unittest.TestCase):

    def setUp(self):
        # Breaker appears as both OH and OPP in the CSV; load_player_cards merges
        # abilities under the first role encountered (OH). Explicitly assign to OPP.
        all_cards = load_player_cards(_CARDS_CSV)
        self._engine = AbilityEngine({PlayerRole.OPP: all_cards["Breaker"]})

    def test_fires_at_threshold(self):
        self.assertTrue(self._engine.no_chase(PlayerRole.OPP, 9))

    def test_fires_above_threshold(self):
        self.assertTrue(self._engine.no_chase(PlayerRole.OPP, 10))

    def test_off_below_threshold(self):
        self.assertFalse(self._engine.no_chase(PlayerRole.OPP, 8))

    def test_cannon_no_chase_fires_at_8(self):
        engine = _engine_for("Cannon")
        self.assertTrue(engine.no_chase(PlayerRole.OPP, 8))
        self.assertFalse(engine.no_chase(PlayerRole.OPP, 7))

    def test_no_chase_endings_appear_with_roll_team(self):
        """Roll-shot endings carry 'no chase' in their reason string.
        team_dummy_medium has Trickster (roll_shot >=6) → should produce >0 such endings."""
        rallies = _run_games_smart(roster_a=_DUMMY_MED, n_games=200, seed=21)
        nc = sum(1 for r in rallies if "no chase" in r.reason.lower())
        self.assertGreater(nc, 0,
            "Expected 'no chase' endings via roll-shot or no-chase abilities")


# ═══════════════════════════════════════════════════════════════════════════════
#  7. Wide-spread bonus ability (unit + integration)
# ═══════════════════════════════════════════════════════════════════════════════

class TestWideSpreadBonusAbility(unittest.TestCase):
    """Unit tests for AbilityEngine.wide_spread_bonus() and game.py integration."""

    # ── Engine value queries ───────────────────────────────────────────────

    def test_flex_returns_4_for_mb(self):
        engine = _engine_for("Flex")
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.MB), 4)

    def test_flex_returns_0_for_non_mb(self):
        engine = _engine_for("Flex")
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.OH), 0)
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.OPP), 0)

    def test_shield_returns_5_for_mb(self):
        engine = _engine_for("Shield")
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.MB), 5)

    def test_prism_returns_6_for_mb(self):
        engine = _engine_for("Prism")
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.MB), 6)

    def test_atlas_has_no_wide_spread_bonus(self):
        engine = _engine_for("Atlas")
        self.assertEqual(engine.wide_spread_bonus(PlayerRole.MB), 0)

    # ── _phase_block_commit integration ───────────────────────────────────

    def _block_layout(self, player_name: str, card_values: Tuple[int, ...],
                      lane: int = 2, seed: int = 0) -> Dict[int, int]:
        """Run _phase_block_commit and return the resulting block_layout dict."""
        engine = _engine_for(player_name)
        cards = [Card(v, "red" if i % 2 == 0 else "black")
                 for i, v in enumerate(card_values)]
        rally, team_a = _make_rally_for_block(engine, cards, lane, seed)
        fixed = _FixedBlockStrategy({lane: cards})
        block_layout, _, _ = rally._phase_block_commit(team_a, fixed, [lane])
        return block_layout

    def test_bonus_fires_when_spread_exactly_at_threshold(self):
        """Flex(4): [9, 5] spread=4 → base=14 + block_value_bonus=2 + wide_spread=4 = 20."""
        layout = self._block_layout("Flex", (9, 5))
        self.assertEqual(layout[2], 20)

    def test_bonus_fires_when_spread_above_threshold(self):
        """Flex(4): [10, 3] spread=7 → base=13 + block_value_bonus=2 + wide_spread=4 = 19."""
        layout = self._block_layout("Flex", (10, 3))
        self.assertEqual(layout[2], 19)

    def test_bonus_not_fired_when_spread_below_threshold(self):
        """Flex(4): [9, 6] spread=3 → 9+6 = 15 (no bonus)."""
        layout = self._block_layout("Flex", (9, 6))
        # Flex also has block_value_bonus(2), so base sum = 15+2 = 17; no spread bonus
        self.assertEqual(layout[2], 17)

    def test_bonus_not_fired_with_single_card(self):
        """Flex(4): placing only [8] → 8+2 (block_value_bonus) = 10, no spread bonus."""
        layout = self._block_layout("Flex", (8,))
        self.assertEqual(layout[2], 10)

    def test_shield_threshold_5_fires_correctly(self):
        """Shield(5): [9, 4] spread=5 → 9+4+5 = 18."""
        layout = self._block_layout("Shield", (9, 4))
        self.assertEqual(layout[2], 18)

    def test_shield_threshold_5_not_fired_at_spread_4(self):
        """Shield(5): [9, 5] spread=4 < 5 → 9+5 = 14 (no spread bonus, no block_value_bonus)."""
        layout = self._block_layout("Shield", (9, 5))
        self.assertEqual(layout[2], 14)

    def test_prism_threshold_6_fires_at_spread_6(self):
        """Prism(6): [10, 4] spread=6 → 10+4+6 = 20."""
        layout = self._block_layout("Prism", (10, 4))
        self.assertEqual(layout[2], 20)

    def test_prism_threshold_6_not_fired_at_spread_5(self):
        """Prism(6): [9, 4] spread=5 < 6 → 9+4 = 13 (no bonus)."""
        layout = self._block_layout("Prism", (9, 4))
        self.assertEqual(layout[2], 13)

    def test_wide_spread_games_run_without_error(self):
        """Full games with wide_spread_bonus players complete without exceptions."""
        rallies = _run_games_smart(roster_a=_PHASE5_CSV, n_games=100, seed=99)
        self.assertGreater(len(rallies), 0)
        for r in rallies:
            self.assertTrue(r.reason)
