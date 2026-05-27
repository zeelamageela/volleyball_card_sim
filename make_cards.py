#!/usr/bin/env python3
"""Generate printable ability cards (9 per 8.5x11 page) from player_cards.csv.

Usage:
    python make_cards.py                         # defaults
    python make_cards.py --output my_cards.pdf   # custom output
    python make_cards.py --teams Blitz Grind     # only selected teams
"""

import csv
import os
import argparse
from typing import Optional, List

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

# ── Team-specific set templates ──────────────────────────────────────────────
TEAM_SET_TEMPLATES = {
    "Blitz": {
        "normal": [
            ("1-3",  "OH · MB",        "—",         "2"),
            ("4-5",  "OH · MB",        "OH/MB/OPP", "3"),
            ("6-7",  "MB · OPP",       "OH/MB/OPP", "3"),
            ("8-9",  "OH · OPP",       "OH/MB/OPP", "4"),
            ("10",   "OH · OPP",       "OH/MB/OPP", "4"),
        ],
        "broken": [
            ("1-3",  "OH",       "—",       "1"),
            ("4-7",  "OH · OPP", "MB only", "2"),
            ("8-10", "OH · OPP", "MB only", "2"),
        ],
    },
    "Grind": {
        "normal": [
            ("1-3",  "OH · MB · OPP",  "—",         "3"),
            ("4-5",  "OH · MB",        "OH/MB/OPP", "3"),
            ("6-7",  "MB · OPP",       "OH/MB/OPP", "3"),
            ("8-9",  "OH · OPP",       "OH/MB/OPP", "4"),
            ("10",   "OH · MB · OPP",  "OH/MB/OPP", "4"),
        ],
        "broken": [
            ("1-3",  "OH · MB",   "MB only", "2"),
            ("4-7",  "OH · OPP",  "MB only", "2"),
            ("8-10", "MB · OPP",  "MB only", "2"),
        ],
    },
    "Easy": {
        "normal": [
            ("1-3",  "OH · MB",   "—",    "2"),
            ("4-5",  "OH · MB",   "—",    "2"),
            ("6-7",  "MB · OPP",  "OPP",  "2"),
            ("8-9",  "OH · OPP",  "OPP",  "3"),
            ("10",   "OH · OPP",  "OH/OPP", "3"),
        ],
        "broken": [
            ("1-3",  "OH",     "—",       "1"),
            ("4-7",  "OH",     "—",       "1"),
            ("8-10", "OH · OPP", "—",     "2"),
        ],
    },
    "Medium": {
        "normal": [
            ("1-4",  "OH · MB · OPP",  "—",         "3"),
            ("5-6",  "OH · MB",        "OH/MB/OPP", "3"),
            ("7-8",  "MB · OPP",       "OH/MB/OPP", "3"),
            ("9-10", "OH · OPP",       "OH/MB/OPP", "4"),
        ],
        "broken": [
            ("1-4",  "OH · MB",   "MB only", "2"),
            ("5-7",  "OH · OPP",  "MB only", "2"),
            ("8-10", "MB · OPP",  "MB only", "2"),
        ],
    },
    "Hard": {
        "normal": [
            ("1-4",  "OH · MB · OPP",  "MB only",   "3"),
            ("5-6",  "OH · MB · OPP",  "OH/MB/OPP", "4"),
            ("7-8",  "MB · OPP",       "OH/MB/OPP", "4"),
            ("9-10", "OH · OPP",       "OH/MB/OPP", "5"),
        ],
        "broken": [
            ("1-4",  "OH · MB",   "MB only", "2"),
            ("5-7",  "OH · OPP",  "MB only", "2"),
            ("8-10", "MB · OPP",  "OH/MB/OPP", "3"),
        ],
    },
}

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
) -> None:
    """Draw a set-template reference card showing normal and broken-play set rules."""
    # Get team-specific template or default to Grind
    template = TEAM_SET_TEMPLATES.get(team_name, TEAM_SET_TEMPLATES["Grind"])
    
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
    def section(title: str, rows: list) -> None:
        nonlocal cur_y
        c.setFont("Helvetica-Bold", 7.5)
        c.drawCentredString(x + w / 2, cur_y, title)
        cur_y -= 9

        col_x = [x + 11, x + 44, x + 100, x + 138]
        c.setFont("Helvetica-Oblique", 6.2)
        for label, cx in zip(["Card", "Front lanes", "Back row", "Max"], col_x):
            c.drawString(cx, cur_y, label)
        cur_y -= 8

        c.setLineWidth(0.4)
        c.setStrokeColor(colors.HexColor("#666666"))
        c.line(x + 8, cur_y + 1, x + w - 8, cur_y + 1)
        c.setStrokeColor(colors.black)
        cur_y -= 1

        c.setFont("Helvetica", 6.8)
        for card_range, front, back, mx in rows:
            c.drawString(col_x[0], cur_y, card_range)
            c.drawString(col_x[1], cur_y, front)
            c.drawString(col_x[2], cur_y, back)
            c.drawString(col_x[3], cur_y, mx)
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

    # ── Lane key footer ───────────────────────────────────────────────────────
    c.setFont("Helvetica", 6.2)
    c.drawCentredString(x + w / 2, cur_y, "Lane 1 = OH   ·   Lane 2 = MB   ·   Lane 3 = OPP")
    cur_y -= 8
    c.setFont("Helvetica-Oblique", 6)
    c.drawCentredString(x + w / 2, cur_y, "Broken play: setter dug ball this rally")


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
    team_order = [t for t in ROSTER_MAP.values() if teams is None or t in teams]
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
        draw_set_template_card(c, cx, cy, card_w, card_h, team_name)

    # ── Reference cards (quick sets, attacks, blocking, chase) ────────────────
    reference_cards = [
        ("Quick Set Rules", draw_quick_set_reference),
        ("Attack Types", draw_attack_types_reference),
        ("Blocking Rules", draw_blocking_reference),
        ("Chase Rules", draw_chase_reference),
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
    print(f"Wrote {total_ability} player cards + {total_team_cards} team cards + {len(reference_cards)} reference cards across {pages} page(s) → {output_path}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate ability cards PDF (9 per page)")
    parser.add_argument("--input", default="data/player_cards.csv", help="Player cards CSV")
    parser.add_argument("--output", default="ability_cards.pdf", help="Output PDF filename")
    parser.add_argument("--data-dir", default="data", help="Directory containing roster CSVs")
    parser.add_argument(
        "--teams", nargs="*",
        choices=list(ROSTER_MAP.values()),
        help="Only include cards for these teams (default: all)",
    )
    args = parser.parse_args()

    team_map = load_team_assignments(args.data_dir)

    # Group abilities by player
    from collections import defaultdict
    players = defaultdict(lambda: {"role": None, "team": None, "abilities": []})
    
    with open(args.input, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            team = team_map.get(row["player_name"], "Unknown")
            if args.teams and team not in args.teams:
                continue
            
            player_name = row["player_name"]
            players[player_name]["role"] = row["role"]
            players[player_name]["team"] = team
            players[player_name]["abilities"].append({
                "ability_name": row["ability_name"],
                "description": row["description"],
            })
    
    # Convert to list of cards (one per player)
    cards = []
    for player_name, data in players.items():
        cards.append({
            "player_name": player_name,
            "role": data["role"],
            "abilities": data["abilities"],
            "team": data["team"],
        })

    if not cards:
        print("No cards matched — check your --teams filter.")
        return

    make_pdf(cards, args.output, teams=args.teams)


if __name__ == "__main__":
    main()
