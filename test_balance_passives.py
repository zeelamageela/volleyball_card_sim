#!/usr/bin/env python3
"""Balance test with passives enabled vs disabled."""

import random
import sys
from src.players import Team
from src.game import Game
from src.strategies import DummyStrategy
from src.abilities import load_player_cards, build_ability_engine

player_cards = load_player_cards('data/player_cards.csv')

print("=" * 70)
print("BALANCE TEST: Dummy teams vs Blitz (100 games each)")
print("=" * 70)
print()

# Test configurations: (team_name, roster_file, passive_ability)
configs = [
    ('Easy', 'data/team_dummy_easy.csv', None),
    ('Easy', 'data/team_dummy_easy.csv', 'Safe Setter'),
    ('Medium', 'data/team_dummy_medium.csv', None),
    ('Medium', 'data/team_dummy_medium.csv', 'Back Court Threat'),
    ('Hard', 'data/team_dummy_hard.csv', None),
    ('Hard', 'data/team_dummy_hard.csv', 'Elite Draw'),
]

for team_name, roster_file, passive in configs:
    wins = 0
    games = 100
    for i in range(games):
        rng = random.Random(1000 + i)
        
        # Create Blitz team with abilities
        team_a = Team('Blitz', rng, use_hand=True, deck_type='standard')
        engine_a = build_ability_engine('data/team_blitz.csv', player_cards)
        engine_a.reset()
        team_a.ability_engine = engine_a
        
        # Create dummy team with abilities and passive
        team_b = Team(team_name, rng, use_hand=False, deck_type='dummy', passive_ability=passive)
        engine_b = build_ability_engine(roster_file, player_cards)
        engine_b.reset()
        team_b.ability_engine = engine_b
        
        game = Game(team_a, team_b, DummyStrategy(), DummyStrategy(), rng)
        result = game.play()
        if result.winner_name == team_name:
            wins += 1
    
    passive_str = f" ({passive})" if passive else " (no passive)"
    print(f"{team_name:8}{passive_str:30} {wins:3}/100 = {100*wins/games:5.1f}%")

print()
print("=" * 70)
