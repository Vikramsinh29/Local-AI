# Local-AI Roadmap

**Status:** Active
**Last updated:** 2026-08-07
**Baseline:** `bea4850` (`main`) — Sprint 1.3 structured proposed patch
preview, 87 passing tests

## Purpose

Local-AI is a private, local-first Windows coding assistant powered only by
Ollama. This roadmap is the scope contract for development. It keeps each
sprint small, evidence-based, and safe.

## Product Principles

1. **Local first.** Prompts, source code, repository context, credentials, and
   telemetry must not leave the computer.
2. **Evidence before claims.** Local-AI must read the relevant source or show
   a tool result before claiming a fact about a project, build, test, or file.
3. **Human approval for writes.** It must never edit a selected repository,
   run a modifying command, commit, or push without an explicit user approval.
4. **Small, reversible changes.** One feature per sprint; use a Git branch and
   show the exact diff before any future apply operation.
5. **Verification is mandatory.** A feature is complete only after a Release
   build, relevant tests, `git diff --check`, and a review of the final diff.
6. **No invented certainty.** When evidence is missing, Local-AI must say so,
   state what it needs to inspect, and avoid guessing.

## Current Capability Baseline

- WPF / MVVM desktop application on .NET 10.
- Local Ollama model discovery, selection, streaming chat, cancellation, and
  startup resilience.
- Fast, Balanced, and Accurate generation profiles.
- Read-only repository selection, tree inspection, and manually selected file
  context with secret, binary, path-containment, and size protections.
- Controlled Agent mode that produces an evidence-bounded, read-only plan and
  explicitly states that no source changes were applied.
- Four fixed, one-run-approved Git/build/test tools with isolated output,
  cancellation, bounded live output, and an in-session audit.
- Strict, evidence-grounded structured patch previews with locally constructed
  unified diffs and no Phase 1 repository write path.
- 87 automated tests passing at the current baseline.

## Roadmap

### Phase 1 — Controlled Read-Only Agent

**Goal:** Turn the current chat into a transparent, evidence-driven assistant
for a selected repository, without allowing it to change anything.

**Sprint 1.1 — Agent plan and evidence — Complete (`5c78241`)**

- Add an Agent mode alongside normal chat.
- Let it inspect selected files and repository metadata through existing
  read-only services.
- Require it to produce: understanding, assumptions, step-by-step plan,
  affected files, and evidence links before proposing work.
- Display an explicit `No changes applied` status.

**Sprint 1.2 — Approved verification tools — Complete (`61b0435`)**

- Add a strictly allow-listed tool runner for read-only Git commands and
  configured `dotnet build`, `dotnet test`, and lint commands.
- Validate the selected repository root and command arguments.
- Stream command output to the UI, support cancellation, and keep an audit
  record in the current session.
- Never allow shell interpolation, arbitrary executables, network commands,
  package installation, file deletion, commits, or pushes.

**Sprint 1.3 — Proposed patch preview — Complete (`bea4850`)**

- Ask the model for a structured proposed patch, not a direct file write.
- Validate every proposed path remains inside the selected repository.
- Render a side-by-side or unified diff preview with changed-file summaries.
- Mark every proposal as `Preview only — not applied`.

**Phase 1 exit gate**

- No selected-repository write path exists in code.
- All agent claims cite inspected files or command output.
- Tool allow-list, cancellation, root containment, and malformed model output
  have automated tests.

### Phase 2 — User-Approved Safe Apply

**Goal:** Allow useful code changes while preserving user control.

**Sprint 2.1 — Approval-gated single-file apply — Active**

- Apply only one currently displayed, validated in-memory preview.
- Require a separate one-run approval and consume it before any operation.
- Reuse the protected Git-status runner and reject a dirty or uncertain tree.
- Revalidate the repository, source path, links, exact source-byte snapshot,
  and unique ORIGINAL text immediately before writing.
- Preserve supported text encoding/BOM and line endings, and atomically replace
  the reviewed file using ignored `.local-ai` staging.
- Clear stale source context and require the user to run verification after a
  successful apply.

**Deferred to later Phase 2 sprints:** multi-file transactions, automatic
post-apply verification, integrated rollback, commit, push, model-directed
tools, and unattended apply.

- Add one explicit `Apply approved patch` action.
- Require a clean Git working tree or a user-chosen backup strategy.
- Re-check paths immediately before writing.
- Apply only the reviewed patch; never make extra model-directed edits.
- Automatically run configured verification after the apply.
- Keep a before/after diff and a clear rollback path.

**Phase 2 exit gate**

- No patch can apply without an approval click.
- Failed patch application is atomic or safely rolled back.
- Build/test outcome and exact changed files are shown after every apply.

### Phase 3 — Project Memory and Reusable Skills

**Goal:** Make Local-AI faster and more consistent across future projects.

- Store local, project-scoped memory for architecture, commands, decisions,
  and known issues.
- Read project instruction files in this order: user request, `AGENTS.md`,
  project rules, relevant `SKILL.md`, then source and test evidence.
- Add versioned `SKILL.md` workflows for repeatable tasks such as bug fixing,
  feature work, test failure diagnosis, and release checks.
- Let users review, edit, export, and delete all stored memory.

**Phase 3 exit gate**

- Project memory stays local and is visibly attributable to its source.
- A skill cannot override repository safety rules or user instructions.

### Phase 4 — Quality and Evaluation

**Goal:** Measure improvement instead of assuming it.

- Create a local evaluation suite from representative Local-AI tasks.
- Measure plan correctness, file-selection precision, patch validity,
  build/test pass rate, and unsafe-tool rejection rate.
- Compare candidate models and generation profiles against the same suite.
- Promote a model/profile only when it improves measured results without
  regressing safety or latency beyond agreed limits.

**Phase 4 exit gate**

- Every future upgrade has a before/after evaluation report.
- “Better” means measurable improvement, not a subjective model claim.

### Phase 5 — Optional Advanced Capabilities

Only begin after Phases 1–4 are stable.

- Multi-step task execution with bounded tool loops.
- Repository search/indexing for large projects.
- Local documentation retrieval.
- Optional self-hosted model upgrades and custom Ollama Modelfiles.

## Anti-Hallucination Operating Rules

- Do not infer source code that has not been read.
- Treat model output as a proposal, never as proof.
- Use structured outputs for plans, tool requests, and patch proposals.
- Reject unknown tools and malformed tool arguments.
- Prefer a failed-but-honest verification result over a confident guess.
- State uncertainty and request the smallest missing piece of evidence.
- Preserve raw tool output so users can inspect the basis for conclusions.

## Sprint Template

Every sprint must define:

1. One user-visible goal.
2. Explicit in-scope and out-of-scope items.
3. The smallest files expected to change.
4. Safety boundaries and approval points.
5. Automated tests for success, failure, cancellation, and regressions.
6. Exact verification commands and acceptance criteria.
7. One atomic Conventional Commit after verification passes.

## Current Active Sprint

**Name:** Approval-Gated Single-File Apply
**Scope:** Phase 2, Sprint 2.1 only.

Add one explicit apply action for exactly one currently displayed structured
preview. Require a separate one-run approval; consume it before running the
protected Git-status check; reject dirty or uncertain Git output; and
revalidate root containment, linked paths, protected paths, the exact raw-file
snapshot, and unique ORIGINAL text immediately before an atomic replacement.
Preserve supported encoding/BOM and line endings. Stage temporary data only in
ignored, non-linked `.local-ai/apply`, clear stale source context after success,
and state that verification is still required. Do not add multi-file apply,
automatic verification, rollback UI, commit, push, arbitrary commands, package
restore, project memory, automatic retries, or unattended/model-directed apply.
