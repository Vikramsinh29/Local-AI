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

## D-007 — Patch proposals use a strict preview-only envelope

**Decision:** Request one delimiter-based `LOCAL_AI_PATCH_V1` response, extract
one complete marked proposal even when surrounded by plain explanation, reject
Markdown-wrapped, duplicated, or malformed output instead of repairing it,
require exact ORIGINAL and
REPLACEMENT text for a selected evidence file, verify the original appears
exactly once, construct the displayed diff in Core, and render only an
in-memory preview.

**Reason:** Small local models are more reliable with a short explicit text
contract than a deeply nested schema. Having Core construct the diff avoids
requiring a small model to reproduce platform-specific path separators and hunk
arithmetic. Evidence matching prevents invented source text from being
presented as valid. A fallback may normalize leading indentation only when the
remaining lines match uniquely and preserve their count. Phase 1 deliberately
contains no apply or repository-write path.

## D-008 — First apply capability is one-file, approval-gated, and atomic

**Decision:** Sprint 2.1 may apply exactly one currently displayed preview only
after a separate one-run approval and a clean result from the protected Git
status tool. The write service revalidates the local Git root, shared source
path rules, links, raw-file SHA-256 snapshot, and unique ORIGINAL text, then
atomically replaces the file through ignored `.local-ai/apply` staging while
preserving supported encoding/BOM and line endings.

**Reason:** Restricting the first write boundary to one reviewed file keeps
failure behavior understandable and prevents a partial multi-file change. A
clean working tree provides an external recovery path, while snapshot and
content checks prevent a stale preview from overwriting newer work. Automatic
verification, multi-file transactions, and rollback UI require separate
reviewed sprints.

## D-009 — Apply approval includes a fixed post-apply verification sequence

**Decision:** Sprint 2.2 makes the one-run apply approval explicitly cover Git
diff check and, when exactly one valid .NET solution is detected, the existing
isolated Release build followed by Release tests. The sequence stops on the
first failure or cancellation and retains every attempted step in the session
audit. If verification cannot complete, the applied source change remains and
Local-AI states that fact.

**Reason:** Verification evidence is most useful immediately after the reviewed
write, but adding another approval between the write and its fixed checks can
leave an unverified change without a clear outcome. Reusing the existing
allow-list avoids a new command surface. Stop-on-failure prevents misleading
downstream test claims, while explicit no-rollback wording preserves the narrow
scope until rollback is designed separately.
