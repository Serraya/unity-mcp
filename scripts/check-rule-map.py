#!/usr/bin/env python3
"""Verify every rule file loads and every rule pointer resolves.

The failure this catches is silent: a rule file that exists but is named nowhere
in AGENTS.md never loads, so an agent works without it and nothing errors. That
happened once already — a session-lifecycle tracker sat unlisted next to a
sibling tracker that was listed.

Checks, in both directions:

  1. every ai-rules/*.md is named in AGENTS.md            (no orphaned rule)
  2. every ai-rules/*.md named in AGENTS.md exists        (no dead pointer)
  3. every ai-rules/knowledge/*.md is named in the index  (no orphaned fact file)
  4. every knowledge file named in the index exists       (no dead index entry)

A file may be excluded only by naming it in EXCLUDED below with a reason. An
undeclared exclusion is the thing this script exists to prevent.

Run standalone (`python3 scripts/check-rule-map.py`) or from a pre-commit hook.
Exits 0 when the rule map is intact, 1 otherwise.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

# path -> reason. Keep empty unless a file genuinely must not load.
EXCLUDED: dict[str, str] = {}


def repo_root() -> Path:
    out = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True, text=True, check=False,
    )
    if out.returncode == 0 and out.stdout.strip():
        return Path(out.stdout.strip())
    return Path(__file__).resolve().parent.parent


def check(root: Path) -> list[str]:
    problems: list[str] = []

    contract = root / "AGENTS.md"
    if not contract.is_file():
        return [f"{contract} not found — this script expects a root AGENTS.md"]
    contract_text = contract.read_text(encoding="utf-8", errors="replace")

    rules_dir = root / "ai-rules"
    if not rules_dir.is_dir():
        return problems  # Repository does not use the ai-rules layout.

    # 1. orphaned rule files
    rule_files = sorted(p for p in rules_dir.glob("*.md"))
    if not rule_files:
        problems.append("ai-rules/ contains no .md files — check the layout")
    for path in rule_files:
        rel = path.relative_to(root).as_posix()
        if rel in EXCLUDED:
            continue
        if path.name not in contract_text:
            problems.append(
                f"ORPHAN: {rel} exists but is named nowhere in AGENTS.md, so it "
                f"never loads. Add it to the rule map, or declare it in "
                f"EXCLUDED with a reason."
            )

    # 2. dead pointers from the contract
    for ref in sorted(set(re.findall(r"ai-rules/[A-Za-z0-9._/-]+\.md", contract_text))):
        if not (root / ref).is_file():
            problems.append(f"DEAD POINTER: AGENTS.md names {ref}, which does not exist")

    # 3 and 4. the knowledge index, when the repository has one
    index = rules_dir / "project-knowledge.md"
    knowledge_dir = rules_dir / "knowledge"
    if index.is_file() and knowledge_dir.is_dir():
        index_text = index.read_text(encoding="utf-8", errors="replace")
        for path in sorted(knowledge_dir.glob("*.md")):
            rel = path.relative_to(root).as_posix()
            if rel in EXCLUDED:
                continue
            if path.name not in index_text:
                problems.append(
                    f"UNINDEXED: {rel} exists but is named nowhere in "
                    f"ai-rules/project-knowledge.md, so nothing routes to it"
                )
        for ref in sorted(set(re.findall(r"knowledge/[A-Za-z0-9._-]+\.md", index_text))):
            if not (rules_dir / ref).is_file():
                problems.append(
                    f"DEAD INDEX ENTRY: project-knowledge.md names {ref}, "
                    f"which does not exist"
                )

    return problems


def main() -> int:
    root = repo_root()
    problems = check(root)
    if problems:
        sys.stderr.write("Rule-map check failed:\n")
        for p in problems:
            sys.stderr.write(f"  - {p}\n")
        sys.stderr.write(
            "\nA rule file that never loads produces no error and no symptom "
            "until someone needs the rule.\n"
        )
        return 1
    print("Rule-map check passed: every rule file loads, every pointer resolves.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
