# AI test environment (Claude Code)

A validated, repeatable setup so an AI agent can write tests safely in this repo.

## Components (all tracked in git)
| File | Purpose |
|---|---|
| `.claude/agents/test-writer.md` | Unit-test agent: scope, workflow, hard rules |
| `.claude/skills/unit-test-conventions/SKILL.md` | Naming, Moq-vs-fakes, templates, repo pitfalls |
| `.claude/skills/run-unit-tests-and-coverage/SKILL.md` | Exact commands, definition of done, preflight |
| `.claude/settings.json` | Pre-approves `dotnet build/test`, `reportgenerator`, read-only git; denies force-push and `rm -rf` |
| `coverage.runsettings`, `dotnet-tools.json`, `scripts/coverage.ps1` | Deterministic coverage measurement |

Guardrails: the agent writes only under `tests/**` and never edits `src/**` (enforced by its prompt; production bugs are reported, not patched).
`.claude/settings.local.json` stays untracked (personal overrides).

## Preflight checklist
1. `dotnet --version` ≥ 10.0.201 (`global.json`).
2. `dotnet tool restore`.
3. `dotnet test tests/Domain.UnitTests` and `dotnet test tests/Application.UnitTests` are green **before** the agent starts.
4. Docker is NOT required for unit tests.

## Validation run
The agent was run on a real gap (0%-coverage classes `Result` and `ForbiddenAccessException`) and had to: read the skills,
add tests only under `tests/`, keep the build warning-free, run each project separately, and report coverage.
Result and any doc fixes found are recorded in the PR description for Activity 1.

## How to use
```
Use the test-writer agent to add unit tests for <ClassName>.
```

## integration-test-writer
| File | Purpose |
|---|---|
| `.claude/agents/integration-test-writer.md` | Integration/E2E agent: scope (`tests/Application.FunctionalTests`, `tests/Infrastructure.IntegrationTests`, Postman collection), mandatory happy + unhappy flow, report format |
| `.claude/skills/integration-test-conventions/SKILL.md` | Aspire/Respawn patterns, HTTP + repository templates, false-failure traps (dates, 48h rule, ids) |
| `.claude/skills/run-integration-tests/SKILL.md` | Docker preflight, exact commands, newman, definition of done, troubleshooting |
| `scripts/run-newman.ps1` | Headless Postman run against docker compose |

Extra guardrail: the agent must **report** production defects rather than edit `src/` (e.g. the missing `errors` map in 400 responses, found via newman, was reported and fixed as a separate, reviewed change).

### Preflight (integration)
1. `docker info --format '{{.ServerVersion}}'` prints a version (Docker Desktop running).
2. Unit tests green (Activity 1 preflight).
3. `dotnet test tests/Infrastructure.IntegrationTests` green before the agent starts.
4. For Postman: Node.js available (`npx`), port 8080 free.

### Validation run
The agent was given a real task (address flow: one happy + one unhappy test) and had to follow only the docs. Findings are recorded in the Activity 2 PR description.
