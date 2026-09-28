"""
Team mats — the fixed 6-seat team identities picked for a run.

A mat is the "board" a team plays on: one seat per on-court role, each seat
holding one player and (optionally) one passive ability. Some seats are
intentionally blank — a mat doesn't need every seat filled to be complete.
Mats persist for the length of a run; they are not meant to be edited card by
card (drafted ability slots and deck upgrades, layered on top later, are for
that).

Today's pickable mats (Blitz, Grind, Spread, Backline) are exactly the
real, hand-using, standard-deck rows in data/teams.csv — as opposed to the
dummy AI opponents (blind draws, tuned-difficulty decks) or the shelved
draft-mode rosters.

This module composes the existing CSV-loading building blocks
(`runtime_config.resolve_team_runtime_config`, `abilities.load_roster`) into
one explicit object, rather than introducing a new data format.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional

from .abilities import PlayerCard, load_player_cards, load_roster
from .players import PlayerRole, SetTemplate
from .runtime_config import TeamRuntimeConfig, load_team_configs, resolve_team_runtime_config

# Seat order matches on-court position 1-6 (see players.Team.__init__).
SEAT_ORDER: List[PlayerRole] = [
    PlayerRole.SETTER, PlayerRole.OPP, PlayerRole.MB,
    PlayerRole.OH, PlayerRole.DS, PlayerRole.LIBERO,
]

DEFAULT_TEAMS_CSV = Path("data/teams.csv")
DEFAULT_PLAYER_CARDS_CSV = Path("data/player_cards.csv")
DEFAULT_PASSIVES_CSV = Path("data/team_passives.csv")
DEFAULT_SET_TEMPLATES_CSV = Path("data/set_templates.csv")


@dataclass(frozen=True)
class Mat:
    """One fixed team identity: 6 seats, each with a player and at most one ability."""
    name: str
    seats: Dict[PlayerRole, PlayerCard]
    passive_ability: Optional[str]
    deck_type: str
    use_hand: bool
    setter_templates: Dict[int, SetTemplate]
    broken_play_templates: Dict[int, SetTemplate]

    def seat(self, role: PlayerRole) -> PlayerCard:
        return self.seats[role]

    def describe(self) -> str:
        """Human-readable summary: one line per seat, plus its ability if any."""
        header = f"{self.name} mat"
        if self.passive_ability:
            header += f"  (team passive: {self.passive_ability})"
        lines = [header]
        for role in SEAT_ORDER:
            card = self.seats.get(role)
            if card is None:
                lines.append(f"  {role.value:8s} —")
                continue
            if not card.abilities:
                lines.append(f"  {role.value:8s} {card.player_name:10s} (no ability)")
                continue
            for a in card.abilities:
                cond = f"{a.condition_field}{a.condition_value}" if a.condition_field else "always"
                lines.append(
                    f"  {role.value:8s} {card.player_name:10s} {a.ability_name}"
                    f"  [{a.trigger}, {cond} -> {a.effect}={a.effect_value}]"
                )
        return "\n".join(lines)


def _is_pickable_mat(cfg: TeamRuntimeConfig) -> bool:
    """A mat is a real, hand-using team on the standard deck (not a dummy AI opponent)."""
    return cfg.use_hand and cfg.deck_type == "standard"


def _build_mat(cfg: TeamRuntimeConfig, player_cards: Dict[str, PlayerCard]) -> Mat:
    if cfg.roster_path is None or not cfg.roster_path.exists():
        raise FileNotFoundError(f"No roster file found for mat '{cfg.team_name}'")
    seats = load_roster(cfg.roster_path, player_cards)
    return Mat(
        name=cfg.team_name,
        seats=seats,
        passive_ability=cfg.passive_ability,
        deck_type=cfg.deck_type,
        use_hand=cfg.use_hand,
        setter_templates=cfg.setter_templates,
        broken_play_templates=cfg.broken_play_templates,
    )


def load_mat(
    team_name: str,
    teams_csv: Path = DEFAULT_TEAMS_CSV,
    player_cards_csv: Path = DEFAULT_PLAYER_CARDS_CSV,
    passives_csv: Path = DEFAULT_PASSIVES_CSV,
    set_templates_csv: Path = DEFAULT_SET_TEMPLATES_CSV,
) -> Mat:
    """Load one mat by team name (e.g. 'Blitz')."""
    cfg = resolve_team_runtime_config(
        roster_path=None,
        team_name=team_name,
        teams_csv=teams_csv,
        passives_csv=passives_csv,
        set_templates_csv=set_templates_csv,
    )
    player_cards = load_player_cards(player_cards_csv)
    return _build_mat(cfg, player_cards)


def load_all_mats(
    teams_csv: Path = DEFAULT_TEAMS_CSV,
    player_cards_csv: Path = DEFAULT_PLAYER_CARDS_CSV,
    passives_csv: Path = DEFAULT_PASSIVES_CSV,
    set_templates_csv: Path = DEFAULT_SET_TEMPLATES_CSV,
) -> List[Mat]:
    """Load every pickable preset mat (today: Blitz, Grind, Spread, Backline)."""
    team_configs = load_team_configs(teams_csv, passives_csv)
    player_cards = load_player_cards(player_cards_csv)
    mats: List[Mat] = []
    for cfg_raw in team_configs.values():
        cfg = resolve_team_runtime_config(
            roster_path=None,
            team_name=cfg_raw.team_name,
            teams_csv=teams_csv,
            passives_csv=passives_csv,
            set_templates_csv=set_templates_csv,
        )
        if _is_pickable_mat(cfg):
            mats.append(_build_mat(cfg, player_cards))
    return mats
