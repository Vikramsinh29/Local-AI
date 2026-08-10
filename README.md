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

The current roadmap is in [ROADMAP.md](ROADMAP.md). The active scope is Phase
3, Sprint 3.3: explicit, revalidated inclusion of one user-selected project
memory entry while the default remains no memory.
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

## Repository map

- `src/LocalAI.Core` — contracts, models, and shared domain logic
- `src/LocalAI.Infrastructure` — Ollama and file-system implementations
- `src/LocalAI.Desktop` — WPF UI, commands, and ViewModels
- `tests/LocalAI.Tests` — automated tests

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
