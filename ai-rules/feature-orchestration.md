# Feature Orchestration

Load this only when splitting fork work into delegated worker tasks or integrating their results. A worker implementing one bounded brief must not load this file.

## Delegation Briefs And Persistent Packets

Every delegated task needs a bounded brief. Persist that brief as a task packet
only when another context must resume it, the work crosses a consequential
compatibility/release/approval boundary, or the packet itself is required
evidence. Small same-session delegations use the worker prompt and reviewed diff;
creating a task/result pair is not a completion predicate.

A delegation brief contains:

- task type (`Read-Only Audit | Implementation | Verification | Diagnostic Isolation`)
- goal restated as a verifiable observable, per `AGENTS.md § Task Framing`
- protected invariants and current provenance of package pins, compatibility
  paths, registry exceptions, guards, exclusions, fallbacks, or legacy transports
- adaptive execution lane: which facts or mechanisms the worker may correct
  when falsified, and the exact consequence that requires fresh approval
- write scope (exact files/folders, or "read-only")
- read-only context files
- rules to load and rules not to load
- non-scope (explicit exclusions)
- acceptance criteria
- verification expectations, per `AGENTS.md § Verification Expectations` and `ai-rules/testing-and-verification.md` (Python evidence does not prove C# compile; state the Unity evidence plan or the expected blocker up front)
- growth check, only when the task adds a materially new capability to an existing owner: current responsibilities, the two prior growth changes, and the keep / extract-first / follow-up decision per `ai-rules/engineering-method.md § Growth Trigger`
- explicit budgets: max attempts at the same problem, max diagnostic loops
- stop-and-report blockers stated as actual authority, compatibility, safety,
  consumer-write, release/rollback, or acceptance boundaries—not merely a
  stale pin/mechanism or a tool's suggested remediation
- result-report path and fields only when persistent handoff evidence is needed

## Task Types

Use one task type per packet unless the scope is very small.

- **Read-Only Audit** - inspect code/config/docs and report findings, risks, and a recommended next task. No edits. Must state "no files changed".
- **Implementation** - edit files inside the write scope only. Must not change shared contracts, schemas, registries, or transport behavior unless the packet explicitly assigns that responsibility.
- **Verification** - review a completed implementation against the acceptance criteria. Read-only unless the packet allows a small docs/result update. Reports pass/fail with file/line issues and a proposed next task on fail.
- **Diagnostic Isolation** - read-only instrumentation after two failed implementation attempts on the same surface. The packet includes failure history, competing hypotheses with discriminating signals, and a decision tree mapping each confirmed hypothesis to one mechanical next fix. A diagnostic pass that returns implementation changes beyond probes violates the contract.

## Parallelization

Parallelize only when concurrent write scopes do not overlap. A pre-existing
dirty file is not automatically an active owner; classify it before dispatch.
Concurrent overlap is a stop; sequential integration follows `§ Worktree
Reconciliation By Role`.

- Cross-language contracts are shared state. If a packet changes tool schema, discovery metadata, response shape, or transport contracts, that change is a prerequisite task; never let two parallel workers hold opposite sides of one contract.
- Do not parallelize extraction and feature expansion against the same owner (`§ Growth Trigger`).
- Safe: independent read-only audits, implementation slices with disjoint files, Python-only and C#-only slices that do not share a contract change.
- Keep one orchestrator/integrator responsible for reconciling naming, duplicated ownership, and cross-slice consistency.

## Worktree Reconciliation By Role

`Unrelated` is relative to one packet or commit; it is never a terminal
repository-level disposition.

- A worker preserves out-of-scope changes and reports any overlap or dependency.
- A packet integrator excludes other slices from the packet commit and routes
  them to the program/global coordinator.
- The program/global coordinator inspects `git status --short` and staged state
  at startup, after every commit, and before advancing to another milestone or
  wave. Group every modified, staged, deleted, and untracked path into a work
  slice and record its provenance/owner, state, evidence or acceptance gap,
  commit disposition, and next action in the active plan, handoff, or tracker.

Every slice must end as `committed`, `active` under a named task,
`acceptance_pending`, `deferred` with owner/reason, `decision_needed`, or
`local_only/ignored` with rationale. `Unknown` is temporary investigation, not
permission to advance. Coordinator-owned packets, reports, handoffs, and
generated orchestration artifacts are included.

This inventory does not authorize editing, staging, committing, reverting, or
discarding another owner's work. It requires routing and closure. A clean index
or a sentence such as "remaining unrelated changes" does not satisfy it.

## Worker Prompt Shape

When spawning or assigning a worker, include at minimum:

```md
You are not alone in the codebase. Do not revert or overwrite other changes.

Task type: <Read-Only Audit | Implementation | Verification | Diagnostic Isolation>

Load these rules:
- AGENTS.md
- ai-rules/engineering-method.md
- <relevant domain rule and ai-rules/knowledge/*.md files>

Do not load:
- ai-rules/feature-orchestration.md

Write scope / Read-only context / Goal / Protected invariant and restriction
provenance / Adaptive execution lane / Actual approval or stop boundary /
Non-scope / Budget / Optional result-report path and required fields when a
persistent handoff is justified.
```

## Result And Artifact Placement

```text
docs/agent-work/tasks/NNN-<slug>.md
docs/agent-work/results/NNN-<slug>-result.md
docs/agent-work/artifacts/<task-id-and-slug>/
```

Result reports are optional handoff evidence, not durable project knowledge.
Create one only when the work must cross a context/acceptance boundary or retain
unique evidence that the source, test output, or git history cannot carry more
directly. Promote only stable, verified facts to the relevant
`ai-rules/knowledge/*.md` file via the `ai-rules/project-knowledge.md` index.

## Integration Rules

After worker results return, the orchestrator must:

- review the diff, not just the worker's summary
- read the result report when one was required, and always confirm from the diff
  that the worker stayed inside the write scope
- run targeted Python tests for touched layers
- require Unity compile/test evidence for C# changes, or record the exact verification gap and the next action that closes it
- assign remaining risks an owner and next action; persist a follow-up packet
  only when its handoff or consequence justifies one
- update durable facts in `ai-rules/knowledge/*.md` only after evidence is verified and reusable
- after any commit, reconcile the full unstaged, staged, deleted, and untracked
  path set under `§ Worktree Reconciliation By Role` before advancing the queue

Do not mark a task accepted from worker confidence alone. Acceptance requires a reviewed diff plus verification evidence or an explicitly documented verification gap.

Two standing disciplines for review and handoff:

- A review finding is a test of existing claims or a proposal — never automatically work. Classify before it changes scope: only a finding that blocks the approved objective stays in scope automatically; separate defects, future hardening, probe artifacts, and cosmetic opportunities enter scope by explicit decision, not by silence.
- Inherited claims keep their original evidence class; retelling or consensus does not upgrade them. For enumerable targets, status is a number generated by a command at the moment of decision — not a quoted figure, not a prose adjective.
- For a consequential change that introduces or moves ownership, review the ownership decision itself. Name the state, lifecycle, policy, I/O, transport, security/rollback, or variability boundary the owner uniquely holds and compare it with the existing source of truth and simpler shapes. Reject a forwarding-only owner unless a current named consumer or failure mode needs the boundary to change, fail, deploy, or be replaced separately; merely labelling it a published interface, serialization or anti-corruption seam, independent lifecycle/policy owner, or test/replaceability seam does not pass.

## Prohibited Delegation Patterns

Do not create tasks that:

- delegate a vague goal without write scope, non-scope, and acceptance criteria
- give overlapping write scopes to parallel workers
- let a worker choose between competing contract or schema interpretations without prior approval
- omit required handoff evidence when the delegation crosses a context,
  consequential acceptance, or release boundary
- continue with a third implementation brief after two failed attempts on the same surface; brief a Diagnostic Isolation task instead
- authorize a fallback, compatibility shim, or defensive patch before a diagnostic pass has confirmed the underlying owner is actually broken
- freeze a proposed mechanism, package pin, compatibility path, registry
  exception, guard, exclusion, fallback, or legacy transport without naming the
  current invariant and provenance that still require it
- stop solely because a tool's suggested remediation crosses an approval
  boundary before inspecting whether the current graph permits a correct
  in-scope owner-level solution
- keep tuning a timeout, retry count, or budget after the worker reports the change had no observable effect; that signal is diagnostic, not tunable
- claim Unity compile/test success without actual Unity evidence
