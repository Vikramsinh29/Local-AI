# Local-AI Decision Log

Record a decision here only when it changes future implementation choices.
Keep entries short and link to a commit or issue when available.

## D-001 — Local-first AI runtime

**Decision:** Use Ollama at `127.0.0.1:11434` for all AI generation.

**Reason:** Local-AI must not send prompts, repository content, credentials, or
telemetry to external AI services.

## D-002 — WPF and MVVM remain the desktop architecture

**Decision:** Keep the existing .NET 10 WPF/MVVM solution and manual
composition.

**Reason:** It preserves current behavior and avoids unrelated framework or
dependency changes.

## D-003 — Repository access begins read-only

**Decision:** The first agent phases may inspect and propose, but may not edit,
execute modifying commands, commit, or push in a selected repository.

**Reason:** A proposed patch is not trustworthy enough to apply without review.

## D-004 — Evidence before claims

**Decision:** Local-AI must base repository claims on inspected files or shown
tool output.

**Reason:** This reduces hallucinated files, APIs, build results, and fixes.

## D-005 — Skills are modular Markdown workflows

**Decision:** Use `skills/<name>/SKILL.md` for reusable task workflows.

**Reason:** Versioned, concise workflows are easier to review and less likely
to conflict than duplicated global instructions or ad-hoc shell scripts.

## D-006 — Verification uses fixed tools and one-run approval

**Decision:** Agent verification exposes only protected Git status, protected
Git diff check, Release build without restore, and Release tests without build
or restore. Each run requires a fresh approval, uses an argument list without a
shell, validates repository and solution containment, streams bounded output,
supports cancellation, and remains in the current session audit.

**Reason:** A model-selected command string is an arbitrary execution path.
Fixed commands make the executable, arguments, evidence, and approval boundary
reviewable. Build/test are still shown as trusted-project operations because
MSBuild targets can execute project code and write generated output. Build and
test use the same ignored `.local-ai/verification` artifacts path so they can
run while the Local-AI desktop process is using its own Release binaries.
