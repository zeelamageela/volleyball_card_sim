from __future__ import annotations

import argparse
import csv
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple


@dataclass
class CheckResult:
    status: str
    metric: str
    scope: str
    team_a: str
    team_b: str
    observed: Optional[float]
    min_value: float
    max_value: float
    detail: str


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Validate matrix CSV output against data/balance_targets.csv"
    )
    p.add_argument(
        "--results",
        type=Path,
        required=True,
        help="Path to balance matrix result CSV",
    )
    p.add_argument(
        "--targets",
        type=Path,
        default=Path("data/balance_targets.csv"),
        help="Path to target definitions CSV",
    )
    p.add_argument(
        "--fail-on-missing",
        action="store_true",
        help="Treat missing matchup rows as failures",
    )
    return p.parse_args()


def load_csv_rows(path: Path) -> List[Dict[str, str]]:
    with open(path, newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def parse_float(row: Dict[str, str], key: str, default: float = 0.0) -> float:
    raw = (row.get(key) or "").strip()
    if raw == "":
        return default
    return float(raw)


def parse_top_endings_lead_count(top_endings: str) -> Optional[int]:
    text = (top_endings or "").strip()
    if not text:
        return None
    first = text.split("|", 1)[0].strip()
    m = re.search(r":\s*(\d+)\s*$", first)
    if not m:
        return None
    return int(m.group(1))


def within(value: float, lo: float, hi: float) -> bool:
    return lo <= value <= hi


def evaluate(
    targets: List[Dict[str, str]],
    results: List[Dict[str, str]],
    fail_on_missing: bool,
) -> List[CheckResult]:
    checks: List[CheckResult] = []

    by_matchup: Dict[Tuple[str, str, str], Dict[str, str]] = {}
    for r in results:
        key = (
            (r.get("team_a") or "").strip(),
            (r.get("team_b") or "").strip(),
            (r.get("mode") or "").strip(),
        )
        by_matchup[key] = r

    for t in targets:
        metric = (t.get("metric") or "").strip()
        scope = (t.get("scope") or "").strip()
        team_a = (t.get("team_a") or "").strip()
        team_b = (t.get("team_b") or "").strip()
        lo = parse_float(t, "min_value")
        hi = parse_float(t, "max_value")

        if metric == "win_rate_a":
            key = (team_a, team_b, scope)
            row = by_matchup.get(key)
            if row is None:
                status = "FAIL" if fail_on_missing else "SKIP"
                checks.append(
                    CheckResult(
                        status=status,
                        metric=metric,
                        scope=scope,
                        team_a=team_a,
                        team_b=team_b,
                        observed=None,
                        min_value=lo,
                        max_value=hi,
                        detail="missing matchup row",
                    )
                )
                continue
            value = parse_float(row, "win_rate_a")
            status = "PASS" if within(value, lo, hi) else "FAIL"
            checks.append(
                CheckResult(
                    status=status,
                    metric=metric,
                    scope=scope,
                    team_a=team_a,
                    team_b=team_b,
                    observed=value,
                    min_value=lo,
                    max_value=hi,
                    detail="",
                )
            )
            continue

        if metric == "avg_exchanges" and scope == "all":
            for row in results:
                value = parse_float(row, "avg_exchanges")
                a = (row.get("team_a") or "").strip()
                b = (row.get("team_b") or "").strip()
                mode = (row.get("mode") or "").strip()
                status = "PASS" if within(value, lo, hi) else "FAIL"
                checks.append(
                    CheckResult(
                        status=status,
                        metric=metric,
                        scope=mode,
                        team_a=a,
                        team_b=b,
                        observed=value,
                        min_value=lo,
                        max_value=hi,
                        detail="",
                    )
                )
            continue

        if metric == "top_endings_share" and scope == "all":
            for row in results:
                a = (row.get("team_a") or "").strip()
                b = (row.get("team_b") or "").strip()
                mode = (row.get("mode") or "").strip()
                lead_count = parse_top_endings_lead_count((row.get("top_endings") or ""))
                games = parse_float(row, "games")
                avg_rallies = parse_float(row, "avg_rallies")
                est_total = games * avg_rallies

                if lead_count is None or est_total <= 0:
                    checks.append(
                        CheckResult(
                            status="SKIP",
                            metric=metric,
                            scope=mode,
                            team_a=a,
                            team_b=b,
                            observed=None,
                            min_value=lo,
                            max_value=hi,
                            detail="cannot parse top_endings share",
                        )
                    )
                    continue

                share = (lead_count / est_total) * 100.0
                status = "PASS" if within(share, lo, hi) else "FAIL"
                checks.append(
                    CheckResult(
                        status=status,
                        metric=metric,
                        scope=mode,
                        team_a=a,
                        team_b=b,
                        observed=share,
                        min_value=lo,
                        max_value=hi,
                        detail="estimated from top_endings + avg_rallies",
                    )
                )
            continue

        checks.append(
            CheckResult(
                status="SKIP",
                metric=metric,
                scope=scope,
                team_a=team_a,
                team_b=team_b,
                observed=None,
                min_value=lo,
                max_value=hi,
                detail="unsupported target row",
            )
        )

    return checks


def print_report(checks: List[CheckResult]) -> None:
    status_counts: Dict[str, int] = {"PASS": 0, "FAIL": 0, "SKIP": 0}
    for c in checks:
        status_counts[c.status] = status_counts.get(c.status, 0) + 1

    print("Balance Target Validation")
    print("=" * 80)
    for c in checks:
        matchup = f"{c.team_a} vs {c.team_b}" if c.team_a and c.team_b else "*"
        observed = "n/a" if c.observed is None else f"{c.observed:.3f}"
        band = f"[{c.min_value:.3f}, {c.max_value:.3f}]"
        extra = f" ({c.detail})" if c.detail else ""
        print(
            f"{c.status:<4} | {c.metric:<18} | {c.scope:<4} | {matchup:<22} "
            f"| observed={observed:<10} target={band}{extra}"
        )

    print("-" * 80)
    print(
        f"Summary: PASS={status_counts.get('PASS', 0)} "
        f"FAIL={status_counts.get('FAIL', 0)} SKIP={status_counts.get('SKIP', 0)}"
    )


def main() -> int:
    args = parse_args()

    if not args.results.exists():
        print(f"Results file not found: {args.results}", file=sys.stderr)
        return 2
    if not args.targets.exists():
        print(f"Targets file not found: {args.targets}", file=sys.stderr)
        return 2

    targets = load_csv_rows(args.targets)
    results = load_csv_rows(args.results)
    checks = evaluate(targets, results, args.fail_on_missing)
    print_report(checks)

    has_fail = any(c.status == "FAIL" for c in checks)
    return 1 if has_fail else 0


if __name__ == "__main__":
    raise SystemExit(main())
