# Git Workflow

Load this for staging, commits, branches, rebases, pushes, PRs, or git-lock problems.

## Branches

- Default development branch for this fork is `beta` unless the user says otherwise.
- Do not commit directly to `main`.
- Use a focused branch when the user asks for branch work.

## Dirty Worktrees

- Inspect `git status --short` before editing.
- Never revert user changes.
- Keep unrelated dirty files out of your diff.
- If generated files appear from tool setup, remove only files that are clearly created by the current task and not user work.

## Commits

- Do not create commits unless the user explicitly asks.
- Keep framework changes, consumer app pin updates, docs, and release metadata in separate commits unless the user asks for a combined change.
- Before staging, inspect the diff and ensure no generated or unrelated files are included.

## Locks And Destructive Actions

- Do not use destructive git commands unless explicitly requested: `git reset --hard`, `git checkout -- <path>`, `git restore <path>` without `--staged`, `git clean -f/-d/-x`, `git stash drop/clear`, `git branch -D`, and `git push --force`. Report the dirty files instead; if the user wants one of these, they can run it themselves.
- If a git lock blocks work, identify the process first; do not delete locks blindly.
