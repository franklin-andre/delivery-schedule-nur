# Final project — complete testing layer (MS4 Delivery Calendar)

Scope: this repository contains one microservice, **MS4 Delivery Calendar**. Its testing layer = unit tests (>= 80% coverage) + integration tests (>= 2 complete flows).

## 1. Unit tests — coverage report
- Projects: `tests/Domain.UnitTests` (56), `tests/Application.UnitTests` (79). NUnit + Moq + Shouldly + EF InMemory. No Docker.
- Report: `./scripts/coverage.ps1` → `coverage-report/index.html` (+ `Summary.txt`, `Cobertura.xml`). The script **fails under 80%**; CI runs it on every PR and uploads the report as an artifact.
- Result: **98.1% line coverage, 89.3% branch** (gate: 80%) (details and scope in [UNIT-TESTING.md](UNIT-TESTING.md)).

## 2. Integration tests — two complete flows (+ contract and repository layers)
Run against a real Postgres (Aspire + Respawn). Grouped with NUnit categories:

| Flow | Category | Endpoints | Scenarios |
|---|---|---|---|
| **A — Production and delivery** (MS3 / MS5) | `FlowA` | #1 schedule, #4 address, ops resolve-daily-catalog, #2 active-tomorrow, #7 delivery-result | happy path to `Delivered`; duplicate contract; 400 with field errors; day not ready; idempotent + conflicting result; 404 |
| **B — Patient manages the calendar** (frontend) | `FlowB` | #1, #4 address, #3 reschedule, #5 cancel, #6 get | happy: address + reschedule + cancel reflected in the read model, cancelled day never reaches production; unhappy: 48h lock → 409 and nothing changes; cancelled day → 422; duplicate date → 400 |

```powershell
dotnet test tests/Application.FunctionalTests --filter "TestCategory=FlowA"
dotnet test tests/Application.FunctionalTests --filter "TestCategory=FlowB"
dotnet test tests/Infrastructure.IntegrationTests        # repositories / EF mapping on Postgres
./scripts/run-newman.ps1                                  # same flows as Postman collection (53 assertions)
./scripts/run-pact.ps1                                    # Pact contract tests (MS3->MS4 verified, MS4->MS2 consumer)
```
See [INTEGRATION-TESTING.md](INTEGRATION-TESTING.md) and [CONTRACT-TESTING.md](CONTRACT-TESTING.md).

## 3. AI-generated code: skills and project rules used
| Artifact | Location |
|---|---|
| Project rules (architecture, English-only, domain rules, known gotchas) | `CLAUDE.md` |
| Unit tests agent + skills | `.claude/agents/test-writer.md`, `.claude/skills/unit-test-conventions`, `.claude/skills/run-unit-tests-and-coverage` |
| Integration tests agent + skills | `.claude/agents/integration-test-writer.md`, `.claude/skills/integration-test-conventions`, `.claude/skills/run-integration-tests` |
| Contract tests agent + skills | `.claude/agents/pact-writer.md`, `.claude/skills/pact-consumer-conventions`, `.claude/skills/pact-provider-verification` |
| Tool permissions for the agents | `.claude/settings.json` |
| Validation of the AI environment | [AI-TEST-ENV.md](AI-TEST-ENV.md) |

## 4. Honest notes
- `AuditableEntityInterceptor` only stamps `LastModified` when the aggregate root (or its owned types) changes; editing only a child `DeliveryDay` does not touch `DeliveryCalendar.LastModified`. Pinned by test on `ProductionOrder`; not changed.
- The mocked `ICatalogPort` is the default; the MS2 contract is still unreconciled upstream (CLAUDE.md §5).
