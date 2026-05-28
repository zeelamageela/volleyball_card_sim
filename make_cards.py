#!/usr/bin/env python3
"""Generate printable ability cards (9 per 8.5x11 page) from player_cards.csv.

Usage:
    python make_cards.py                         # defaults
    python make_cards.py --output my_cards.pdf   # custom output
    python make_cards.py --teams Blitz Grind     # only selected teams
"""

import csv
import hashlib
import json
import os
import argparse
from collections import defaultdict
from typing import Dict, Optional, List, Tuple

from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_CENTER

# ── Team display names (file stem → label) ──────────────────────────────────
ROSTER_MAP = {
    "team_blitz":        "Blitz",
    "team_grind":        "Grind",
    "team_dummy_easy":   "Easy",
    "team_dummy_medium": "Medium",
    "team_dummy_hard":   "Hard",
}

# ── Greyscale header fills (B&W print-safe; darker = harder tier) ───────────
TEAM_COLORS = {
    "Blitz":   colors.HexColor("#BBBBBB"),  # medium grey
    "Grind":   colors.HexColor("#D0D0D0"),  # light-medium grey
    "Easy":    colors.HexColor("#EEEEEE"),  # very light grey
    "Medium":  colors.HexColor("#CCCCCC"),  # light grey
    "Hard":    colors.HexColor("#999999"),  # dark grey
    "Unknown": colors.HexColor("#E8E8E8"),  # neutral
}

# ── Deck reference (for dedicated reference card) ───────────────────────────
PLAYER_DECK_COUNTS = "1x1 2x2 3x3 4x4 5x4 6x4 7x4 8x3 9x2 10x1"
DUMMY_DECK_COUNTS = "1x2 2x2 3x2 4x3 5x2 6x2 7x4 8x5 9x4 10x2"
PLAYER_DECK_SPLIT = "Low 36% · Mid 29% · High 36%"
DUMMY_DECK_SPLIT = "Low 32% · Mid 14% · High 54%"

# ── Role abbreviation labels ─────────────────────────────────────────────────
ROLE_LABEL = {
    "Setter": "Setter",
    "OPP":    "Opposite",
    "MB":     "Middle Blocker",
    "OH":     "Outside Hitter",
    "DS":     "Defensive Specialist",
    "Libero": "Libero",
}


def load_team_assignments(data_dir: str):
    """Return {player_name: team_display_name} by reading all known roster CSVs."""
    assignments = {}
    for stem, display in ROSTER_MAP.items():
        path = os.path.join(data_dir, f"{stem}.csv")
        if os.path.exists(path):
            with open(path, newline="", encoding="utf-8") as fh:
                for row in csv.DictReader(fh):
                    assignments[row["player_name"]] = display
    return assignments


def _as_bool(value: str, default: bool = True) -> bool:
    v = (value or "").strip().lower()
    if v in {"true", "1", "yes", "y"}:
        return True
    if v in {"false", "0", "no", "n"}:
        return False
    return default


def _is_blank_lane(cell: str) -> bool:
    token = (cell or "").strip().lower()
    return token in {"", "-", "—", "none", "n/a"}


def _role_tokens(cell: str) -> List[str]:
    raw = (cell or "").replace("·", "/")
    tokens = [t.strip().upper() for t in raw.split("/") if t.strip()]
    out: List[str] = []
    for token in tokens:
        if token in {"OH", "MB", "OPP"} and token not in out:
            out.append(token)
    return out


def _card_range_key(card_range: str) -> int:
    spec = (card_range or "").strip()
    if "-" in spec:
        left = spec.split("-", 1)[0].strip()
        return int(left or 999)
    return int(spec or 999)


def _clean_passive_name(name: str) -> Optional[str]:
    token = (name or "").strip()
    if not token:
        return None
    if token.lower() in {"tbd", "none", "null", "-", "—"}:
        return None
    return token


def load_template_bundles(set_templates_csv: str) -> Dict[str, Dict[str, List[Tuple[str, str, str, str, str, str]]]]:
    bundles: Dict[str, Dict[str, List[Tuple[str, str, str, str, str, str]]]] = {}
    with open(set_templates_csv, newline="", encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            template_name = (row.get("template_name") or "").strip()
            set_type = (row.get("set_type") or "").strip().lower()
            if not template_name or set_type not in {"normal", "broken"}:
                continue

            role_order = ["OH", "MB", "OPP"]
            front_roles: List[str] = []
            back_roles: List[str] = []

            for lane in (1, 2, 3):
                front_cell = row.get(f"lane{lane}_front", "")
                if not _is_blank_lane(front_cell):
                    for role in _role_tokens(front_cell):
                        if role not in front_roles:
                            front_roles.append(role)

                back_cell = row.get(f"lane{lane}_back", "")
                if not _is_blank_lane(back_cell):
                    for role in _role_tokens(back_cell):
                        if role not in back_roles:
                            back_roles.append(role)

            front = " · ".join([r for r in role_order if r in front_roles]) or "—"
            back = "/".join([r for r in role_order if r in back_roles]) or "—"

            set_name = (row.get("set_name") or template_name).strip()
            card_range = (row.get("card_range") or "").strip()
            quick_to = (row.get("quickset_to") or "").strip() or "—"
            max_hitters = (row.get("max_hitters") or "1").strip()

            entry = (set_name, card_range, quick_to, front, back, max_hitters)
            bundle = bundles.setdefault(template_name, {"normal": [], "broken": []})
            bundle[set_type].append(entry)

    for bundle in bundles.values():
        bundle["normal"].sort(key=lambda item: _card_range_key(item[1]))
        bundle["broken"].sort(key=lambda item: _card_range_key(item[1]))

    return bundles


def load_team_passive_texts(passives_csv: str) -> Dict[str, str]:
    passive_by_team: Dict[str, str] = {}
    if not os.path.exists(passives_csv):
        return passive_by_team

    with open(passives_csv, newline="", encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            team = (row.get("team_name") or "").strip()
            if not team:
                continue
            if not _as_bool(row.get("is_active", "false"), default=False):
                continue
            passive_name = _clean_passive_name(row.get("passive_name", ""))
            if not passive_name:
                continue
            desc = (row.get("passive_description") or "").strip()
            passive_by_team[team] = f"{passive_name}: {desc}" if desc else passive_name

    return passive_by_team


def load_team_card_data(data_dir: str) -> Dict[str, dict]:
    teams_csv = os.path.join(data_dir, "teams.csv")
    set_templates_csv = os.path.join(data_dir, "set_templates.csv")
    passives_csv = os.path.join(data_dir, "team_passives.csv")

    templates_by_name = load_template_bundles(set_templates_csv)
    passives_by_team = load_team_passive_texts(passives_csv)

    team_cards: Dict[str, dict] = {}
    with open(teams_csv, newline="", encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            team_name = (row.get("team_name") or "").strip()
            if not team_name:
                continue

            template_name = (row.get("set_template") or team_name).strip()
            template = templates_by_name.get(template_name)
            if template is None:
                template = {"normal": [], "broken": []}

            passive_name = _clean_passive_name(row.get("passive_ability", ""))
            passive_text = passives_by_team.get(team_name)
            if passive_name and not passive_text:
                passive_text = passive_name

            team_cards[team_name] = {
                "template_name": template_name,
                "template": template,
                "passive_text": passive_text,
            }

    return team_cards


def _stable_player_signature(card: dict) -> str:
    payload = {
        "player_name": card["player_name"],
        "role": card["role"],
        "team": card["team"],
        "abilities": card["abilities"],
    }
    blob = json.dumps(payload, sort_keys=True, ensure_ascii=True)
    return hashlib.sha256(blob.encode("utf-8")).hexdigest()


def _stable_team_signature(team_name: str, team_info: dict) -> str:
    payload = {
        "team_name": team_name,
        "template_name": team_info.get("template_name"),
        "template": team_info.get("template"),
        "passive_text": team_info.get("passive_text"),
    }
    blob = json.dumps(payload, sort_keys=True, ensure_ascii=True)
    return hashlib.sha256(blob.encode("utf-8")).hexdigest()


def load_print_cache(cache_file: str) -> Dict[str, Dict[str, str]]:
    if not os.path.exists(cache_file):
        return {"players": {}, "teams": {}}
    try:
        with open(cache_file, "r", encoding="utf-8") as fh:
            payload = json.load(fh)
        players = payload.get("players", {})
        teams = payload.get("teams", {})
        if isinstance(players, dict) and isinstance(teams, dict):
            return {"players": players, "teams": teams}
    except (OSError, json.JSONDecodeError):
        pass
    return {"players": {}, "teams": {}}


def save_print_cache(cache_file: str, player_sigs: Dict[str, str], team_sigs: Dict[str, str]) -> None:
    payload = {"players": player_sigs, "teams": team_sigs}
    cache_dir = os.path.dirname(cache_file)
    if cache_dir:
        os.makedirs(cache_dir, exist_ok=True)
    with open(cache_file, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2, sort_keys=True)


def draw_card(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
    player_name: str, role: str, abilities: List[dict],
    team: str,
) -> None:
    """Draw one card at lower-left corner (x, y) with width w and height h.
    
    abilities: list of dicts with 'ability_name' and 'description' keys
    """
    bg = TEAM_COLORS.get(team, TEAM_COLORS["Unknown"])
    role_full = ROLE_LABEL.get(role, role)

    # ── Card border ──────────────────────────────────────────────────────────
    c.setLineWidth(1.2)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    # ── Coloured header band (top 34% of card) ───────────────────────────────
    header_h = h * 0.34

    # ── Team tag (small, top-right corner) ──────────────────────────────────
    c.setFillColor(colors.black)
    c.setFont("Helvetica", 6.5)
    c.drawRightString(x + w - 6, y + h - 9, team.upper())

    # ── Role (italic, centered, just below top) ───────────────────────────────
    c.setFont("Helvetica-Oblique", 7.5)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, y + h - 19, role_full)

    # ── Player name (large bold, centered) ───────────────────────────────────
    c.setFont("Helvetica-Bold", 17)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, y + h - 38, player_name)

    # ── Divider between header and body ─────────────────────────────────────
    c.setLineWidth(0.8)
    c.setStrokeColor(colors.black)
    c.line(x + 10, y + h - header_h + 2, x + w - 10, y + h - header_h + 2)

    # ── Abilities (listed with bullet points) ────────────────────────────────
    body_top = y + h - header_h - 10
    body_h_avail = body_top - y - 6
    
    if not abilities:
        # No abilities - show placeholder text
        c.setFont("Helvetica-Oblique", 8)
        c.setFillColor(colors.HexColor("#666666"))
        c.drawCentredString(x + w / 2, y + h / 2 - 5, "No special abilities")
    else:
        # Build abilities text with bullet points
        abilities_text = []
        for ability in abilities:
            abilities_text.append(f"<b>• {ability['ability_name']}:</b> {ability['description']}")
        
        combined_text = "<br/>".join(abilities_text)

        style = ParagraphStyle(
            "abilities",
            fontName="Helvetica",
            fontSize=7.5,
            leading=10,
            alignment=TA_CENTER,
            textColor=colors.black,
        )
        para = Paragraph(combined_text, style)
        _, ph = para.wrap(w - 16, body_h_avail)
        # Vertically centre the abilities block
        para.drawOn(c, x + 8, y + 4 + max(0, (body_h_avail - ph) / 2))


def draw_set_template_card(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
    team_name: str,
    template: Dict[str, List[Tuple[str, str, str, str, str, str]]],
    passive_text: Optional[str],
) -> None:
    """Draw a set-template reference card showing normal and broken-play set rules."""
    
    # ── Card border ──────────────────────────────────────────────────────────
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # ── Team name ─────────────────────────────────────────────────────────────
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, team_name)
    cur_y -= 12

    # ── "SET TEMPLATE" subtitle ───────────────────────────────────────────────
    c.setFont("Helvetica", 7)
    c.drawCentredString(x + w / 2, cur_y, "SET TEMPLATE")
    cur_y -= 7

    # ── Full-width divider ────────────────────────────────────────────────────
    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 10

    # ── Helper to draw a section ──────────────────────────────────────────────
    def _lane_map_from_front(front: str) -> tuple[str, str, str]:
        lane_roles = {"OH": "—", "MB": "—", "OPP": "—"}
        if front and front != "—":
            tokens = [t.strip() for t in front.split("·")]
            for t in tokens:
                if t in lane_roles:
                    lane_roles[t] = t
        return lane_roles["OH"], lane_roles["MB"], lane_roles["OPP"]

    def _lane_map_from_back(back: str) -> tuple[str, str, str]:
        lane_roles = {"OH": "—", "MB": "—", "OPP": "—"}
        if not back or back == "—":
            return lane_roles["OH"], lane_roles["MB"], lane_roles["OPP"]
        value = back.replace(" only", "")
        tokens = [t.strip() for t in value.split("/")]
        for t in tokens:
            if t in lane_roles:
                lane_roles[t] = t
        return lane_roles["OH"], lane_roles["MB"], lane_roles["OPP"]

    def section(title: str, rows: list) -> None:
        nonlocal cur_y
        c.setFont("Helvetica-Bold", 7.5)
        c.drawCentredString(x + w / 2, cur_y, title)
        cur_y -= 9

        col_x = [x + 8, x + 56, x + 70, x + 84, x + 100, x + 114, x + 128, x + 142, x + 156]
        c.setFont("Helvetica-Oblique", 5.2)
        for label, cx in zip(["Set", "L1F", "L2F", "L3F", "L1B", "L2B", "L3B", "Q", "Max"], col_x):
            c.drawString(cx, cur_y, label)
        cur_y -= 8

        c.setLineWidth(0.4)
        c.setStrokeColor(colors.HexColor("#666666"))
        c.line(x + 8, cur_y + 1, x + w - 8, cur_y + 1)
        c.setStrokeColor(colors.black)
        cur_y -= 1

        c.setFont("Helvetica", 4.8)
        for set_name, card_range, quick_to, front, back, mx in rows:
            set_label = f"{set_name} ({card_range})"
            l1f, l2f, l3f = _lane_map_from_front(front)
            l1b, l2b, l3b = _lane_map_from_back(back)
            c.drawString(col_x[0], cur_y, set_label)
            c.drawString(col_x[1], cur_y, l1f)
            c.drawString(col_x[2], cur_y, l2f)
            c.drawString(col_x[3], cur_y, l3f)
            c.drawString(col_x[4], cur_y, l1b)
            c.drawString(col_x[5], cur_y, l2b)
            c.drawString(col_x[6], cur_y, l3b)
            c.drawString(col_x[7], cur_y, quick_to)
            c.drawString(col_x[8], cur_y, mx)
            cur_y -= 9

    # ── Normal set table ──────────────────────────────────────────────────────
    section("NORMAL SET  (setter sets)", template["normal"])

    cur_y -= 6
    c.setLineWidth(0.6)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 14

    # ── Broken-play set table ─────────────────────────────────────────────────
    section("BROKEN PLAY  (non-setter sets)", template["broken"])

    cur_y -= 6
    c.setLineWidth(0.6)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 12

    # ── Team passive ability ──────────────────────────────────────────────────
    passive = passive_text
    if passive:
        c.setFont("Helvetica-Bold", 7.5)
        c.drawCentredString(x + w / 2, cur_y, "PASSIVE ABILITY")
        cur_y -= 11
        
        # Wrap text if needed
        style = ParagraphStyle(
            "passive",
            fontName="Helvetica",
            fontSize=6.8,
            leading=8.5,
            alignment=TA_CENTER,
            textColor=colors.black,
        )
        para = Paragraph(passive, style)
        _, ph = para.wrap(w - 20, 30)
        para.drawOn(c, x + 10, cur_y - ph)
        cur_y -= ph + 6

    # ── Lane key footer ───────────────────────────────────────────────────────
    c.setFont("Helvetica", 6.2)
    c.drawCentredString(x + w / 2, cur_y, "Lane 1 = OH   ·   Lane 2 = MB   ·   Lane 3 = OPP")
    cur_y -= 8
    c.setFont("Helvetica-Oblique", 6)
    c.drawCentredString(x + w / 2, cur_y, "Broken play: setter dug ball this rally")


def draw_deck_reference(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
) -> None:
    """Draw deck composition reference card."""
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # Title
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, "DECK MAKEUP")
    cur_y -= 10

    c.setFont("Helvetica", 7)
    c.drawCentredString(x + w / 2, cur_y, "Player vs Dummy card distribution")
    cur_y -= 10

    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 13

    # Player deck section
    c.setFont("Helvetica-Bold", 8.2)
    c.drawString(x + 10, cur_y, "Player Deck (standard):")
    cur_y -= 10
    c.setFont("Helvetica", 7.0)
    c.drawString(x + 12, cur_y, PLAYER_DECK_COUNTS)
    cur_y -= 10
    c.drawString(x + 12, cur_y, PLAYER_DECK_SPLIT)
    cur_y -= 14

    # Dummy deck section
    c.setFont("Helvetica-Bold", 8.2)
    c.drawString(x + 10, cur_y, "Dummy Deck (blind-play):")
    cur_y -= 10
    c.setFont("Helvetica", 7.0)
    c.drawString(x + 12, cur_y, DUMMY_DECK_COUNTS)
    cur_y -= 10
    c.drawString(x + 12, cur_y, DUMMY_DECK_SPLIT)
    cur_y -= 14

    # Usage note
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Why different decks?")
    cur_y -= 10
    c.setFont("Helvetica", 7.2)
    c.drawString(x + 12, cur_y, "• Dummy teams do blind flips (no hand selection)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Extra high cards keep dummy teams competitive")


def draw_quick_set_reference(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
) -> None:
    """Draw quick set mechanics reference card."""
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # Title
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, "QUICK SET RULES")
    cur_y -= 10

    c.setFont("Helvetica", 7)
    c.drawCentredString(x + w / 2, cur_y, "Set card 1-3 only")
    cur_y -= 10

    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 13

    # Quick lanes
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Quick Lanes:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Lane 1 (OH)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Lane 2 (MB)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Lane 3 (OPP)")
    cur_y -= 14

    # Single blocker rule
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Single Blocker Rule:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Only 1 blocker per quick lane")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Must blind draw from deck")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Cannot choose from hand")
    cur_y -= 14

    # No chase rule
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "No Chase Rule:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Failed quick set dig = point")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• No chase attempt allowed")
    cur_y -= 14

    # Advantage
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Tactical Advantage:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "Efficient, high-pressure option")


def draw_attack_types_reference(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
) -> None:
    """Draw attack types and chase rules reference card."""
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # Title
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, "ATTACK TYPES")
    cur_y -= 17

    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 13

    # Hit
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "HIT:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Standard power attack")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Chase allowed on failed dig")
    cur_y -= 14

    # Tip
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "TIP:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Soft attack (card ≤ tip threshold)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• No chase on failed dig")
    cur_y -= 14

    # Roll Shot
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "ROLL SHOT:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Goes over block (block ignored)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Dug like tip (uses tip threshold)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Chase allowed on failed dig")
    cur_y -= 14

    # Heavy Spin
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "HEAVY SPIN:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Bypasses block (block ignored)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• No chase on failed dig")
    cur_y -= 14

    # Seam Shot
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "SEAM SHOT:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Deflection = instant point")


def draw_blocking_reference(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
) -> None:
    """Draw blocking rules reference card."""
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # Title
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, "BLOCKING RULES")
    cur_y -= 17

    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 13

    # Attack resolution
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Attack Resolution:")
    cur_y -= 10
    c.setFont("Helvetica", 7)
    c.drawString(x + 12, cur_y, "Differential = Attack - Block")
    cur_y -= 12

    c.drawString(x + 12, cur_y, "• ≤ -1: STUFFED (blocker wins)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "•  0-2: DEFLECT (blocker side)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "•  3-4: DEFLECT (attacker side)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "•  5+:  KILL (attack vs dig)")
    cur_y -= 14

    # Quick set blocking
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Quick Set Blocking:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• 1 blocker only (blind draw)")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Cannot choose from hand")
    cur_y -= 14

    # Normal blocking
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Normal Blocking:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Choose cards from hand")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Up to max blockers per lane")
    cur_y -= 14

    # Odd/Even logic
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Attack Card Parity:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• ODD attack → block ODD cards")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• EVEN attack → block EVEN cards")


def draw_chase_reference(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
) -> None:
    """Draw chase rules reference card."""
    c.setLineWidth(1.8)
    c.setStrokeColor(colors.black)
    c.roundRect(x, y, w, h, radius=8, stroke=1, fill=0)

    cur_y = y + h - 10

    # Title
    c.setFont("Helvetica-Bold", 13)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, cur_y, "CHASE RULES")
    cur_y -= 17

    c.setLineWidth(0.8)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 13

    # When chase happens
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Chase Triggered When:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• Failed dig on HIT or ROLL SHOT")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Not triggered on failed tip")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Not triggered on quick sets")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• Not triggered on heavy spin")
    cur_y -= 14

    # Two attempts
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Two Chase Attempts:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "1. Adjacent player chase")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "2. Setter/Libero chase")
    cur_y -= 14

    # Chase outcomes
    c.setFont("Helvetica-Bold", 8)
    c.drawString(x + 10, cur_y, "Chase Outcomes:")
    cur_y -= 10
    c.setFont("Helvetica", 7.5)
    c.drawString(x + 12, cur_y, "• ARMED: Dig ≥ target")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "   → Chase team attacks")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• FREE BALL: Armed requirements")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "   not met → continue rally")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "• FAILED: Total < target")
    cur_y -= 9
    c.drawString(x + 12, cur_y, "   → Attacker wins point")


def make_pdf(
    cards: List[dict],
    output_path: str,
    teams: Optional[List[str]] = None,
    team_card_data: Optional[Dict[str, dict]] = None,
    include_reference_cards: bool = True,
) -> None:
    PAGE_W, PAGE_H = letter   # 612 × 792 points
    MARGIN = 0.45 * inch      # ~32 pt
    GUTTER = 7                # points between cards
    COLS, ROWS = 3, 3

    card_w = (PAGE_W - 2 * MARGIN - (COLS - 1) * GUTTER) / COLS
    card_h = (PAGE_H - 2 * MARGIN - (ROWS - 1) * GUTTER) / ROWS

    c = canvas.Canvas(output_path, pagesize=letter)

    for i, card in enumerate(cards):
        slot = i % (COLS * ROWS)
        if slot == 0 and i > 0:
            c.showPage()

        col = slot % COLS
        row = slot // COLS

        cx = MARGIN + col * (card_w + GUTTER)
        cy = PAGE_H - MARGIN - (row + 1) * card_h - row * GUTTER

        draw_card(
            c, cx, cy, card_w, card_h,
            card["player_name"], card["role"],
            card["abilities"],
            card["team"],
        )

    # ── Team template cards (one per team, appended after ability cards) ─────
    team_data = team_card_data or {}
    team_order = [t for t in ROSTER_MAP.values() if (teams is None or t in teams) and t in team_data]
    total_ability = len(cards)
    for j, team_name in enumerate(team_order):
        i = total_ability + j
        slot = i % (COLS * ROWS)
        if slot == 0:
            c.showPage()
        col = slot % COLS
        row = slot // COLS
        cx = MARGIN + col * (card_w + GUTTER)
        cy = PAGE_H - MARGIN - (row + 1) * card_h - row * GUTTER
        draw_set_template_card(
            c,
            cx,
            cy,
            card_w,
            card_h,
            team_name,
            team_data[team_name]["template"],
            team_data[team_name].get("passive_text"),
        )

    # ── Reference cards (rules + deck makeup) ─────────────────────────────────
    reference_cards = []
    if include_reference_cards:
        reference_cards = [
            ("Quick Set Rules", draw_quick_set_reference),
            ("Attack Types", draw_attack_types_reference),
            ("Blocking Rules", draw_blocking_reference),
            ("Chase Rules", draw_chase_reference),
            ("Deck Makeup", draw_deck_reference),
        ]
    
    total_team_cards = len(team_order)
    for j, (ref_name, draw_func) in enumerate(reference_cards):
        i = total_ability + total_team_cards + j
        slot = i % (COLS * ROWS)
        if slot == 0:
            c.showPage()
        col = slot % COLS
        row = slot // COLS
        cx = MARGIN + col * (card_w + GUTTER)
        cy = PAGE_H - MARGIN - (row + 1) * card_h - row * GUTTER
        draw_func(c, cx, cy, card_w, card_h)

    c.save()
    total_cards = total_ability + total_team_cards + len(reference_cards)
    pages = (total_cards + COLS * ROWS - 1) // (COLS * ROWS)
    print(f"Wrote {total_ability} player cards + {total_team_cards} team cards + {len(reference_cards)} reference cards across {pages} page(s) -> {output_path}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate ability cards PDF (9 per page)")
    parser.add_argument("--input", default="data/player_cards.csv", help="Player cards CSV")
    parser.add_argument("--output", default="ability_cards.pdf", help="Output PDF filename")
    parser.add_argument("--data-dir", default="data", help="Directory containing roster CSVs")
    parser.add_argument(
        "--changed-only",
        action="store_true",
        help="Only include cards/templates changed since last cached print run",
    )
    parser.add_argument(
        "--cache-file",
        default="data/print_cache.json",
        help="Cache file used by --changed-only mode",
    )
    parser.add_argument(
        "--no-reference-cards",
        action="store_true",
        help="Do not append rules/deck reference cards",
    )
    parser.add_argument(
        "--teams", nargs="*",
        choices=list(ROSTER_MAP.values()),
        help="Only include cards for these teams (default: all)",
    )
    args = parser.parse_args()

    team_cards = load_team_card_data(args.data_dir)

    # First, load ALL players from roster files
    players = defaultdict(lambda: {"role": None, "team": None, "abilities": []})
    
    # Load all players from rosters
    for stem, display in ROSTER_MAP.items():
        if args.teams and display not in args.teams:
            continue
        path = os.path.join(args.data_dir, f"{stem}.csv")
        if os.path.exists(path):
            with open(path, newline="", encoding="utf-8") as fh:
                for row in csv.DictReader(fh):
                    player_name = row["player_name"]
                    players[player_name]["role"] = row["role"]
                    players[player_name]["team"] = display
                    players[player_name]["print_card"] = True
    
    # Then overlay abilities from player_cards.csv
    with open(args.input, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            player_name = row["player_name"]
            if player_name in players:
                if "print_card" in row and row.get("print_card", "").strip() != "":
                    players[player_name]["print_card"] = _as_bool(row.get("print_card", ""), default=True)
                if "skip_print" in row and row.get("skip_print", "").strip() != "":
                    if _as_bool(row.get("skip_print", ""), default=False):
                        players[player_name]["print_card"] = False

                ability_name = (row.get("ability_name") or "").strip()
                if ability_name:
                    players[player_name]["abilities"].append({
                        "ability_name": ability_name,
                        "description": row.get("description", "").strip(),
                    })
    
    # Convert to list of cards (one per player)
    cards = []
    for player_name, data in players.items():
        if not data.get("print_card", True):
            continue
        cards.append({
            "player_name": player_name,
            "role": data["role"],
            "abilities": data["abilities"],
            "team": data["team"],
        })

    player_sigs = {card["player_name"]: _stable_player_signature(card) for card in cards}
    team_sigs = {name: _stable_team_signature(name, info) for name, info in team_cards.items()}

    selected_teams = [t for t in ROSTER_MAP.values() if args.teams is None or t in args.teams]
    selected_team_data = {t: team_cards[t] for t in selected_teams if t in team_cards}

    if args.changed_only:
        cache = load_print_cache(args.cache_file)
        old_player_sigs = cache.get("players", {})
        old_team_sigs = cache.get("teams", {})

        cards = [
            card for card in cards
            if player_sigs.get(card["player_name"]) != old_player_sigs.get(card["player_name"])
        ]
        selected_team_data = {
            team_name: info
            for team_name, info in selected_team_data.items()
            if team_sigs.get(team_name) != old_team_sigs.get(team_name)
        }

        if not cards and not selected_team_data:
            print("No changed player/team cards detected; nothing to print.")
            return

    if not cards:
        print("No player cards matched print filter; generating only team/reference cards.")

    make_pdf(
        cards,
        args.output,
        teams=list(selected_team_data.keys()),
        team_card_data=selected_team_data,
        include_reference_cards=not args.no_reference_cards,
    )

    if args.changed_only:
        save_print_cache(args.cache_file, player_sigs, team_sigs)
        print(f"Updated print cache: {args.cache_file}")


if __name__ == "__main__":
    main()
