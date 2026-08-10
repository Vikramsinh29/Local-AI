# Local-AI Architecture

## Technology

- .NET 10
- WPF desktop UI
- MVVM presentation pattern
- Ollama local runtime at `http://127.0.0.1:11434`
- xUnit tests

## Projects and dependency direction

```text
LocalAI.Desktop -> LocalAI.Infrastructure -> LocalAI.Core
LocalAI.Desktop -> LocalAI.Core
LocalAI.Core -> no Desktop or Infrastructure dependency
```

`LocalAI.Desktop` may reference Infrastructure only to compose the application.
ViewModels depend on Core interfaces, never concrete Infrastructure services.

## Responsibility map

| Area | Location | Responsibility |
|---|---|---|
| Contracts and models | `src/LocalAI.Core` | Interfaces, immutable models, shared rules |
| External services | `src/LocalAI.Infrastructure` | Ollama HTTP, read-only repository/context and instruction-manifest access, bounded LocalAppData project-memory persistence, fixed verification execution, approval-gated patch writes, and bounded local evaluation report I/O |
| UI coordination | `src/LocalAI.Desktop/ViewModels` | Observable state, one-run approval, commands, cancellation, status, and session audit |
| UI presentation | `src/LocalAI.Desktop` | XAML bindings and view-specific code-behind only |
| Tests | `tests/LocalAI.Tests` | Deterministic unit and service behavior tests |

## Safety boundary

Selected repositories are read-only by default. Repository inspection must
canonicalize paths, preserve root containment, reject uncertain links, exclude
secrets and generated/binary files, and enforce file/context limits.

Verification execution crosses a separate controlled boundary:

- Core defines the fixed tool identities, immutable results, and runner
  contract.
- Infrastructure maps each identity to an exact executable and argument list
  through `ProcessStartInfo.ArgumentList`; it never invokes a command shell.
- Desktop requires an Agent-mode approval that is consumed by exactly one run,
  streams bounded output, supports cancellation, and retains only the current
  session audit.
- Git tools disable file-system monitor hooks, external diffs, text conversion,
  terminal prompts, pagers, and optional index locks. Linked Git metadata is
  rejected.
- .NET build/test run only the single detected root-contained solution, with
  package restore disabled. Binary outputs are isolated under the ignored
  `.local-ai/verification` directory so the running desktop application does
  not lock verification output. Existing project `obj` restore metadata is
  reused. Local-AI state is excluded from the repository tree and source
  context. These tools are permitted only for a repository the user trusts
  because MSBuild targets can execute project code and write generated output.

Proposed patches cross a separate model-output boundary:

- Core builds an evidence-bounded `LOCAL_AI_PATCH_V1` request and extracts
  exactly one complete marked proposal. Ordinary surrounding explanation is
  ignored, but Markdown-wrapped, duplicated, or malformed envelopes are
  rejected rather than repaired.
- The model supplies exact ORIGINAL and REPLACEMENT text instead of constructing
  diff grammar. The parser requires the original to occur exactly once in a
  selected evidence file and rejects duplicate, protected, escaping,
  unavailable, unselected, or linked paths.
- If exact matching fails, the parser may ignore leading indentation only. The
  remaining text must match one unique, line-count-preserving source fragment;
  Local-AI then restores the file's actual indentation.
- Core constructs the displayed diff only after that evidence validation.
- Desktop keeps the proposal in memory and renders a unified preview with an
  explicit `Preview only — not applied` label.
- No Core, Infrastructure, Desktop, or model-directed code path writes the
  proposal to the selected repository during Phase 1.

Approved patch application crosses a distinct Phase 2 write boundary:

- Core retains the reviewed ORIGINAL, REPLACEMENT, relative path, and raw-file
  SHA-256 snapshot in the in-memory preview and defines the patch-service
  contract.
- Desktop exposes a separate one-run approval and consumes it before reusing
  the protected Git-status runner. Dirty, failed, cancelled, or structurally
  uncertain Git output stops the apply.
- Infrastructure accepts exactly one reviewed file, revalidates the local Git
  root, shared path rules, links, raw-file snapshot, and unique ORIGINAL text,
  then performs one atomic replacement through non-linked ignored
  `.local-ai/apply` staging.
- Supported BOM/encoding and line-ending style are preserved. Successful apply
  clears stale source context and explicitly requires later verification.
- Sprint 2.1 contains no multi-file transaction, automatic verification,
  rollback UI, commit, push, arbitrary shell, or model-directed write path.

Post-apply verification reuses only the fixed verification boundary:

- The apply approval visibly includes post-apply verification and is still
  consumed before the clean-Git preflight.
- A successful write always runs Git diff check. When exactly one valid .NET
  solution is detected, the same isolated Release build and Release tests run
  sequentially without restore.
- The sequence stops after the first failed or cancelled step. Every completed,
  failed, or cancelled step remains in the current-session audit.
- A failed, unavailable, or cancelled verification never becomes a success
  claim and does not silently roll back the already-applied source file.
- Sprint 2.2 adds no new executable, command arguments, restore, commit, push,
  rollback, multi-file transaction, or unattended action.

Current-session rollback crosses a second explicit write boundary:

- A successful one-file apply retains an in-memory record containing the
  canonical repository root, validated relative path, exact original and
  applied bytes, and both SHA-256 hashes. Nothing is persisted across restart.
- Desktop exposes a separate `Approve one rollback` control, consumes that
  approval before the attempt, and permits only the latest successful apply.
- Infrastructure revalidates the same repository and Git metadata, shared path
  and link rules, and an exact byte-for-byte match with the applied snapshot.
  A changed repository, path, link, or file is rejected without writing.
- A valid rollback atomically restores the retained original bytes through the
  same protected `.local-ai/apply` staging boundary, preserving the original
  encoding, BOM, and line endings exactly.
- After restoration, Desktop runs only protected Git diff check and Git status,
  retains both results in the current-session audit, and reports an uncertain
  or dirty final state honestly. Confirmation failure does not undo the
  already-completed rollback.
- The record is invalidated after successful rollback, repository refresh or
  change, a newer preview/apply, or application restart. Sprint 2.3 adds no Git
  reset/checkout, persistent history, multi-file undo, commit, push, arbitrary
  command, model-directed action, or automatic rollback.

Project instructions remain inside the read-only repository boundary:

- Infrastructure discovers only root `AGENTS.md` and direct
  `skills/<name>/SKILL.md` candidates. It canonicalizes paths, refuses linked
  paths, reads only complete UTF-8 text files, and records exclusions instead
  of silently repairing or truncating content.
- Core deterministically includes a valid root `AGENTS.md`, permits at most one
  explicitly selected skill, and enforces a combined 8 KB and approximately
  2,000-token instruction budget.
- Agent prompt evidence is ordered as user request, included `AGENTS.md`, the
  selected included skill, one explicitly selected project-memory entry,
  selected source files, and retained verification evidence. Product safety
  rules remain outside and above that evidence.
- Core validates a completed agent plan against the exact instruction and
  source paths sent with that request. Desktop withholds the plan when required
  paths are missing or a same-extension unlisted path is cited.
- Desktop displays path, type, byte size, token estimate, inclusion state, and
  reason; it clears the manifest and skill selection whenever the repository is
  reloaded or changed.
- Sprint 3.1 adds no automatic skill selection, persistent memory, instruction
  editing, repository writes, tools, network access, commit, or push path.

Project memory crosses a separate local-state boundary, not the repository or
model boundary:

- Core defines immutable memory entries, the four supported categories, load
  and mutation results, and the asynchronous persistence contract.
- Infrastructure derives a deterministic SHA-256 repository identity from the
  validated canonical root and stores versioned UTF-8 JSON below
  `%LOCALAPPDATA%\Local-AI\ProjectMemory`, never inside the repository.
- The service validates the complete store before any mutation, rejects linked
  roots, sensitive or binary/control content, unsupported schema, duplicate
  IDs, and over-budget data, and performs same-directory atomic replacement.
- The limits are 16 entries, 1 KiB per complete title/content entry, 8 KiB and
  approximately 2,000 estimated tokens combined. Entries are never truncated.
- Desktop loads only the selected repository's memory, displays provenance and
  metadata, clears stale state on repository reload, and consumes a distinct
  one-run approval before each create, update, or delete.
- Prompt-memory selection is separate from the editor selection, defaults to
  none, permits at most one entry, and remains session-only. Repository reload,
  repository change, selected-entry update, or deletion clears the selection.
- Immediately before composing an agent prompt, Desktop reloads the repository's
  memory and requires an exact immutable match. Missing, changed, corrupt, or
  cross-repository evidence is cleared and generation does not start.
- Core delimits the complete selected entry as untrusted user-managed context
  after project instructions and before source evidence. It exposes the stable
  identity `project-memory:<entry-id>` and requires the response to cite that
  exact identity.
- Command entries remain inert text and never become a tool request. Sprint 3.3
  adds no automatic selection, multiple-memory inclusion, model-written memory,
  tool permission, repository write, Git, network, or unattended path.

## Change rules

1. Start from the smallest affected layer.
2. Put shared contracts in Core before implementations.
3. Keep I/O asynchronous and cancelable.
4. Preserve the existing UI design and binding patterns.
5. Add focused tests for normal, rejected, cancellation, and failure paths.
6. Verify with the commands in `README.md` before committing.

## Sprint 4.1 deterministic evaluation boundary

The evaluation path is intentionally separate from the interactive desktop
agent. `JsonEvaluationFixtureLoader` validates versioned top-level case
definitions and all referenced fixture paths. `DeterministicEvaluationRunner`
scores recorded outputs through fixed rules and reuses the product's existing
evidence and structured-patch validators. `LocalEvaluationReportWriter` writes
one bounded JSON report and one bounded Markdown report to a direct run folder
under the configured local evaluation root.

The console host accepts only the documented fixed arguments. It has no Ollama
client, network client, shell runner, repository writer, project-memory writer,
or Git publishing dependency. The five aggregate metrics are plan correctness,
evidence grounding, file-selection precision, structured-patch validity, and
unsafe-action rejection. Each report records the case schema, evaluator schema,
product commit, recorded model label, profile label, duration, fixture paths,
per-case findings, safety labels, and aggregate scores.

Fixture failures are domain results and remain reportable. Loader, containment,
schema, and output failures are infrastructure failures and stop the command.

## Sprint 4.2 deterministic comparison boundary

Candidate comparison remains outside the interactive Desktop agent. The local
report loader accepts exactly the two explicitly named JSON reports below the
fixed `.local-ai/evaluations` root. It rejects unsupported evaluator or report
schemas, malformed or oversized data, duplicate or incomplete cases and
metrics, escaping paths, and linked path components before any recommendation
is calculated.

Core compares stable case identifiers and the five Sprint 4.1 metric summaries
only after evaluator schema, product commit, and fixture-set identity match. It
records absolute metric deltas, deterministic directions, safety preservation,
and reported-duration change. Eligibility requires one quality improvement, no
quality regression, preserved unsafe-action rejection, and no more than a 20
percent reported-duration increase. Every gate remains visible even when one
fails.

The comparison writer emits one bounded JSON report and one bounded Markdown
report under `.local-ai/evaluations/comparisons/<comparison-id>`. Input hashes,
run IDs, labels, commits, schemas, and case-set identity are retained. The
console command has no Ollama, network, repository writer, settings writer,
project-memory writer, Git publication, or candidate-output execution path.
`Eligible for user review` is advisory and cannot change a model or profile.

## Sprint 5.1 bounded repository-search boundary

Core searches only the immutable repository tree already returned by the
bounded inspector. It compares a trimmed query with file names and relative
paths, ranks exact and name matches before path-only matches, orders ties
deterministically, and returns at most 50 results. Directories are not results.

Desktop owns the query, visible result list, selection, and clear state. A
repository reload clears every result. Search never reads a file or sends data
to Ollama. Only a separate explicit action passes one selected relative path to
the existing repository file-context service, which revalidates containment,
links, generated/binary/secret exclusions, UTF-8, and byte budgets.

## Sprint 5.2 bounded single-file content-search boundary

Desktop permits literal content search only after the user selects a file
already accepted into context. Immediately before every search it re-reads that
same relative path through `IRepositoryFileContextService`; a failed safety
revalidation produces no results. Core performs deterministic case-insensitive
line matching on the returned immutable content and emits at most 20 previews,
each bounded to 240 source characters and labeled with its one-based line.

Search results are display-only. They are cleared when the selected context
file or repository state changes and cannot add context, alter a prompt, invoke
Ollama, create an index, write the repository, or cross into another file.

## Sprint 5.3 bounded multi-file content-search boundary

Desktop searches multiple files only through an explicit action and only when
1–5 files are already selected for context. It re-reads each relative path in
deterministic order through `IRepositoryFileContextService`, carrying the
cumulative byte count forward. Any failed revalidation stops the whole search
and clears results.

Core emits at most 10 literal matches per file and 50 total, ordered by path
and line. Results are display-only and cannot discover another file, alter
context or prompts, invoke a model or network, persist an index, or write.
