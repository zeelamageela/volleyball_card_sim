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

# ── Role abbreviation labels ─────────────────────────────────────────────────
ROLE_LABEL = {
    "Setter": "Setter",
    "OPP":    "Opposite",
    "MB":     "Middle Blocker",
    "OH":     "Outside Hitter",
    "DS":     "Defensive Specialist",
    "Libero": "Libero",
}


def load_team_assignments(data_dir: str) -> dict[str, str]:
    """Return {player_name: team_display_name} by reading all known roster CSVs."""
    assignments: dict[str, str] = {}
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
    player_name: str, role: str, ability_name: str,
    description: str, team: str,
) -> None:
    """Draw one card at lower-left corner (x, y) with width w and height h."""
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

    # ── Ability name (bold, centred, just below divider) ─────────────────────
    ability_y = y + h - header_h - 15
    c.setFont("Helvetica-Bold", 10)
    c.setFillColor(colors.black)
    c.drawCentredString(x + w / 2, ability_y, ability_name)

    # ── Thin rule below ability name ─────────────────────────────────────────
    c.setLineWidth(0.4)
    c.setStrokeColor(colors.HexColor("#666666"))
    c.line(x + 18, ability_y - 5, x + w - 18, ability_y - 5)

    # ── Description (wrapped, centred, small) ────────────────────────────────
    desc_top = ability_y - 10
    desc_h_avail = desc_top - y - 6

    style = ParagraphStyle(
        "desc",
        fontName="Helvetica",
        fontSize=8.5,
        leading=12,
        alignment=TA_CENTER,
        textColor=colors.black,
    )
    para = Paragraph(description, style)
    _, ph = para.wrap(w - 18, desc_h_avail)
    # Vertically centre the description block
    para.drawOn(c, x + 9, y + 4 + max(0, (desc_h_avail - ph) / 2))


def draw_set_template_card(
    c: canvas.Canvas,
    x: float, y: float, w: float, h: float,
    team_name: str,
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
    section("NORMAL SET  (setter sets)", [
        ("1-3",  "OH · MB · OPP",  "—",         "3"),
        ("4-5",  "OH · MB",        "OH/MB/OPP", "3"),
        ("6-7",  "MB · OPP",       "OH/MB/OPP", "3"),
        ("8-9",  "OH · OPP",       "OH/MB/OPP", "4"),
        ("10",   "OH · MB · OPP",  "OH/MB/OPP", "4"),
    ])

    cur_y -= 6
    c.setLineWidth(0.6)
    c.line(x + 8, cur_y, x + w - 8, cur_y)
    cur_y -= 14

    # ── Broken-play set table ─────────────────────────────────────────────────
    section("BROKEN PLAY  (non-setter sets)", [
        ("1-3",  "OH · MB",   "MB only", "2"),
        ("4-7",  "OH · OPP",  "MB only", "1"),
        ("8-10", "MB · OPP",  "MB only", "2"),
    ])

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


def make_pdf(
    cards: list[dict],
    output_path: str,
    teams: list[str] | None = None,
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
            card["ability_name"], card["description"],
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

    c.save()
    total_cards = total_ability + len(team_order)
    pages = (total_cards + COLS * ROWS - 1) // (COLS * ROWS)
    print(f"Wrote {total_ability} ability cards + {len(team_order)} team cards across {pages} page(s) → {output_path}")


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

    cards: list[dict] = []
    with open(args.input, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            team = team_map.get(row["player_name"], "Unknown")
            if args.teams and team not in args.teams:
                continue
            cards.append({
                "player_name": row["player_name"],
                "role":        row["role"],
                "ability_name": row["ability_name"],
                "description": row["description"],
                "team":        team,
            })

    if not cards:
        print("No cards matched — check your --teams filter.")
        return

    make_pdf(cards, args.output, teams=args.teams)


if __name__ == "__main__":
    main()
