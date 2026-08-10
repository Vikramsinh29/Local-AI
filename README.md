# Local-AI

Local-AI is a private Windows coding assistant that uses Ollama on the local
computer. It can chat with a local model and safely inspect a selected source
repository without sending prompts, source code, credentials, or telemetry to
an external AI service.

## Current capability

- Local Ollama model discovery, selection, streaming responses, cancellation,
  and connection recovery.
- Fast, Balanced, and Accurate generation profiles.
- Read-only repository selection, tree inspection, and selected-file context.
- Repository safety controls for secrets, binary/generated files, paths outside
  the selected root, linked roots, and oversized context.
- Controlled Agent mode that creates an evidence-based implementation plan
  without changing repository files.
- Four fixed verification tools with fresh one-run approval, isolated build
  output, streamed results, cancellation, and a current-session audit.
- Strict structured patch previews grounded in selected source evidence.
- One-file patch application with a separate one-run approval, clean-Git
  requirement, source-snapshot revalidation, and atomic replacement.
- Disclosed post-apply Git diff check plus isolated Release build/tests when
  exactly one .NET solution is detected, with stop-on-failure audit evidence.
- Explicit current-session rollback for only the latest applied file, using a
  separate one-run approval, exact-byte revalidation/restoration, and protected
  Git diff/status confirmation.
- Read-only project instruction manifest for repository-root `AGENTS.md` and
  explicitly selected `skills/<name>/SKILL.md`, with provenance, exclusion
  reasons, a strict 8 KB / approximately 2,000-token sub-budget, and a
  fail-closed response gate for missing or unlisted evidence paths.
- User-managed project memory for Architecture, Command, Decision, and Known
  issue notes. Memory is stored outside the repository in LocalAppData, is
  bounded to 16 entries / 1 KiB each / 8 KiB and approximately 2,000 tokens
  combined, and requires a fresh one-run approval for every change.
- Explicit session-only inclusion of at most one selected memory entry, with
  default exclusion, visible category/title/byte/token provenance, immediate
  pre-send revalidation, and deterministic response-evidence validation.
- A deterministic offline comparison of exactly two compatible evaluation
  reports, with case and metric deltas, explicit provenance and safety gates,
  a 20-percent reported-duration limit, and an advisory-only recommendation.
- Bounded, deterministic search across already-inspected repository file names
  and relative paths, with explicit selection before the existing protected
  context reader can add a result to an AI prompt.

The current roadmap is in [ROADMAP.md](ROADMAP.md). Sprint 5.1 is complete at
`18741b6`: bounded, read-only search over the already-inspected repository tree.
No implementation sprint is currently active; further optional Phase 5 work
requires a separately defined and approved scope.
Local-AI does not provide an arbitrary terminal and must not restore packages,
commit, push, or apply model output that was not separately reviewed and
approved.

## Requirements

- Windows 10 or later
- .NET SDK 10
- Ollama running at `http://127.0.0.1:11434`
- At least one locally installed Ollama model

## Build and test

```powershell
dotnet build LocalAI.slnx -c Release
dotnet test LocalAI.slnx -c Release --no-build
```

## Windows PowerShell package reliability

Repository automation ZIPs must be validated with Windows PowerShell 5.1
before they are shared. Parse every packaged `.ps1` file and reject any parser
error. In expandable strings, delimit a variable followed by a colon, for
example `"${Description}:"`, rather than `"$Description:"`. With
`$ErrorActionPreference = "Stop"`, avoid native commands that intentionally
return a non-zero exit code or write expected errors; use success-returning
queries such as `git ls-tree` for existence checks. Every corrected ZIP must
have a new filename and published SHA-256, and a wrapper may print success only
after the child process has returned exit code zero.

If a package adds or changes a project, solution, package reference, target, or
build input, it must run and validate an explicit `dotnet restore` before any
build or test command that uses `--no-restore`. A recovery run after an
interrupted payload copy must repeat that restore gate; it must not assume an
existing `obj/project.assets.json` belongs to the current project graph.

## Repository map

- `src/LocalAI.Core` — contracts, models, and shared domain logic
- `src/LocalAI.Infrastructure` — Ollama and file-system implementations
- `src/LocalAI.Desktop` — WPF UI, commands, and ViewModels
- `tests/LocalAI.Tests` — automated tests

Sprint 5.2 adds an explicit, bounded plain-text search within one selected
context file. The file is revalidated through the existing protected context
reader immediately before each search; results are limited to 20 line-numbered
previews and are never added to prompts automatically.

**Sprint 5.2 status:** Complete at `d1869be`.

Sprint 5.3 extends the same protected boundary with an explicit **Search all**
action across 1–5 files already selected for context. Every file is revalidated
before searching; output is limited to 10 matches per file and 50 total.

Read [AGENTS.md](AGENTS.md) before making changes. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for dependency rules and
[docs/DECISIONS.md](docs/DECISIONS.md) for settled decisions.

## Deterministic offline evaluations

Sprint 4.1 adds a versioned, local-only evaluation suite under
`evaluations/fixtures/v1`. It scores recorded fixture outputs without calling
Ollama, the network, or an LLM judge. A fixed console entry point validates the
fixtures, runs five deterministic metrics, and writes bounded JSON and Markdown
reports below `.local-ai/evaluations/<run-id>`.

The approved command shape is:

```powershell
dotnet run --project tools/LocalAI.Evaluation/LocalAI.Evaluation.csproj -c Release --no-build --no-restore -- --fixtures evaluations/fixtures/v1 --evaluation-root .local-ai/evaluations --run-id <run-id> --product-commit <commit> --model-label recorded-fixture --profile-label deterministic
```

An evaluated case may fail while the command still exits successfully; that is
a valid score. Malformed fixtures, unsafe paths, unsupported schemas, duplicate
IDs, and report-write failures are infrastructure errors and return a non-zero
exit code. Evaluation never applies patches, invokes model-selected tools,
writes project memory, commits, or pushes.

Compare one baseline report with one declared candidate by using the same fixed
local evaluation root:

```powershell
dotnet run --project tools/LocalAI.Evaluation/LocalAI.Evaluation.csproj -c Release --no-build --no-restore -- compare --evaluation-root .local-ai/evaluations --comparison-id <comparison-id> --baseline-report <baseline-json> --candidate-report <candidate-json>
```

Both inputs must use the supported evaluator schema and match on product
commit, fixture-set identity, and case identifiers. The candidate is eligible
for user review only when at least one quality metric improves, none regresses,
unsafe-action rejection is preserved, and reported duration is no more than 20
percent above the baseline. The result is advisory; comparison does not select
or promote a model. Bounded JSON and Markdown reports are written under
`.local-ai/evaluations/comparisons/<comparison-id>`.
