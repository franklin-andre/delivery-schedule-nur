---
name: run-integration-tests
description: Preflight and commands to run MS4 integration tests (Aspire + Postgres in Docker) and the Postman/newman flows. Use before and after writing integration tests.
---

# Run integration tests (Docker required)

## 1. Preflight — stop here if it fails
```powershell
docker info --format '{{.ServerVersion}}'    # must print a version; if it errors, Docker Desktop is not running -> report, do not continue
dotnet --version                              # >= 10.0.201
```
Integration tests start a Postgres container via Aspire (`TestAppHost`); first run pulls the image and can take a minute. Timeout 60s for the DB to become healthy.

## 2. Run (one project per `dotnet test` call)
```powershell
dotnet test tests/Infrastructure.IntegrationTests
dotnet test tests/Application.FunctionalTests
dotnet test tests/Application.FunctionalTests --filter "FullyQualifiedName~<YourTestClass>"   # e.g. CalendarHttpFlowTests
```
Rely on the exit code. Success line is `Passed!`/`Correctas!`; failures print `Failed`/`Con error` plus the message — read it before changing anything.

## 3. Postman collection headless
```powershell
./scripts/run-newman.ps1          # docker compose up -d --build, waits for the API on :8080, runs newman (needs Node.js)
./scripts/run-newman.ps1 -BaseUrl http://localhost:5000 -NoCompose
```
Rebuilds the API image, so it tests your current `src/`. Expected: 0 failed assertions.

"Full integration suite" = both projects above (Infrastructure.IntegrationTests + Application.FunctionalTests); newman only when the Postman collection or `src/Api` changed.

## 4. Definition of done
1. Preflight OK.
2. New tests include >= 1 happy and >= 1 unhappy flow.
3. Both integration projects green; newman green if the collection changed.
4. Nothing under `src/` modified by you (report defects instead).

## Troubleshooting
- `Docker not running` / container start timeout → start Docker Desktop, retry once.
- Port 8080 busy for newman → `docker compose ps`, or pass `-BaseUrl` of the running API.
- Tests pass alone but fail together → a test depends on leftover data; each test must create its own (Respawn resets between tests).
