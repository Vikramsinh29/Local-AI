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
| External services | `src/LocalAI.Infrastructure` | Ollama HTTP and read-only repository/file access |
| UI coordination | `src/LocalAI.Desktop/ViewModels` | Observable state, commands, cancellation, status text |
| UI presentation | `src/LocalAI.Desktop` | XAML bindings and view-specific code-behind only |
| Tests | `tests/LocalAI.Tests` | Deterministic unit and service behavior tests |

## Safety boundary

Selected repositories are read-only by default. Repository inspection must
canonicalize paths, preserve root containment, reject uncertain links, exclude
secrets and generated/binary files, and enforce file/context limits.

## Change rules

1. Start from the smallest affected layer.
2. Put shared contracts in Core before implementations.
3. Keep I/O asynchronous and cancelable.
4. Preserve the existing UI design and binding patterns.
5. Add focused tests for normal, rejected, cancellation, and failure paths.
6. Verify with the commands in `README.md` before committing.
