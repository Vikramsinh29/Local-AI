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
| External services | `src/LocalAI.Infrastructure` | Ollama HTTP, read-only repository/context access, fixed verification execution, and the approval-gated patch write boundary |
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

## Change rules

1. Start from the smallest affected layer.
2. Put shared contracts in Core before implementations.
3. Keep I/O asynchronous and cancelable.
4. Preserve the existing UI design and binding patterns.
5. Add focused tests for normal, rejected, cancellation, and failure paths.
6. Verify with the commands in `README.md` before committing.
