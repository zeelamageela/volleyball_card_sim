from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Optional, Tuple

from .players import (
    SetTemplate,
    SETTER_TEMPLATES,
    BROKEN_PLAY_TEMPLATES,
)


@dataclass(frozen=True)
class TeamCsvConfig:
    team_name: str
    roster_file: str
    set_template: str
    passive_ability: Optional[str]
    deck_type: str
    use_hand: bool


@dataclass(frozen=True)
class TeamRuntimeConfig:
    team_name: str
    roster_path: Optional[Path]
    set_template: str
    passive_ability: Optional[str]
    deck_type: str
    use_hand: bool
    setter_templates: Dict[int, SetTemplate]
    broken_play_templates: Dict[int, SetTemplate]


def _as_bool(value: str, default: bool = True) -> bool:
    v = (value or "").strip().lower()
    if v in {"true", "1", "yes", "y"}:
        return True
    if v in {"false", "0", "no", "n"}:
        return False
    return default


def _clean_passive(value: str) -> Optional[str]:
    v = (value or "").strip()
    if not v:
        return None
    if v.lower() in {"tbd", "none", "null", "-", "—"}:
        return None
    return v


def _parse_card_range(spec: str) -> Tuple[int, int]:
    raw = (spec or "").strip()
    if not raw:
        raise ValueError("empty card_range")
    if "-" in raw:
        a, b = raw.split("-", 1)
        lo = int(a.strip())
        hi = int(b.strip())
        if lo > hi:
            lo, hi = hi, lo
        return lo, hi
    val = int(raw)
    return val, val


def _lane_enabled(cell: str) -> bool:
    token = (cell or "").strip().lower()
    return token not in {"", "-", "—", "none", "n/a"}


def load_team_configs(
    teams_csv: Path,
    passives_csv: Optional[Path] = None,
) -> Dict[str, TeamCsvConfig]:
    passives_by_team: Dict[str, str] = {}
    if passives_csv and passives_csv.exists():
        with open(passives_csv, newline="", encoding="utf-8-sig") as f:
            for row in csv.DictReader(f):
                team = (row.get("team_name") or "").strip()
                passive = (row.get("passive_name") or "").strip()
                active = _as_bool(row.get("is_active", "false"), default=False)
                if team and passive and active:
                    passives_by_team[team.lower()] = passive

    teams: Dict[str, TeamCsvConfig] = {}
    with open(teams_csv, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            team_name = (row.get("team_name") or "").strip()
            roster_file = (row.get("roster_file") or "").strip()
            if not team_name:
                continue
            passive = _clean_passive(row.get("passive_ability", ""))
            if passive is None:
                passive = passives_by_team.get(team_name.lower())
            cfg = TeamCsvConfig(
                team_name=team_name,
                roster_file=roster_file,
                set_template=(row.get("set_template") or team_name).strip() or team_name,
                passive_ability=passive,
                deck_type=(row.get("deck_type") or "standard").strip() or "standard",
                use_hand=_as_bool(row.get("use_hand", "true"), default=True),
            )
            teams[team_name.lower()] = cfg
    return teams


def load_template_bundles(
    set_templates_csv: Path,
) -> Dict[str, Dict[str, Dict[int, SetTemplate]]]:
    bundles: Dict[str, Dict[str, Dict[int, SetTemplate]]] = {}
    with open(set_templates_csv, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            template_name = (row.get("template_name") or "").strip()
            set_type = (row.get("set_type") or "").strip().lower()
            if not template_name or set_type not in {"normal", "broken"}:
                continue

            lo, hi = _parse_card_range(row.get("card_range", ""))
            front_lanes = [
                lane for lane in (1, 2, 3)
                if _lane_enabled(row.get(f"lane{lane}_front", ""))
            ]
            back_lanes = [
                lane for lane in (1, 2, 3)
                if _lane_enabled(row.get(f"lane{lane}_back", ""))
            ]
            max_hitters = int((row.get("max_hitters") or "1").strip())

            bundle = bundles.setdefault(template_name.lower(), {"normal": {}, "broken": {}})
            for v in range(max(1, lo), min(10, hi) + 1):
                bundle[set_type][v] = SetTemplate(
                    front_lanes=front_lanes,
                    back_lanes=back_lanes,
                    max_attackers=max_hitters,
                )

    return bundles


def _clone_templates(src: Dict[int, SetTemplate]) -> Dict[int, SetTemplate]:
    return {
        k: SetTemplate(
            front_lanes=list(v.front_lanes),
            back_lanes=list(v.back_lanes),
            max_attackers=v.max_attackers,
        )
        for k, v in src.items()
    }


def resolve_team_runtime_config(
    roster_path: Optional[Path],
    team_name: Optional[str],
    teams_csv: Path = Path("data/teams.csv"),
    passives_csv: Path = Path("data/team_passives.csv"),
    set_templates_csv: Path = Path("data/set_templates.csv"),
) -> TeamRuntimeConfig:
    teams_by_name = load_team_configs(teams_csv, passives_csv)
    bundles = load_template_bundles(set_templates_csv)

    roster_lookup = None
    if roster_path is not None:
        roster_lookup = roster_path.name.lower()

    selected: Optional[TeamCsvConfig] = None
    if roster_lookup:
        for cfg in teams_by_name.values():
            if cfg.roster_file.strip().lower() == roster_lookup:
                selected = cfg
                break
    if selected is None and team_name:
        selected = teams_by_name.get(team_name.strip().lower())

    if selected is None:
        fallback_name = team_name or (roster_path.stem if roster_path else "Team")
        return TeamRuntimeConfig(
            team_name=fallback_name,
            roster_path=roster_path,
            set_template=fallback_name,
            passive_ability=None,
            deck_type="standard",
            use_hand=True,
            setter_templates=_clone_templates(SETTER_TEMPLATES),
            broken_play_templates=_clone_templates(BROKEN_PLAY_TEMPLATES),
        )

    data_dir = teams_csv.parent
    resolved_roster = roster_path
    if resolved_roster is None and selected.roster_file:
        resolved_roster = data_dir / selected.roster_file

    chosen_bundle = bundles.get(selected.set_template.lower(), None)
    normal = _clone_templates(SETTER_TEMPLATES)
    broken = _clone_templates(BROKEN_PLAY_TEMPLATES)
    if chosen_bundle:
        normal.update(chosen_bundle.get("normal", {}))
        broken.update(chosen_bundle.get("broken", {}))

    return TeamRuntimeConfig(
        team_name=selected.team_name,
        roster_path=resolved_roster,
        set_template=selected.set_template,
        passive_ability=selected.passive_ability,
        deck_type=selected.deck_type or "standard",
        use_hand=selected.use_hand,
        setter_templates=normal,
        broken_play_templates=broken,
    )
