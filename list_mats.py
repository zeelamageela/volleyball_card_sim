"""
list_mats.py — print the pickable team mats and their seats.

Usage:
  python list_mats.py            # list every pickable mat
  python list_mats.py Blitz       # show just one mat
"""

from __future__ import annotations

import sys

from src.mats import load_all_mats, load_mat


def main() -> None:
    if len(sys.argv) > 1:
        mat = load_mat(sys.argv[1])
        print(mat.describe())
        return

    mats = load_all_mats()
    print(f"{len(mats)} pickable mat(s):\n")
    for mat in mats:
        print(mat.describe())
        print()


if __name__ == "__main__":
    main()
