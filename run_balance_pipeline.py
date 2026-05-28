from __future__ import annotations

import argparse
import csv
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import List


@dataclass
class RunResult:
    seed: int
    matrix_csv: Path
    validation_txt: Path
    validator_exit: int


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Run balance_matrix.py and validate_balance_targets.py for one or more seeds."
    )
    p.add_argument("--games", type=int, default=2000, help="Games per matchup")
    p.add_argument("--mode", choices=["all", "pvp", "pvd"], default="all")
    p.add_argument(
        "--seeds",
        nargs="+",
        type=int,
        default=[42],
        help="One or more seeds (space-separated)",
    )
    p.add_argument(
        "--targets",
        type=Path,
        default=Path("data/balance_targets.csv"),
        help="Target bands CSV",
    )
    p.add_argument(
        "--out-dir",
        type=Path,
        default=Path("results"),
        help="Directory for matrix and validation artifacts",
    )
    p.add_argument(
        "--label",
        type=str,
        default="pipeline",
        help="Label prefix for output filenames",
    )
    return p.parse_args()


def run_cmd(cmd: List[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(cmd, check=False, text=True, capture_output=True)


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def main() -> int:
    args = parse_args()
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    args.out_dir.mkdir(parents=True, exist_ok=True)

    all_results: List[RunResult] = []

    for seed in args.seeds:
        matrix_csv = args.out_dir / f"{args.label}_{args.mode}_{args.games}g_seed{seed}_{ts}.csv"
        matrix_cmd = [
            sys.executable,
            "balance_matrix.py",
            "--mode", args.mode,
            "--games", str(args.games),
            "--seed", str(seed),
            "--output", str(matrix_csv),
        ]
        matrix_proc = run_cmd(matrix_cmd)

        matrix_log = matrix_csv.with_suffix(".matrix.log.txt")
        write_text(
            matrix_log,
            "COMMAND:\n"
            + " ".join(matrix_cmd)
            + "\n\nSTDOUT:\n"
            + (matrix_proc.stdout or "")
            + "\nSTDERR:\n"
            + (matrix_proc.stderr or "")
            + f"\nEXIT_CODE: {matrix_proc.returncode}\n",
        )

        if matrix_proc.returncode != 0:
            print(f"[FAIL] matrix run failed for seed={seed}. See {matrix_log}")
            all_results.append(
                RunResult(
                    seed=seed,
                    matrix_csv=matrix_csv,
                    validation_txt=matrix_csv.with_suffix(".validation.txt"),
                    validator_exit=99,
                )
            )
            continue

        validate_cmd = [
            sys.executable,
            "validate_balance_targets.py",
            "--results", str(matrix_csv),
            "--targets", str(args.targets),
        ]
        validate_proc = run_cmd(validate_cmd)

        validation_txt = matrix_csv.with_suffix(".validation.txt")
        write_text(
            validation_txt,
            "COMMAND:\n"
            + " ".join(validate_cmd)
            + "\n\nSTDOUT:\n"
            + (validate_proc.stdout or "")
            + "\nSTDERR:\n"
            + (validate_proc.stderr or "")
            + f"\nEXIT_CODE: {validate_proc.returncode}\n",
        )

        print(
            f"[DONE] seed={seed} matrix={matrix_csv.name} "
            f"validator_exit={validate_proc.returncode}"
        )

        all_results.append(
            RunResult(
                seed=seed,
                matrix_csv=matrix_csv,
                validation_txt=validation_txt,
                validator_exit=validate_proc.returncode,
            )
        )

    summary_csv = args.out_dir / f"{args.label}_{args.mode}_{args.games}g_{ts}_summary.csv"
    with open(summary_csv, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(
            f,
            fieldnames=["seed", "matrix_csv", "validation_txt", "validator_exit"],
        )
        w.writeheader()
        for r in all_results:
            w.writerow(
                {
                    "seed": r.seed,
                    "matrix_csv": str(r.matrix_csv),
                    "validation_txt": str(r.validation_txt),
                    "validator_exit": r.validator_exit,
                }
            )

    failures = [r for r in all_results if r.validator_exit != 0]
    print(f"\nSummary file: {summary_csv}")
    print(f"Runs: {len(all_results)} | nonzero validator exits: {len(failures)}")

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
