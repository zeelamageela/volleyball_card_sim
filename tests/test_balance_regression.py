from __future__ import annotations

import csv
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TARGETS_CSV = ROOT / "data" / "balance_targets.csv"
LOCKED_RESULTS_CSV = ROOT / "results" / "season1_week2_identity_branch_passL_500g.csv"


def _load_rows(path: Path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def test_locked_week2_pvd_win_rates_in_band():
    """
    Regression guard for Week 2 lock.

    This asserts the locked Week 2 artifact remains inside PvD target bands.
    PvP is intentionally excluded due accepted waiver.
    """
    targets = _load_rows(TARGETS_CSV)
    results = _load_rows(LOCKED_RESULTS_CSV)

    by_pair = {(r["team_a"], r["team_b"]): float(r["win_rate_a"]) for r in results}

    checks = [
        t
        for t in targets
        if t.get("metric") == "win_rate_a" and t.get("scope") == "pvd"
    ]

    failures = []
    for t in checks:
        a = t["team_a"]
        b = t["team_b"]
        lo = float(t["min_value"])
        hi = float(t["max_value"])
        observed = by_pair.get((a, b))
        if observed is None:
            failures.append(f"missing matchup row: {a} vs {b}")
            continue
        if not (lo <= observed <= hi):
            failures.append(
                f"{a} vs {b} observed={observed:.2f} target=[{lo:.2f}, {hi:.2f}]"
            )

    assert not failures, "\n".join(failures)
