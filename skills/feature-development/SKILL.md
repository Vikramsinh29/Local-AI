# Feature Development Skill

## Use when

The user asks to add or change one Local-AI feature.

## Required inputs

- The user-visible goal
- Acceptance criteria
- Any explicit safety or compatibility constraint

## Workflow

1. Read `ROADMAP.md`, `AGENTS.md`, `docs/ARCHITECTURE.md`, and relevant
   decision-log entries.
2. Confirm the request is within the active roadmap phase.
3. Inspect the smallest relevant interfaces, models, implementations,
   ViewModels, XAML, and tests.
4. State the smallest implementation plan, affected files, and assumptions.
5. Implement one cohesive feature without unrelated cleanup or redesign.
6. Add or update tests for success, rejection, cancellation, and regression
   behavior that applies to the feature.
7. Run the required verification commands:

   ```powershell
   dotnet build LocalAI.slnx -c Release
   dotnet test LocalAI.slnx -c Release --no-build
   git diff --check
   git status --short
   ```

8. Report evidence: changed files, build result, test total, and remaining
   limitations.

## Guardrails

- Do not expand scope without a new sprint decision.
- Do not claim a result without source or tool evidence.
- Do not make selected-repository writes during Phase 1.
- Do not commit or push unless the user explicitly authorizes it.
