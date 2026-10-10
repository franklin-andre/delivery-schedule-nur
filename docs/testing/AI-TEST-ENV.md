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
