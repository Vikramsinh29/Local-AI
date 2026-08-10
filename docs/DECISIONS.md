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

## D-010 — Rollback is exact, single-file, and current-session only

**Decision:** Sprint 2.3 retains the exact original and applied bytes, canonical
repository identity, validated path, and hashes for only the latest successful
one-file apply. A separate one-run rollback approval is consumed before the
service revalidates the repository, links, path, and exact applied bytes. Only
then may it atomically restore the exact original bytes and run protected Git
diff/status confirmation. The record remains in memory only and is invalidated
after success, repository change, a newer preview/apply, or restart.

**Reason:** Exact retained bytes make rollback deterministic and preserve the
original encoding, BOM, and line endings. Rejecting any external edit avoids
overwriting newer user work. A narrow current-session record provides useful
recovery without introducing persistent undo state, multi-file transactions,
Git reset/checkout, arbitrary commands, commits, pushes, or model-directed
writes.

## D-011 — Project instructions are explicit, bounded evidence

**Decision:** Discover only repository-root `AGENTS.md` and direct
`skills/<name>/SKILL.md` files. Include a valid root `AGENTS.md` by default,
allow the user to select at most one skill, require complete UTF-8 files, and
enforce a combined 8 KB / approximately 2,000-token budget. Display every
candidate's provenance and inclusion or exclusion reason. Product safety and
the current user request always outrank local instruction content.

**Reason:** Project instructions can improve consistency without becoming a
hidden prompt or a new authority. Explicit provenance, deterministic
precedence, full-file budgeting, and fail-closed path/link/text checks prevent
silent truncation, automatic skill choice, unsafe external references, and
instruction-driven expansion of Local-AI permissions. Generated agent plans
are also withheld when they omit an exact included instruction or source path,
or cite a same-extension repository path outside the displayed evidence set;
the local model is not trusted to self-certify its grounding.

## D-012 — Project memory is user-written, local, bounded, and prompt-excluded

**Decision:** Sprint 3.2 stores only user-typed Architecture, Command,
Decision, and Known issue notes in a versioned UTF-8 JSON file below
LocalAppData. The canonical repository path determines an isolated project ID.
Every create, update, or delete consumes a separate one-run approval; the
service validates the full existing store and performs an atomic replacement.
Memory is not added to prompts or source context in this sprint.

**Reason:** Small persistent notes can preserve useful project facts without
granting the model a hidden or self-writing memory channel. External storage,
strict entry and combined budgets, sensitive-content rejection, visible
metadata, honest corruption errors, and explicit approvals keep the new state
attributable and reversible while avoiding repository changes, command
execution, cloud synchronization, or unattended behavior.

## D-013 — Prompt memory is explicit, singular, and revalidated

**Decision:** Sprint 3.3 keeps project memory excluded by default and permits
the user to include at most one stored entry through a separate session-only
prompt selector. Desktop reloads and exactly matches that entry immediately
before composition. Core places the complete entry after project instructions
and before source evidence, labels it untrusted, and exposes the stable evidence
identity `project-memory:<entry-id>`. A completed response is withheld unless it
cites that exact identity. Repository changes, reloads, updates, and deletions
clear the prompt selection.

**Reason:** Explicit selection and visible metadata preserve user attribution
without creating hidden retrieval. Exact pre-send revalidation prevents stale
or cross-project memory from entering a prompt, while deterministic identity
checking prevents the model from silently substituting another note. Keeping
Command memory inert and subordinate to product safety, the user request,
project instructions, and approval gates avoids adding a command or permission
channel.

## ADR: deterministic recorded-output evaluation foundation

**Decision:** Use versioned recorded fixtures and fixed local scoring rules for
the first evaluation foundation. Do not use a live Ollama request or an LLM
judge in Sprint 4.1.

**Reason:** A recorded suite makes regressions repeatable on the same source
commit, works without network access, and makes every score explainable. It also
keeps evaluation outside the agent's write and tool-approval paths.

**Consequences:** Case definitions require stable IDs, explicit categories,
bounded evidence files, expected properties, and safety labels. The loader
rejects malformed, duplicate, unsupported, linked, outside-root, oversized, or
ambiguous fixtures. Reports are local generated state under `.local-ai` and are
not source inputs. Expanding to live-model evaluation, statistical sampling, a
dashboard, or LLM judging requires a later explicit decision.

## ADR: candidate comparison is deterministic and advisory

**Decision:** Compare exactly one baseline and one candidate Sprint 4.1 report
only when their supported evaluator schema, product commit, fixture-set
identity, and case identifiers match. Preserve both report hashes and complete
provenance. Calculate fixed case, metric, safety, and reported-duration gates,
then emit only `Eligible for user review` or `Not recommended`.

**Reason:** A candidate comparison is meaningful only when the declared model
or profile is the isolated variable. Fail-closed provenance and per-gate output
prevent missing cases, invalid scores, safety failures, or an aggregate average
from hiding a regression. The 20-percent duration threshold is deterministic
reported-run evidence, not a hardware benchmark.

**Consequences:** Comparison remains offline, local, bounded, and repeatable.
It never runs Ollama, judges with an LLM, executes recorded output, changes a
model/profile, promotes automatically, or writes a selected repository. Any
quality regression, unsafe-action regression, provenance mismatch, invalid
input, or excessive duration makes the candidate not recommended.

## ADR: first repository search uses the inspected tree only

**Decision:** Sprint 5.1 searches only file names and relative paths already
present in the bounded repository tree. It returns at most 50 deterministic
results and requires an explicit user action before the existing context reader
may load one selected file.

**Reason:** Tree-only search improves navigation without adding another
filesystem crawler, content-reading boundary, persistent index, semantic model,
or hidden prompt-selection channel. The existing context service remains the
single authority for deciding whether a selected file is safe to read.

## ADR: content search is explicit and limited to one revalidated file

**Decision:** Sprint 5.2 searches only literal text in the one context file the
user explicitly selects. Desktop revalidates that file with the existing
protected reader for each search. Core returns at most 20 line-numbered previews
with fixed query and preview limits; results are display-only.

**Reason:** Reusing the established read boundary avoids a second filesystem
trust path. Single-file scope and fixed limits provide useful navigation while
excluding indexing, implicit prompt selection, model calls, repository writes,
and broad or semantic retrieval.
