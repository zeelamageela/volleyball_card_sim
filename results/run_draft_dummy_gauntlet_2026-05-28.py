import csv
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYTHON = ROOT / '.venv' / 'Scripts' / 'python.exe'
DATE = '2026-05-28'

ROSTERS = [
    ('DraftA', 'data/team_draft_a.csv'),
    ('DraftB', 'data/team_draft_b.csv'),
    ('EvenA', 'data/team_draft_even_a.csv'),
    ('EvenB', 'data/team_draft_even_b.csv'),
    ('RandomA', 'data/team_draft_random_a.csv'),
    ('RandomB', 'data/team_draft_random_b.csv'),
]
DUMMIES = ['Easy', 'Medium', 'Hard']

RESULTS_DIR = ROOT / 'results'
RESULTS_DIR.mkdir(parents=True, exist_ok=True)
CSV_PATH = RESULTS_DIR / f'draft_dummy_gauntlet_{DATE}.csv'
MD_PATH = RESULTS_DIR / f'draft_dummy_gauntlet_{DATE}.md'

LINE_WINS_RE = re.compile(r'^\s*(?P<name>.+?)\s*:\s*(?P<wins>\d+)\s+wins\s+\((?P<pct>[\d.]+)%\)\s*$', re.MULTILINE)
AVG_EX_RE = re.compile(r'Avg exchanges/rally:\s*([0-9]+(?:\.[0-9]+)?)')
ENDING_RE = re.compile(r'^\s*(\d+)x\s+(.*?)\s*$', re.MULTILINE)


def decode_bytes(data: bytes) -> str:
    for enc in ('utf-8', 'utf-8-sig', 'utf-16', 'cp1252'):
        try:
            return data.decode(enc)
        except UnicodeDecodeError:
            pass
    return data.decode('utf-8', errors='replace')


def parse_log(text: str, team: str, dummy: str):
    wins_map = {}
    pct_map = {}
    for m in LINE_WINS_RE.finditer(text):
        name = m.group('name').strip()
        wins_map[name] = int(m.group('wins'))
        pct_map[name] = float(m.group('pct'))

    avg_match = AVG_EX_RE.search(text)
    avg_exchanges = float(avg_match.group(1)) if avg_match else ''

    top1 = top2 = top3 = ''
    section_idx = text.find('Rally endings (all types):')
    if section_idx >= 0:
        tail = text[section_idx:]
        endings = [m.group(2).strip() for m in ENDING_RE.finditer(tail)]
        if endings:
            top1 = endings[0]
        if len(endings) > 1:
            top2 = endings[1]
        if len(endings) > 2:
            top3 = endings[2]

    return {
        'team_wins': wins_map.get(team, ''),
        'team_win_pct': pct_map.get(team, ''),
        'dummy_wins': wins_map.get(dummy, ''),
        'dummy_win_pct': pct_map.get(dummy, ''),
        'avg_exchanges': avg_exchanges,
        'top1': top1,
        'top2': top2,
        'top3': top3,
    }


rows = []
for team, roster in ROSTERS:
    for dummy in DUMMIES:
        log_rel = f'results/draft_gauntlet_{team}_vs_{dummy}_{DATE}.log'
        log_path = ROOT / log_rel

        cmd = [
            str(PYTHON), '-X', 'utf8', 'main.py',
            '--mode', 'pvd',
            '--strategy-a', 'smart',
            '--games', '2000',
            '--seed', '42',
            '--player-cards', 'data/player_cards.csv',
            '--roster-a', roster,
            '--team-a-name', team,
            '--team-b-name', dummy,
        ]

        env = os.environ.copy()
        env['PYTHONIOENCODING'] = 'utf-8'
        env['PYTHONUTF8'] = '1'

        proc = subprocess.run(
            cmd,
            cwd=str(ROOT),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            env=env,
            check=False,
        )

        log_path.write_bytes(proc.stdout)
        parsed = parse_log(decode_bytes(proc.stdout), team, dummy)

        row = {
            'team': team,
            'dummy': dummy,
            'games': 2000,
            'seed': 42,
            'exit_code': proc.returncode,
            'team_wins': parsed['team_wins'],
            'team_win_pct': parsed['team_win_pct'],
            'dummy_wins': parsed['dummy_wins'],
            'dummy_win_pct': parsed['dummy_win_pct'],
            'avg_exchanges': parsed['avg_exchanges'],
            'top1': parsed['top1'],
            'top2': parsed['top2'],
            'top3': parsed['top3'],
            'log_file': log_rel,
        }
        rows.append(row)
        print(f"{team} vs {dummy}: exit={proc.returncode} team_win_pct={row['team_win_pct']}")

fieldnames = [
    'team', 'dummy', 'games', 'seed', 'exit_code', 'team_wins', 'team_win_pct',
    'dummy_wins', 'dummy_win_pct', 'avg_exchanges', 'top1', 'top2', 'top3', 'log_file'
]
with CSV_PATH.open('w', newline='', encoding='utf-8') as f:
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(rows)

team_avg = {}
for team, _ in ROSTERS:
    vals = [r['team_win_pct'] for r in rows if r['team'] == team and isinstance(r['team_win_pct'], (int, float))]
    team_avg[team] = (sum(vals) / len(vals)) if vals else 0.0
ranked = sorted(team_avg.items(), key=lambda x: x[1], reverse=True)

md_lines = []
md_lines.append(f'# Draft Dummy Gauntlet ({DATE})')
md_lines.append('')
md_lines.append('## Ranking by Average Team Win% (Easy/Medium/Hard)')
md_lines.append('')
md_lines.append('| Rank | Team | Avg Team Win% |')
md_lines.append('|---:|---|---:|')
for i, (team, avg) in enumerate(ranked, 1):
    md_lines.append(f'| {i} | {team} | {avg:.2f}% |')

md_lines.append('')
md_lines.append('## All 18 Runs')
md_lines.append('')
md_lines.append('| Team | Dummy | Games | Seed | Exit | Team Wins | Team Win% | Dummy Wins | Dummy Win% | Avg Exchanges | Top1 | Top2 | Top3 | Log |')
md_lines.append('|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|')
for r in rows:
    team_pct = f"{r['team_win_pct']:.2f}%" if isinstance(r['team_win_pct'], (int, float)) else ''
    dummy_pct = f"{r['dummy_win_pct']:.2f}%" if isinstance(r['dummy_win_pct'], (int, float)) else ''
    md_lines.append(
        f"| {r['team']} | {r['dummy']} | {r['games']} | {r['seed']} | {r['exit_code']} | {r['team_wins']} | {team_pct} | {r['dummy_wins']} | {dummy_pct} | {r['avg_exchanges']} | {r['top1']} | {r['top2']} | {r['top3']} | {r['log_file']} |"
    )

MD_PATH.write_text('\n'.join(md_lines) + '\n', encoding='utf-8')

print(f'WROTE {CSV_PATH}')
print(f'WROTE {MD_PATH}')
print('RANKING')
for team, avg in ranked:
    print(f'{team},{avg:.4f}')
