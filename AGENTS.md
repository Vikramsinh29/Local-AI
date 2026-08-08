# Local AI - Agent Development Rules

## Purpose

This file is the authoritative operating guide for AI coding agents working in
this repository. Read it before inspecting, planning, or changing code.

Local AI is a completely local Windows coding assistant. It uses Ollama
and must not require paid API tokens or send prompts, source code, repository
content, telemetry, or credentials to external AI services.

## Technology and Repository

- Operating system: Windows 10 or later
- Language and framework: C# with .NET 10
- Desktop UI: WPF
- Architecture: MVVM
- Local model runtime: Ollama at `http://127.0.0.1:11434`
- Solution: `LocalAI.slnx`
- Core project: `src/LocalAI.Core`
- Infrastructure project: `src/LocalAI.Infrastructure`
- Desktop project: `src/LocalAI.Desktop`
- Tests: `tests/LocalAI.Tests`
- Shell: PowerShell

Never use another project, including MediaForge, as the LocalAI repository.

## Instruction Priority

1. Follow the user's current request.
2. Follow this `AGENTS.md`.
3. Follow existing architecture and established code patterns.
4. Prefer the smallest safe implementation when details are unspecified.

If the repository state conflicts with the request, stop before destructive
work and report the exact conflict.

## Non-Negotiable Architecture Rules

- Do not redesign or recreate the solution.
- Preserve the existing WPF and MVVM architecture.
- Keep views focused on presentation and binding.
- Keep UI coordination and observable state in ViewModels.
- Put contracts and shared models in `LocalAI.Core`.
- Put Ollama, repository, file-system, and other external implementations in
  `LocalAI.Infrastructure`.
- Make ViewModels depend on interfaces rather than concrete infrastructure
  implementations.
- Keep code-behind limited to view-specific behavior that cannot be expressed
  cleanly through binding or commands.
- Reuse existing models, services, commands, and helpers.
- Do not duplicate logic.
- Do not rename or break public APIs unless the requested feature requires it.
- Preserve backward compatibility and existing behavior.
- Do not modify unrelated files.

Dependency direction must remain:

```text
LocalAI.Desktop -> LocalAI.Core
LocalAI.Desktop -> LocalAI.Infrastructure (composition only)
LocalAI.Infrastructure -> LocalAI.Core
LocalAI.Core -> no Desktop or Infrastructure dependency
```

## Local-First and Repository Safety

- All AI generation must use the configured local Ollama service.
- Do not introduce cloud AI SDKs, hosted inference, paid APIs, analytics, or
  telemetry.
- Never expose prompts, source code, repository content, tokens, secrets, or
  local paths to external services.
- Repository inspection and context collection are read-only unless a future
  sprint explicitly adds a reviewed editing capability.
- Selected-repository writes are allowed only through the dedicated patch
  service for the exact in-memory preview after a separate one-run approval.
  Revalidate Git state, paths, links, and the source snapshot immediately
  before an atomic write.
- The apply approval may include only the disclosed fixed post-apply sequence:
  Git diff check and, when one solution is detected, isolated Release build and
  tests. Stop on failure or cancellation and never claim an unrun step passed.
- Rollback is limited to the latest successfully applied single file in the
  current application session. It requires a separate consumed approval and
  may restore only the retained exact original bytes after the repository,
  path, links, and exact applied bytes are revalidated. External edits must be
  rejected without writing. Never use Git reset or checkout for rollback.
- Never edit files in a repository selected through the LocalAI UI as part
  of repository inspection or context collection.
- Never execute commands inside a user-selected repository through read-only
  repository features.
- Never create commits in a user-selected repository through read-only
  repository features.
- Canonicalize and validate paths before reading.
- Reject paths outside the selected repository root.
- Reject linked or reparse-point paths when containment is uncertain.
- Exclude generated directories and files, binary files, secrets, and files
  that exceed configured limits.
- Apply both individual-file and total-context limits.
- Project instruction discovery may read only repository-root `AGENTS.md` and
  direct `skills/<name>/SKILL.md` files. Include root rules by default, require
  an explicit choice for at most one skill, preserve visible provenance, and
  apply the complete-file 8 KB / approximately 2,000-token sub-budget.
- Local instruction files never override product safety or the user's current
  request and must not add tool, write, network, commit, or push permissions.
- User-managed project memory must stay outside the selected repository under
  LocalAppData. Every create, update, or delete requires a separate consumed
  one-run approval. Keep it bounded, reject sensitive content, report corrupt
  storage honestly, and never add memory to prompts during Sprint 3.2.
- Cancellation must be supported for long-running work.

## UI Rules

- Preserve the existing ChatGPT-style interface.
- Keep the UI responsive; never block the WPF UI thread.
- Use `async` and `await` for I/O and long-running operations.
- Bind commands and observable state through the ViewModel.
- Clearly show important state, errors, cancellation, elapsed work, selected
  model, repository, and files included in AI context.
- New controls must remain usable at the window's supported minimum size.
- Follow existing colors, spacing, typography, and control patterns.
- Do not introduce a new design system during a feature sprint.
- Handle failures gracefully with actionable user-facing status text.

## Coding Standards

- Enable and respect nullable reference types.
- Follow existing naming and formatting conventions.
- Prefer readable production code over clever or overly abstract code.
- Keep classes focused and methods small.
- Use immutable records for data transfer when consistent with existing code.
- Validate public method arguments.
- Avoid magic values; use named constants or clearly owned configuration.
- Do not use synchronous blocking such as `.Result`, `.Wait()`, or long work on
  the UI thread.
- Dispose streams, HTTP responses, timers, and cancellation sources correctly.
- Do not swallow errors unless cleanup must not hide the original result.
- Do not add placeholder implementations, dead code, or speculative features.
- Do not add dependencies unless they are necessary and approved by the user.
- Add or update automated tests for new business logic and regressions.

## Required Workflow

One sprint equals one narrowly scoped feature, one verification cycle, and one
atomic commit when the user has requested a commit.

### 1. Establish the Baseline

Run these PowerShell commands before changing code:

```powershell
git status
git branch --show-current
dotnet build LocalAI.slnx -c Release
dotnet test LocalAI.slnx -c Release --no-build
```

Do not build on top of unexplained changes. Preserve user-owned changes and
never discard them.

### 2. Inspect Before Editing

- Read the relevant interfaces, models, implementations, ViewModels, views,
  composition code, and tests.
- Confirm the requested feature is not already implemented.
- Identify the smallest set of files that must change.
- Reuse the current architecture and naming patterns.
- State any material assumption when the repository does not answer it.

### 3. Branch Safely

- Check whether the requested branch already exists before creating it.
- Do not create a duplicate branch.
- Do not switch branches with uncommitted changes unless the user explicitly
  approves how those changes should be handled.
- Use fast-forward merges when the user specifically requests them.
- Do not pull, push, merge, rebase, reset, or delete branches unless requested.
- Never use destructive Git commands to make the working tree clean.

### 4. Implement One Feature

- Keep the sprint narrow.
- Modify only required files.
- Preserve all completed features.
- Keep repository access read-only unless explicitly authorized otherwise.
- Do not mix refactoring, formatting cleanup, dependency upgrades, or unrelated
  fixes into a feature sprint.

### 5. Verify

Run:

```powershell
dotnet build LocalAI.slnx -c Release
dotnet test LocalAI.slnx -c Release --no-build
git diff --check
git status --short
```

Also inspect the final diff for accidental or unrelated changes.

If formatting verification reports pre-existing issues in untouched files,
report them and leave those files unchanged.

### 6. Commit Only When Authorized and Green

Create a commit only when:

- the user requested or approved a commit;
- the build succeeds;
- all tests pass;
- no new compiler warnings are introduced;
- the staged diff contains only the requested sprint.

Use an atomic Conventional Commit message, for example:

```text
feat: add repository file context
fix: handle Ollama cancellation cleanly
test: cover repository path limits
docs: add agent development workflow
```

Never commit failing code. Never push unless the user explicitly requests it.

## Testing Expectations

- Test success paths, rejection paths, limits, cancellation, and regressions
  in proportion to the feature.
- File-system tests must use isolated temporary directories.
- Tests must not depend on a user's real repository.
- Tests must not require network access or a running Ollama instance unless
  they are explicitly integration tests.
- HTTP behavior should use controlled test handlers or fakes.
- Cleanup failures must not hide test results.
- Keep tests deterministic and independent.

## Definition of Done

A sprint is complete only when:

- the requested behavior is fully implemented;
- the existing architecture is preserved;
- repository and local-data safety requirements are maintained;
- relevant automated tests are added or updated;
- Release build succeeds;
- all tests pass;
- no new compiler warnings are introduced;
- unrelated files are unchanged;
- the working tree and commit state are clearly reported;
- documentation is updated when behavior or architecture materially changes.

## Agent Progress and Handoff

Keep updates concise and factual.

At completion, report:

- feature outcome;
- branch name;
- build result;
- test totals;
- commit hash and message, if committed;
- working-tree status;
- any known limitation or pre-existing issue.

Do not repeat completed setup or provide long generic explanations.
