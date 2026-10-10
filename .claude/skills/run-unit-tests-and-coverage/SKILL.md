---
name: run-unit-tests-and-coverage
description: Commands to build, run MS4 unit tests, filter to one class, and produce/read a coverage report. Use after writing or changing unit tests, or when asked for coverage numbers.
---

# Run unit tests and coverage (no Docker needed)

Run from the repo root.

## 1. Fast loop
```powershell
dotnet test tests/Domain.UnitTests
dotnet test tests/Application.UnitTests
dotnet test tests/Application.UnitTests --filter "FullyQualifiedName~ResolveDailyCatalog"   # one class
```
`dotnet test` accepts ONE project per call (a second path is an MSBuild error).
Success line depends on locale: English `Passed! - Failed: 0, Passed: N`; Spanish `Correctas! - Con error: 0, Superado: N`. Rely on the exit code (`$LASTEXITCODE` = 0) rather than matching text.

## 2. Coverage report
```powershell
./scripts/coverage.ps1              # runs both projects, writes coverage-report/ (Html + Summary.txt)
./scripts/coverage.ps1 -Threshold 80
```
- Settings live in `coverage.runsettings` (includes Domain/Application/Infrastructure, excludes Migrations).
- The script's console output is long; do not tail it. Read `coverage-report/Summary.txt` (use Grep for the class name) for per-class line % — find the class you touched and compare before/after.
- Infrastructure EF classes show 0% here by design: they are covered by integration tests (Docker).

## 3. Definition of done for a test-writing task
1. `dotnet build` has no warnings (warnings are errors).
2. Both unit projects green.
3. Touched class coverage reported (before → after).
4. Nothing under `src/` modified (`git status`).

## Environment validation (preflight)
```powershell
dotnet --version                 # >= 10.0.201 per global.json
dotnet tool restore              # installs reportgenerator from dotnet-tools.json
dotnet test tests/Domain.UnitTests
```
