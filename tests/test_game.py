"""
Unit tests for volleyball_card_sim.

Run with:  python -m pytest tests/ -v
  or:      python -m unittest discover tests/
"""

from __future__ import annotations

import random
import unittest

from src.cards import Card, Deck, HAND_SIZE
from src.players import Team, SET_ELIGIBLE_LANES, PlayerRole
from src.game import resolve_attack, Rally, Game, POINTS_TO_WIN
from src.game_state import AttackOutcomeType
from src.strategies import RandomStrategy


# ── resolve_attack ─────────────────────────────────────────────────────────────

class TestResolveAttack(unittest.TestCase):

    def test_kill_attack_greater_than_block(self):
        self.assertEqual(resolve_attack(7, 5), AttackOutcomeType.KILL)

    def test_kill_unblocked_lane(self):
        # Unblocked lane: block_value = 0; any attack wins
        self.assertEqual(resolve_attack(1, 0), AttackOutcomeType.KILL)

    def test_kill_exact_greater(self):
        self.assertEqual(resolve_attack(10, 9), AttackOutcomeType.KILL)

    def test_deflect_diff_1(self):
        self.assertEqual(resolve_attack(6, 7), AttackOutcomeType.DEFLECT)

    def test_deflect_diff_2(self):
        self.assertEqual(resolve_attack(5, 7), AttackOutcomeType.DEFLECT)

    def test_stuffed_diff_3(self):
        self.assertEqual(resolve_attack(4, 7), AttackOutcomeType.STUFFED)

    def test_stuffed_diff_large(self):
        self.assertEqual(resolve_attack(1, 10), AttackOutcomeType.STUFFED)

    def test_boundary_attack_equals_block(self):
        # attack == block: diff = 0, which is < 1 ... actually attack == block means
        # attack is NOT > block, so diff = 0. diff <= 2 → DEFLECT
        self.assertEqual(resolve_attack(5, 5), AttackOutcomeType.DEFLECT)


# ── SET_ELIGIBLE_LANES ────────────────────────────────────────────────────────

class TestSetEligibleLanes(unittest.TestCase):

    def test_quick_set_1(self):
        self.assertEqual(SET_ELIGIBLE_LANES[1], [1, 2])

    def test_quick_set_2(self):
        self.assertEqual(SET_ELIGIBLE_LANES[2], [1, 2])

    def test_quick_set_3(self):
        self.assertEqual(SET_ELIGIBLE_LANES[3], [1, 2])

    def test_weak_side_4(self):
        self.assertEqual(SET_ELIGIBLE_LANES[4], [3, 2])

    def test_weak_side_5(self):
        self.assertEqual(SET_ELIGIBLE_LANES[5], [3, 2])

    def test_strong_side_6(self):
        self.assertEqual(SET_ELIGIBLE_LANES[6], [1, 2])

    def test_strong_side_7(self):
        self.assertEqual(SET_ELIGIBLE_LANES[7], [1, 2])

    def test_high_outside_8(self):
        self.assertEqual(SET_ELIGIBLE_LANES[8], [1, 3])

    def test_high_outside_9(self):
        self.assertEqual(SET_ELIGIBLE_LANES[9], [1, 3])

    def test_free_choice_10(self):
        # All three lanes eligible
        self.assertEqual(sorted(SET_ELIGIBLE_LANES[10]), [1, 2, 3])

    def test_all_values_covered(self):
        for v in range(1, 11):
            self.assertIn(v, SET_ELIGIBLE_LANES)
            self.assertTrue(len(SET_ELIGIBLE_LANES[v]) >= 2)


# ── Deck ──────────────────────────────────────────────────────────────────────

class TestDeck(unittest.TestCase):

    def _make_deck(self, seed: int = 0) -> Deck:
        return Deck(random.Random(seed))

    def test_deck_has_20_cards(self):
        deck = self._make_deck()
        self.assertEqual(deck.draw_pile_size, 20)

    def test_draw_reduces_pile(self):
        deck = self._make_deck()
        deck.draw()
        self.assertEqual(deck.draw_pile_size, 19)

    def test_discard_increases_discard_pile(self):
        deck = self._make_deck()
        card = deck.draw()
        deck.discard(card)
        self.assertEqual(deck.discard_pile_size, 1)

    def test_reshuffle_when_draw_empty(self):
        deck = self._make_deck()
        # Draw all 20 cards, discard them
        cards = [deck.draw() for _ in range(20)]
        for c in cards:
            deck.discard(c)
        self.assertEqual(deck.draw_pile_size, 0)
        self.assertEqual(deck.discard_pile_size, 20)
        # Drawing again should trigger a reshuffle
        drawn = deck.draw()
        self.assertIsInstance(drawn, Card)
        # After reshuffle, discard is empty and draw pile had 20 cards, drew 1
        self.assertEqual(deck.draw_pile_size, 19)
        self.assertEqual(deck.discard_pile_size, 0)

    def test_error_both_empty(self):
        deck = self._make_deck()
        # Drain draw pile without discarding
        for _ in range(20):
            deck.draw()
        with self.assertRaises(RuntimeError):
            deck.draw()

    def test_deck_values_are_1_to_10_each_color(self):
        deck = self._make_deck()
        cards = [deck.draw() for _ in range(20)]
        red_vals   = sorted(c.value for c in cards if c.color == "red")
        black_vals = sorted(c.value for c in cards if c.color == "black")
        self.assertEqual(red_vals,   list(range(1, 11)))
        self.assertEqual(black_vals, list(range(1, 11)))


# ── Team hand mechanics ────────────────────────────────────────────────────────

class TestTeamHand(unittest.TestCase):

    def _make_team(self, seed: int = 42) -> Team:
        return Team("Test", random.Random(seed))

    def test_draw_starting_hand_gives_5_cards(self):
        team = self._make_team()
        team.draw_starting_hand()
        self.assertEqual(len(team.hand), HAND_SIZE)

    def test_refill_hand_restores_to_5(self):
        team = self._make_team()
        team.draw_starting_hand()
        team.play_card(team.hand[0])
        team.play_card(team.hand[0])
        self.assertEqual(len(team.hand), 3)
        team.refill_hand()
        self.assertEqual(len(team.hand), HAND_SIZE)

    def test_eligible_receivers_excludes_setter(self):
        team = self._make_team()
        receivers = team.eligible_receivers()
        roles = [p.role for p in receivers]
        self.assertNotIn(PlayerRole.SETTER, roles)

    def test_eligible_receivers_are_back_row(self):
        team = self._make_team()
        for p in team.eligible_receivers():
            self.assertTrue(p.is_back_row())


# ── Full rally smoke test ──────────────────────────────────────────────────────

class TestRally(unittest.TestCase):

    def _make_rally(self, seed: int = 0) -> Rally:
        rng = random.Random(seed)
        team_a = Team("A", random.Random(rng.randint(0, 2**31)))
        team_b = Team("B", random.Random(rng.randint(0, 2**31)))
        team_a.draw_starting_hand()
        team_b.draw_starting_hand()
        strat_a = RandomStrategy(random.Random(rng.randint(0, 2**31)))
        strat_b = RandomStrategy(random.Random(rng.randint(0, 2**31)))
        return Rally(team_a, team_b, strat_a, strat_b, rng)

    def test_rally_returns_result(self):
        rally = self._make_rally()
        result = rally.play()
        self.assertIn(result.winner_name, ["A", "B"])
        self.assertGreaterEqual(result.rally_length, 0)

    def test_rally_winner_name_is_valid(self):
        for seed in range(20):
            rally = self._make_rally(seed)
            result = rally.play()
            self.assertIn(result.winner_name, ["A", "B"])


# ── Full game smoke test ───────────────────────────────────────────────────────

class TestGame(unittest.TestCase):

    def _make_game(self, seed: int = 0) -> Game:
        rng = random.Random(seed)
        strat_a = RandomStrategy(random.Random(rng.randint(0, 2**31)))
        strat_b = RandomStrategy(random.Random(rng.randint(0, 2**31)))
        team_a  = Team("Team A", random.Random(rng.randint(0, 2**31)))
        team_b  = Team("Team B", random.Random(rng.randint(0, 2**31)))
        return Game(team_a, team_b, strat_a, strat_b, rng)

    def test_game_produces_winner(self):
        game = self._make_game()
        result = game.play()
        self.assertIn(result.winner_name, ["Team A", "Team B"])

    def test_winner_reached_points_to_win(self):
        game = self._make_game()
        result = game.play()
        self.assertEqual(result.scores[result.winner_name], POINTS_TO_WIN)

    def test_loser_has_fewer_points_than_winner(self):
        game = self._make_game()
        result = game.play()
        loser = "Team B" if result.winner_name == "Team A" else "Team A"
        self.assertLess(result.scores[loser], POINTS_TO_WIN)

    def test_game_has_rallies(self):
        game = self._make_game()
        result = game.play()
        self.assertGreater(result.total_rallies, 0)

    def test_deterministic_with_seed(self):
        result1 = self._make_game(seed=99).play()
        result2 = self._make_game(seed=99).play()
        self.assertEqual(result1.winner_name, result2.winner_name)
        self.assertEqual(result1.scores, result2.scores)

    def test_multiple_games_run_without_error(self):
        for seed in range(50):
            self._make_game(seed).play()


# ── Simulation smoke test ──────────────────────────────────────────────────────

class TestSimulation(unittest.TestCase):

    def test_simulation_runs_100_games(self):
        from src.simulation import Simulation
        rng = random.Random(0)
        sim = Simulation(
            RandomStrategy(random.Random(1)),
            RandomStrategy(random.Random(2)),
            n_games=100,
            seed=42,
        )
        stats = sim.run()
        self.assertEqual(stats.n_games, 100)
        self.assertEqual(sum(stats.wins.values()), 100)

    def test_win_rates_roughly_equal(self):
        from src.simulation import Simulation
        sim = Simulation(
            RandomStrategy(random.Random(1)),
            RandomStrategy(random.Random(2)),
            n_games=2000,
            seed=42,
        )
        stats = sim.run()
        rate_a = stats.win_rates["Team A"]
        # With 2000 games, random vs random should be between 40%–60%
        self.assertGreater(rate_a, 0.40)
        self.assertLess(rate_a, 0.60)


if __name__ == "__main__":
    unittest.main()
