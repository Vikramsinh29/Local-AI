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

The current roadmap is in [ROADMAP.md](ROADMAP.md). The active scope is Phase
2, Sprint 2.2: verifying one approved apply with the existing fixed tools.
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
