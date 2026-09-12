# Engineering Method

## Purpose

Use this workflow for any task that changes behavior, architecture, transport, schemas, tests, packaging, or cross-file contracts.

## Requested Changes And Simplification

Continue within the approved objective, authority, write scope, protected
behavior, side effects and stop conditions. Diagnosis to an already-authorized
repair, or approved plan to implementation, does not require another approval.
Ask only for an unresolved decision or changed boundary. Diagnostic-only scope
stays diagnostic-only; product, backend, live-data and deployment authority do
not expand implicitly.

An explicit behavior-change request supplies the target; it does not require
proving the old behavior defective. For a bounded removal or simplification,
inspect the owner and actual dependencies, remove obsolete support within scope,
and verify the remaining behavior. Use this route instead of diagnostic proof
blocks, architecture assessments, or KB research unless unresolved dependencies
or ownership changes need them. Consequential data, security, compatibility,
and external-action checks still apply.

Before adding filters, aliases, flags, or fallbacks to retain disputed behavior,
name the current requirement or real consumer it serves. Existing code or
backend fields alone do not justify retention. If none supports it, recommend
removal. Clarify only uncertainty that materially changes scope or risk; do not
reopen an explicit decision.

## Core Workflow

For other non-trivial changes:

1. Identify the user-visible contract being changed.
2. Trace the flow through every owning layer that participates in that contract.
3. Inspect sibling implementations and tests before designing the change.
4. For a bug fix, locate the first invalid transition, schema mismatch, routing error, or ownership conflict; for a requested change, name the intended difference.
5. Choose the owner that should enforce the invariant.
6. Implement the fix at that owner, not at a downstream symptom site.
7. Verify with the narrowest tests or runtime evidence that proves the contract.
8. Record durable project facts in `ai-rules/knowledge/*.md` when future work should not rediscover them.

## Required Pre-Patch Understanding

Before changing non-trivial behavior, be able to state:

- the intended invariant;
- the authoritative owner;
- the source of truth for the data/schema/state;
- the entry point that starts the flow;
- each boundary crossed by the flow;
- the intended behavior difference, or the first failure for a bug fix;
- why the edit belongs at that owner.

If those answers are not clear and the code exists locally, keep reading.

## Architecture Rules

- Do not optimize for smallest diff when the bug is an ownership or source-of-truth problem.
- Do not preserve duplicated registries, stale schemas, or parallel routing paths silently.
- Do not fix only stdio when the same contract is shared by HTTP/WebSocket.
- Do not fix only the Python wrapper when Unity discovery/handler metadata is the actual source of truth.
- Do not fix only C# dispatch when FastMCP registration is the actual user-facing schema surface.
- If a proposal works only by tuning timeouts, retries, buffer sizes, or budgets around a known ownership, ordering, or contract problem, treat that as evidence the fix is incomplete and keep refactoring.
- Treat package pins, compatibility paths, registry exceptions, guards,
  exclusions, fallbacks, and legacy transports as decisions to verify, not
  permanent constraints. Trace their provenance and current consumers; remove
  an obsolete restriction and its dead dependents inside the approved
  owner/write boundary instead of routing around it. Published compatibility
  and consumer-app pin boundaries remain binding until explicitly changed.

## Minimal-Fix Gate

"Minimal change" is valid only after the ownership check passes. Before choosing a narrow fix, verify all of these:

- the authoritative owner and source of truth are clear;
- the subsystem is not already split across competing registries, duplicated schemas, or parallel legacy/replacement paths;
- the same class of symptom has not recurred in this subsystem;
- the fix does not add a new fallback, normalization layer, compatibility shim, or transport special case where a shared owner already exists.

If any item fails, name the ownership gap and make the smallest architectural correction that removes it. Do not justify preserving debt with "minimal change" in a subsystem that is already repeatedly patched.

## Patch Classification

For bug work, classify the planned change in plain language:

- **Architectural fix** - removes the failure mode at the owning layer.
- **Quick patch** - narrows, avoids, or stabilizes the failure without fixing the owner.
- **Temporary diagnostic** - adds evidence only; never presented as a behavior fix.

Do not let a quick patch look like an architectural fix. When choosing a quick patch, state what it does not solve, why the architectural fix is deferred, and what follow-up would make it architectural. If the user did not ask for a workaround and the owning layer is clear, do the architectural fix.

## Explicitly Forbidden Patterns

Do not solve behavior or contract issues by:

- absorbing a schema or naming mismatch at a downstream layer (client normalization, wrapper aliasing, response rewriting) instead of fixing the metadata source;
- patching the Python wrapper to compensate for C# handler behavior, or vice versa, instead of aligning the shared contract;
- adding retries, timeouts, delays, or reconnect loops to mask registration ordering, domain-reload, or lifecycle problems;
- fixing one transport path and special-casing the other;
- broad catch-and-continue that keeps a tool "working" past an invalid state instead of returning a structured error;
- keeping legacy and replacement paths both active "for safety" after the replacement is live;
- layering a second discovery/registration/normalization mechanism on top of an existing owner.

These are allowed only as temporary workarounds the user explicitly asked for, clearly labeled as such.

That list names the evasions seen so far, not the boundary. The rule is the class: do not make a symptom disappear without fixing the owner. A technique nobody has written down yet is not thereby allowed — if a change would leave the broken ownership model active while the repro looks better, it belongs here whether or not it is listed.

## Refactor Trigger

If two fixes in the same subsystem are required to address symptoms of the same bug or change class, stop and refactor around the real ownership boundary instead of applying a third patch.

Examples:

- repeated C#/Python schema drift -> make one metadata source authoritative
- repeated stdio/HTTP divergence fixes -> align the shared contract or dispatch layer
- repeated alias/casing patches -> fix the naming contract at its source
- repeated registration/visibility fixes -> consolidate registry ownership

## Growth Trigger

The Refactor Trigger is bug-driven. This trigger is feature-driven: successive successful feature passes can keep adding responsibilities to one owner without ever failing, so the bug-driven trigger never fires while the architecture degrades. The motivating case in this repository is `RecordedFrameAnalysisOps` growing request parsing, Profiler scanning, aggregation, response mapping, CSV/file I/O, and a job scheduler across two feature passes.

Before adding a materially new capability to an existing owner, stop and produce a module-boundary plan if either holds:

- this would be the third distinct growth pass on the same owner (two prior tasks or commits already materially expanded it with different capabilities)
- the change adds a new responsibility category — lifecycle/concurrency, I/O/persistence, integration/transport, serialization/response mapping, or domain computation — to an owner that already spans two or more of those categories

A fired trigger requires an explicit keep / extract-first / split-into-follow-up decision backed by evidence (current responsibilities, growth history, available test seams), not an automatic split. Keeping the owner is valid when it has one coherent invariant and a direct test seam. File size alone is a review signal, not a trigger.

## No-Effect Patch Gate

If a change should observably affect behavior, schema, or output but the next check shows no effect, stop the patch loop. First prove which layer actually ran:

- the MCP client is talking to the edited server code, not a stale stdio process or an old negotiated tool list;
- the consumer project imports this local fork, not a published package pin or a PackageCache copy;
- Unity recompiled and re-registered after the C# edit (domain reload completed, discovery re-ran);
- the request went through the transport path that was edited (stdio vs HTTP/WebSocket);
- the test or probe exercises the edited layer rather than a stub or fixture.

After a no-effect edit, the next action is diagnostics or a blocker report, not another blind patch.

A no-effect result is not automatic permission to revert. If the edit is a valid in-scope correction, keep it and report that it did not resolve the current repro. If it is out of scope or unverified, ask before undoing it.

## Useful Fixes That Do Not Solve The Current Repro

- Do not quietly revert a valid fix, diagnostic improvement, or cleanup just because it did not resolve the current bug.
- If it is correct and in scope, keep it and report it as adjacent progress while the investigation continues.
- If it caused a regression or violates scope, revert it explicitly and state the evidence.
- If it is valuable but outside the current write scope, ask whether to keep, split, or revert it.

## Ambiguity Rule

If multiple valid interpretations remain after reading the code, ask for the next decision or evidence. Do not silently pick a policy for API compatibility, transport scope, release process, or consumer app pinning.

## Source And Decision Records

Preserve original feedback wording, author and recoverable source reference
before triage or migration. Attribute interpretations and later requests
separately. Retain consequential approvals, holds, rejected attempts and recorded
reasons with date, scope and supersession; unknown reasons stay unknown. Proposed
approval text is not a user decision.

Each current claim/decision has one owning record; routers link there and
handoffs identify their snapshot date. Before removing history, verify its
durable destination, including never-committed work. Update status without
rewriting original requests or decisions. This requires neither a conversation
archive nor a new register for trivial work.
