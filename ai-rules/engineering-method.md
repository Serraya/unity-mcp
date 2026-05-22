# Engineering Method

## Purpose

Use this workflow for any task that changes behavior, architecture, transport, schemas, tests, packaging, or cross-file contracts.

## Core Workflow

1. Identify the user-visible contract being changed.
2. Trace the flow through every owning layer that participates in that contract.
3. Inspect sibling implementations and tests before designing the change.
4. Name the first invalid state transition, schema mismatch, routing error, or ownership conflict before editing.
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
- where the current behavior first becomes wrong;
- why the planned edit fixes that point rather than hiding a later symptom.

If those answers are not clear and the code exists locally, keep reading.

## Architecture Rules

- Do not optimize for smallest diff when the bug is an ownership or source-of-truth problem.
- Do not preserve duplicated registries, stale schemas, or parallel routing paths silently.
- Do not fix only stdio when the same contract is shared by HTTP/WebSocket.
- Do not fix only the Python wrapper when Unity discovery/handler metadata is the actual source of truth.
- Do not fix only C# dispatch when FastMCP registration is the actual user-facing schema surface.

## Ambiguity Rule

If multiple valid interpretations remain after reading the code, ask for the next decision or evidence. Do not silently pick a policy for API compatibility, transport scope, release process, or consumer app pinning.
