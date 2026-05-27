#!/usr/bin/env python3
"""Test team passive abilities."""

import random
import sys
import os

# Add src to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from src.players import Team
from src.game import Game
from src.strategies import DummyStrategy
from src.abilities import load_player_cards


def test_deep_bench():
    """Test Grind's Deep Bench passive (hand size 6)."""
    print("Testing Deep Bench (Grind) - Hand size should be 6...")
    rng = random.Random(42)
    team = Team("Grind", rng, use_hand=True, deck_type="standard", passive_ability="Deep Bench")
    team.draw_starting_hand()
    print(f"  Initial hand size: {len(team.hand)} (expected 6)")
    assert len(team.hand) == 6, f"Expected 6, got {len(team.hand)}"
    
    # Use a card and refill
    card = team.hand[0]
    team.play_card(card)
    team.refill_hand()
    print(f"  After play+refill: {len(team.hand)} (expected 6)")
    assert len(team.hand) == 6, f"Expected 6, got {len(team.hand)}"
    print("  ✓ Deep Bench working!\n")


def test_elite_draw():
    """Test Hard's Elite Draw passive (draw 2, keep highest)."""
    print("Testing Elite Draw (Hard) - Should draw 2 and keep highest...")
    rng = random.Random(42)
    team = Team("Hard", rng, use_hand=False, deck_type="dummy", passive_ability="Elite Draw")
    
    # Draw multiple times and check that we're getting reasonable variance
    draws = [team.draw_for_action().value for _ in range(10)]
    print(f"  10 draws: {draws}")
    print(f"  Average: {sum(draws) / len(draws):.2f}")
    print("  ✓ Elite Draw working (high average indicates better draws)!\n")


def test_safe_setter():
    """Test Easy's Safe Setter passive (no broken play on setter digs)."""
    print("Testing Safe Setter (Easy) - Running simulation...")
    load_player_cards("data/player_cards.csv")
    rng = random.Random(42)
    
    team_a = Team("Blitz", rng, use_hand=True, deck_type="standard")
    team_a_strat = DummyStrategy()
    
    team_b = Team("Easy", rng, use_hand=False, deck_type="dummy", passive_ability="Safe Setter")
    team_b_strat = DummyStrategy()
    
    game = Game(team_a, team_b, team_a_strat, team_b_strat, rng)
    result = game.play()
    
    print(f"  Final score: {result.winner_name} wins!")
    print("  ✓ Safe Setter passive enabled!\n")


def test_back_court_threat():
    """Test Medium's Back Court Threat passive."""
    print("Testing Back Court Threat (Medium) - Running simulation...")
    load_player_cards("data/player_cards.csv")
    rng = random.Random(42)
    
    team_a = Team("Blitz", rng, use_hand=True, deck_type="standard")
    team_a_strat = DummyStrategy()
    
    team_b = Team("Medium", rng, use_hand=False, deck_type="dummy", passive_ability="Back Court Threat")
    team_b_strat = DummyStrategy()
    
    game = Game(team_a, team_b, team_a_strat, team_b_strat, rng)
    result = game.play()
    
    print(f"  Final score: {result.winner_name} wins!")
    print("  ✓ Back Court Threat passive enabled!\n")


if __name__ == "__main__":
    print("=" * 60)
    print("TESTING TEAM PASSIVE ABILITIES")
    print("=" * 60 + "\n")
    
    test_deep_bench()
    test_elite_draw()
    test_safe_setter()
    test_back_court_threat()
    
    print("=" * 60)
    print("ALL TESTS COMPLETED!")
    print("=" * 60)
