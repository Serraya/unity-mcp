# Feature Orchestration

Load this only when splitting fork work into delegated worker tasks or integrating their results. A worker implementing a single packet must not load this file.

## Task Packet Requirement

Every delegated task must contain:

- task type (`Read-Only Audit | Implementation | Verification | Diagnostic Isolation`)
- goal restated as a verifiable observable, per `AGENTS.md § Task Framing`
- write scope (exact files/folders, or "read-only")
- read-only context files
- rules to load and rules not to load
- non-scope (explicit exclusions)
- acceptance criteria
- verification expectations, per `AGENTS.md § Verification Expectations` and `ai-rules/testing-and-verification.md` (Python evidence does not prove C# compile; state the Unity evidence plan or the expected blocker up front)
- growth check, only when the task adds a materially new capability to an existing owner: current responsibilities, the two prior growth changes, and the keep / extract-first / follow-up decision per `ai-rules/engineering-method.md § Growth Trigger`
- explicit budgets: max attempts at the same problem, max diagnostic loops
- stop-and-report blockers (ambiguity, overlapping ownership, missing evidence, contract conflict, compile blocker)
- result report path and required report fields

## Task Types

Use one task type per packet unless the scope is very small.

- **Read-Only Audit** - inspect code/config/docs and report findings, risks, and a recommended next task. No edits. Must state "no files changed".
- **Implementation** - edit files inside the write scope only. Must not change shared contracts, schemas, registries, or transport behavior unless the packet explicitly assigns that responsibility.
- **Verification** - review a completed implementation against the acceptance criteria. Read-only unless the packet allows a small docs/result update. Reports pass/fail with file/line issues and a proposed next task on fail.
- **Diagnostic Isolation** - read-only instrumentation after two failed implementation attempts on the same surface. The packet includes failure history, competing hypotheses with discriminating signals, and a decision tree mapping each confirmed hypothesis to one mechanical next fix. A diagnostic pass that returns implementation changes beyond probes violates the contract.

## Parallelization

Parallelize only when write scopes do not overlap. Overlapping file ownership is a stop, not a coordination prompt.

- Cross-language contracts are shared state. If a packet changes tool schema, discovery metadata, response shape, or transport contracts, that change is a prerequisite task; never let two parallel workers hold opposite sides of one contract.
- Do not parallelize extraction and feature expansion against the same owner (`§ Growth Trigger`).
- Safe: independent read-only audits, implementation slices with disjoint files, Python-only and C#-only slices that do not share a contract change.
- Keep one orchestrator/integrator responsible for reconciling naming, duplicated ownership, and cross-slice consistency.

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

Write scope / Read-only context / Goal / Non-scope / Budget / Stop-and-report conditions / Result report path and required fields.
```

## Result And Artifact Placement

```text
docs/agent-work/tasks/NNN-<slug>.md
docs/agent-work/results/NNN-<slug>-result.md
docs/agent-work/artifacts/<task-id-and-slug>/
```

Result reports are handoff evidence, not durable project knowledge. Promote only stable, verified facts to the relevant `ai-rules/knowledge/*.md` file via the `ai-rules/project-knowledge.md` index.

## Integration Rules

After worker results return, the orchestrator must:

- review the diff, not just the worker's summary
- read the result report and confirm the worker stayed inside the write scope
- run targeted Python tests for touched layers
- require Unity compile/test evidence for C# changes, or record the exact verification gap and the next action that closes it
- turn remaining risks into explicit follow-up packets or accepted deviations
- update durable facts in `ai-rules/knowledge/*.md` only after evidence is verified and reusable

Do not mark a task accepted from worker confidence alone. Acceptance requires a reviewed diff plus verification evidence or an explicitly documented verification gap.

Two standing disciplines for review and handoff:

- A review finding is a test of existing claims or a proposal — never automatically work. Classify before it changes scope: only a finding that blocks the approved objective stays in scope automatically; separate defects, future hardening, probe artifacts, and cosmetic opportunities enter scope by explicit decision, not by silence.
- Inherited claims keep their original evidence class; retelling or consensus does not upgrade them. For enumerable targets, status is a number generated by a command at the moment of decision — not a quoted figure, not a prose adjective.

## Prohibited Delegation Patterns

Do not create tasks that:

- delegate a vague goal without write scope, non-scope, and acceptance criteria
- give overlapping write scopes to parallel workers
- let a worker choose between competing contract or schema interpretations without prior approval
- omit the result report requirement
- continue with a third implementation brief after two failed attempts on the same surface; brief a Diagnostic Isolation task instead
- authorize a fallback, compatibility shim, or defensive patch before a diagnostic pass has confirmed the underlying owner is actually broken
- keep tuning a timeout, retry count, or budget after the worker reports the change had no observable effect; that signal is diagnostic, not tunable
- claim Unity compile/test success without actual Unity evidence
