# Local-AI Roadmap

**Status:** Active
**Last updated:** 2026-08-08
**Baseline:** `c0bb8fb` (`main`) — Sprint 3.3 explicit project-memory
prompt inclusion, 158 passing tests

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
  unified diffs.
- Approval-gated, clean-Git, atomic single-file apply with immediate source
  revalidation and stale-context clearing.
- Disclosed post-apply Git diff check, isolated Release build, and
  Release tests with stop-on-failure audit evidence.
- Explicit, approval-gated current-session rollback that restores the exact
  pre-apply bytes of the latest applied single file, followed by protected
  Git diff and Git status confirmation.
- Read-only project instruction manifest for root `AGENTS.md` and one explicitly
  selected `SKILL.md`, with visible precedence, budget, inclusion reasons, and
  deterministic response-evidence validation.
- User-approved local project memory for bounded Architecture, Command,
  Decision, and Known issue notes, with atomic local JSON persistence and
  separate consumed approval for every create, update, or delete.
- Explicit, session-only inclusion of at most one user-selected project-memory
  entry, with default exclusion, full-entry revalidation, visible token cost,
  approved prompt precedence, and deterministic evidence validation.
- 158 automated tests passing at the current baseline.

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

**Sprint 2.1 — Approval-gated single-file apply — Complete (`7c89faf`)**

- Apply only one currently displayed, validated in-memory preview.
- Require a separate one-run approval and consume it before any operation.
- Reuse the protected Git-status runner and reject a dirty or uncertain tree.
- Revalidate the repository, source path, links, exact source-byte snapshot,
  and unique ORIGINAL text immediately before writing.
- Preserve supported text encoding/BOM and line endings, and atomically replace
  the reviewed file using ignored `.local-ai` staging.
- Clear stale source context and require the user to run verification after a
  successful apply.

**Sprint 2.2 — Disclosed post-apply verification — Complete (`9e62121`)**

- Make the one-run apply approval explicitly include the fixed post-apply
  verification sequence.
- After a successful write, always run protected Git diff check.
- When exactly one root-contained .NET solution is detected, run the existing
  isolated Release build and then Release tests without restore.
- Stop on the first failure or cancellation, retain every result in the
  current-session audit, and state clearly that the source change remains
  applied.
- When no single solution is available, report build/tests as not run rather
  than inventing a result.

**Sprint 2.3 — Explicit current-session single-file rollback — Complete (`9758287`)**

**User-visible goal:** After one approved patch is applied, let the user
explicitly restore that exact file to its pre-apply bytes during the current
Local-AI session.

**In scope:**

- Retain the exact original file bytes, applied file bytes, repository identity,
  validated relative path, and hashes after a successful one-file apply.
- Show a separate `Approve one rollback` checkbox and
  `Rollback applied patch` action.
- Consume rollback approval before attempting any repository operation.
- Immediately before rollback, revalidate the selected repository, root
  containment, link safety, file path, and exact applied-byte snapshot.
- Reject rollback without writing when the repository changed, the file was
  externally edited, the path became unsafe, or the snapshot is unavailable.
- Atomically restore the exact original bytes, preserving encoding, BOM, and
  line endings.
- Run protected Git diff/status confirmation after restoration and retain the
  result in the current-session audit.
- Invalidate rollback availability after successful rollback, repository
  change, another patch apply, or application restart.

**Safety boundaries:**

- Roll back only the latest successfully applied single-file patch.
- Never use Git reset, checkout, commit, push, arbitrary shell commands, or
  model-directed rollback actions.
- Never repair, guess, or broaden a stale rollback record.
- Failure or cancellation must not perform a hidden fallback write.

**Acceptance criteria:**

- Test exact-byte restoration, approval consumption, external-edit rejection,
  repository/path/link mismatch rejection, cancellation, audit evidence, and
  rollback invalidation.
- Release build, all existing tests, new rollback tests, `git diff --check`,
  and final-diff review must pass.
- A disposable repository must prove approved `BEFORE -> AFTER` apply,
  approved `AFTER -> BEFORE` rollback, and a clean final Git state.

**Out of scope:** automatic rollback, rollback after restart, multi-file
rollback, persistent undo history, Git reset/checkout, commits, pushes,
arbitrary shell access, package restore, and unattended/model-directed actions.

**Deferred to later Phase 2 sprints:** multi-file transactions, rollback after
restart, commit, push, model-directed tools, and unattended apply.

- Add one explicit `Apply approved patch` action.
- Require a clean Git working tree or a user-chosen backup strategy.
- Re-check paths immediately before writing.
- Apply only the reviewed patch; never make extra model-directed edits.
- Automatically run configured verification after the apply.
- Keep a before/after diff and a clear rollback path.

**Phase 2 exit gate  Satisfied (`9758287`)**

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

**Sprint 3.1 — Read-only project instruction manifest — Complete (`b5f40f7`)**

**User-visible goal:** Before Local-AI sends an agent prompt, show the local
project instruction files that will be used, their precedence, size, estimated
token cost, and inclusion or exclusion reason.

**In scope:**

- Discover only repository-root `AGENTS.md` and files matching
  `skills/<name>/SKILL.md`.
- Display a read-only manifest containing instruction type, relative path,
  byte size, estimated tokens, inclusion state, and exclusion reason.
- Visibly enable a valid root `AGENTS.md` by default.
- Require the user to explicitly select at most one `SKILL.md`.
- Keep product safety boundaries highest. Within an allowed task, compose
  evidence in this order: user request, `AGENTS.md`, selected `SKILL.md`, then
  selected source and retained verification evidence.
- Enforce a combined instruction sub-budget of 8 KiB and approximately 2,000
  estimated tokens, with full-file inclusion only and no silent truncation.
- Reuse repository containment, link, secret, binary, text, and size
  protections.
- Clear instruction selections and manifest state when the repository changes.
- Show missing, unsafe, unsupported, or over-budget instruction files honestly
  instead of silently including or ignoring them.
- Reject a completed agent plan when it omits an exact included instruction or
  source path, or cites a same-extension repository path outside the displayed
  evidence manifest.

**Expected implementation scope:**

- Core instruction manifest models, discovery interface, and prompt composer.
- One read-only infrastructure discovery service.
- Manifest display and one-skill selection in `MainWindow` and its view model.
- Focused discovery, containment, precedence, budget, prompt, and view-model
  tests.
- Relevant README, architecture, decision, evaluation, and roadmap updates.

**Safety boundaries:**

- Instruction discovery and display are read-only.
- Instruction files cannot override product safety rules or the current user
  request.
- Never follow linked files outside the selected repository.
- Never infer, repair, generate, or rewrite missing instruction content.
- Never automatically choose a skill, execute instructions, apply patches,
  run tools, create memory, or contact a network service.

**Acceptance criteria:**

- Test deterministic discovery and ordering of root `AGENTS.md` and
  `skills/<name>/SKILL.md`.
- Test explicit one-skill selection, repository-change clearing, and manifest
  provenance.
- Test outside-root, linked, secret, binary, malformed, oversized, duplicate,
  and over-budget rejection.
- Test prompt precedence and confirm excluded instructions are never sent.
- Test that incomplete, abbreviated, or unlisted evidence paths cause the
  generated plan to be withheld rather than presented as grounded.
- A manual repository test must visibly show `AGENTS.md`, one selected skill,
  token estimates, precedence, and exclusion reasons.
- Release build, all existing and new tests, `git diff --check`, and final-diff
  review must pass.

**Out of scope:** persistent project memory, automatic or semantic skill
selection, multiple simultaneous skills, nested `AGENTS.md`, `CLAUDE.md` and
other instruction formats, instruction editing, skill creation, model training,
self-modification, cloud access, new tool permissions, repository writes,
commits, pushes, or unattended execution.

**Sprint 3.2 — User-approved local project memory store — Complete (8a8415c)**

**User-visible goal:** Let the user create, review, edit, and delete small
project-specific notes that remain available after restarting Local-AI, without
automatically sending those notes to the model.

**In scope:**

- Support four explicit note categories: Architecture, Command, Decision, and
  Known issue.
- Require the user to type the note title and content.
- Display every stored note with category, title, byte size, estimated tokens,
  and last-updated time.
- Store memory outside the selected repository under
  `%LOCALAPPDATA%\Local-AI\ProjectMemory\<repository-id>\memory.json`.
- Derive the project identity deterministically from the validated canonical
  repository path so different repositories never share memory.
- Require a separate, consumed one-run approval before every create, update, or
  delete operation.
- Use bounded UTF-8 JSON with atomic replacement and honest corruption errors.
- Allow at most 16 entries, 1 KiB per entry, and 8 KiB combined content.
- Clear the displayed memory state when the selected repository changes, then
  load only the newly selected repository memory.
- Keep memory read-only to the model during this sprint; stored notes are not
  added to prompts or source context.

**Expected implementation scope:**

- Core project-memory models, validation rules, and storage interface.
- One local JSON persistence service using the LocalAppData directory.
- A user-managed memory panel and one-run approval controls in the WPF view and
  view model.
- Focused persistence, repository-isolation, approval, corruption, budget,
  cancellation, restart, and view-model tests.
- Relevant README, architecture, decision, evaluation, and roadmap updates.

**Safety boundaries:**

- Never derive or save memory automatically from model responses or source code.
- Never write memory inside the selected repository.
- Never include stored memory in an AI prompt during Sprint 3.2.
- Never execute a stored command; command entries are plain text only.
- Never store credentials, tokens, secrets, environment values, or binary data.
- Never repair, guess, or silently discard malformed memory data.
- Never sync memory to Git, GitHub, a cloud service, or another repository.

**Acceptance criteria:**

- Test explicit approval consumption for create, update, and delete.
- Test persistence across restart and strict separation between repositories.
- Test atomic writes, malformed JSON, unsupported schema, duplicate identifiers,
  oversize entries, combined-budget rejection, and cancellation.
- Test that repository changes clear stale displayed memory.
- Test and manually confirm that stored notes are never sent in prompts.
- A disposable repository test must prove create, restart/load, edit, delete,
  and an unchanged Git working tree.
- Release build, all existing and new tests, `git diff --check`, and final-diff
  review must pass.

**Out of scope:** automatic memory creation, model-written notes, prompt
inclusion, semantic retrieval, embeddings, vector databases, memory import or
export, cross-project sharing, cloud synchronization, command execution, source
writes, commits, pushes, or unattended actions.

**Sprint 3.3 — Explicit project-memory prompt inclusion — Complete (`c0bb8fb`)**

**User-visible goal:** Let the user explicitly select one stored project-memory
entry to include in AI prompts, while keeping the default at no memory and
showing exactly what will be sent.

**In scope:**

- Default to no project-memory entry selected or sent.
- Allow explicit selection of at most one valid stored memory entry.
- Show the selected entry's category, title, byte size, estimated token cost,
  and inclusion state before every prompt.
- Keep the selection session-only and clear it when the repository changes,
  memory reloads, or the selected entry is updated or deleted.
- Revalidate the selected entry immediately before prompt composition.
- Compose evidence in this order: user request, root `AGENTS.md`, explicitly
  selected `SKILL.md`, explicitly selected project memory, selected source
  files, then retained verification evidence.
- Delimit memory as untrusted, user-managed project context that cannot override
  product safety, the current user request, repository instructions, or tool
  approval requirements.
- Give included memory a stable, visible evidence identity so the response can
  be checked deterministically without inventing another memory entry.
- Keep the existing 1 KiB entry limit and include the full selected entry only;
  never silently truncate memory content.

**Expected implementation scope:**

- Core selected-memory prompt evidence model and deterministic composer support.
- Session-only selection and visible inclusion summary in the desktop view model.
- One explicit memory selector in the existing Project memory panel.
- Response-evidence validation for the selected memory identity.
- Focused default-exclusion, explicit-selection, stale-selection, precedence,
  budget, repository-change, prompt-content, and view-model tests.
- Relevant README, architecture, decision, evaluation, and roadmap updates.

**Safety boundaries:**

- Never select, rank, summarize, infer, repair, or create memory automatically.
- Never include more than one memory entry in a prompt.
- Never execute a Command memory entry; all memory remains untrusted text.
- Memory cannot grant tool permission, approve a write, weaken containment,
  override instructions, or claim that a command or verification ran.
- Never send memory from another repository or an entry that changed after
  selection.
- Never modify memory as a side effect of prompting.

**Acceptance criteria:**

- Prove that memory is excluded from prompts by default.
- Prove that only the one explicitly selected entry is included.
- Show category, title, bytes, tokens, and inclusion state before sending.
- Test exact prompt precedence and stable memory evidence identity.
- Test repository-change, reload, update, delete, corruption, and stale-entry
  clearing.
- Test that Command memory remains text and never becomes a tool request.
- Test that memory cannot override safety or approval requirements.
- A disposable repository test must show default exclusion, explicit inclusion,
  deterministic evidence validation, clearing, and unchanged Git status.
- Release build, all existing and new tests, `git diff --check`, and final-diff
  review must pass.

**Out of scope:** automatic memory selection, multiple selected memories,
semantic retrieval, embeddings, vector databases, model-created memory,
summarization, prompt-driven memory changes, command execution, cross-project
memory, cloud synchronization, import or export, source writes, commits, pushes,
or unattended actions.
**Phase 3 exit gate — Satisfied (`c0bb8fb`)**

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

**Status:** None.
