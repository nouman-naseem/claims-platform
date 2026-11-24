# Claims Management Platform 
Insurance claims platform for motor and property claims across six APAC markets (SG, HK, AU, JP, MY, TH).

* **Backend:** .NET 8, ASP.NET Core Web API, Clean Architecture (Domain / Application / Infrastructure / API), EF Core
* **Database:** SQL Server (Docker Compose default); SQLite supported via config (used by tests)
* **Messaging:** Kafka with the Transactional Outbox pattern
* **Frontend:** Angular 18 (standalone components, reactive forms, route guards, interceptor)
* **Roles:** Claimant, ClaimsOfficer, Manager (JWT + role-based authorization)

## ⚠️ Verification status (read first)

This repository was generated in a sandbox **without a .NET SDK, Docker, or network access**. Therefore:

| Item | Status |
|---|---|
| Source for all layers, tests, Angular app, Dockerfiles, Compose | Written |
| `dotnet build` / `dotnet test` | **Not run** |
| `ng build` | **Not run** |
| `docker compose up` | **Not run** |
| Structural check (balanced braces) of every C# / TS file | Done |

Nothing here should be called "complete" until you run the commands below. Please expect to fix a small number of
compile errors on first build. Tests are written to be the safety net for doing so.

## Quick start

### Option A: everything in Docker
```bash
docker compose up --build
```
* UI: http://localhost:8081
* API + Swagger: http://localhost:5000/swagger
* On Apple Silicon, SQL Server runs under emulation; enable Rosetta in Docker Desktop (or use Option B with SQLite).

### Option B: local development (no Docker, SQLite, Kafka disabled)
```bash
# backend
export Database__Provider=Sqlite
export ConnectionStrings__Default="Data Source=claims.db"
dotnet run --project src/Claims.API        # http://localhost:5000/swagger

# frontend (second terminal)
cd frontend && npm install && npm start   # http://localhost:4200 (proxies /api to :5000)
```
With `Kafka__Enabled=false` (default) events are written to the outbox and logged instead of published; auto-assignment
does not run, so officers assign claims manually. Set `Kafka__Enabled=true` and point `Kafka__BootstrapServers` at a broker
(for example `docker compose up kafka`) to see the asynchronous flow.

### Run the tests
```bash
dotnet test        # unit + service tests + API integration tests (SQLite, no Docker needed)
```

## Demo accounts (password `Password123!`)

| Role | Email |
|---|---|
| Manager | `manager@claims.example` |
| Claims officers | `officer.sg@`, `officer.sg2@`, `officer.hk@`, `officer.au@`, `officer.au2@`, `officer.jp@`, `officer.my@`, `officer.th@` `claims.example` |
| Claimants | `claimant.sg1@`, `claimant.sg2@`, `claimant.hk1@`, `claimant.au1@`, `claimant.au2@`, `claimant.jp1@`, `claimant.my1@`, `claimant.th1@` `example.com` |
| Register yourself | `new.customer@example.com` (pre-issued policies get linked) |

Seed data: 48 claims across all markets and every status, including overdue and awaiting-information cases.

## Demonstrating the Kafka flow (Docker Compose)
1. Sign in as `claimant.sg1@example.com`, report an incident.
2. Within a few seconds the claim moves to **Under review** and is assigned to the least-loaded Singapore officer
   (no human involved). Watch it with `docker compose logs -f api` (outbox publish, consumer auto-assign).
3. Inspect the topic: `docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:19092 --topic claims.events --from-beginning`

## API overview (full detail in Swagger)

| Method & path | Role | Purpose |
|---|---|---|
| POST `/api/auth/login`, `/api/auth/register` | anon | Authenticate / self-register claimant |
| GET `/api/policies/mine` | Claimant | Policies available to claim against |
| POST `/api/claims` | Claimant | Report an incident |
| GET `/api/claims` | all | Paged list; filters: `status,type,market,unassigned,assignedToMe,overdue,search,page,pageSize` |
| GET `/api/claims/{id}` | all (scoped) | Detail, documents, history |
| POST `/api/claims/{id}/assign` | Officer, Manager | Self-assign / manager assigns |
| POST `/api/claims/{id}/information-requests` | Officer | Request more info |
| POST `/api/claims/{id}/information-requests/{rid}/response` | Claimant | Respond |
| POST `/api/claims/{id}/assessments` | Officer | Record assessment |
| POST `/api/claims/{id}/approve` · `/reject` | Officer | Decide |
| POST/GET `/api/claims/{id}/documents[/{docId}]` | Claimant, Officer | Upload / download |
| GET `/api/officers` | Officer, Manager | Officers + current load |
| GET `/api/dashboard` | Manager | KPIs, breakdowns, exposure, workload |

Errors are RFC 7807 problem details: 400 validation, 401, 403, 404, 409 (invalid transition / concurrent edit), 422 (business rule).

## Repository layout
```
src/Claims.Domain          aggregate, state machine, domain events (no dependencies)
src/Claims.Application     use-case services, DTOs, FluentValidation, ports
src/Claims.Infrastructure  EF Core, outbox, Kafka, JWT, storage, seed
src/Claims.API             controllers, auth, middleware, Swagger, Dockerfile
tests/Claims.Tests         domain unit, service (real SQLite) and API integration tests
frontend/                  Angular app (+ nginx Dockerfile)
docs/                      ARCHITECTURE.md, DOMAIN-MODEL.md, TRADE-OFFS.md
AI_WORKING_JOURNAL.md      how AI assistance was used and what was decided
```

## Documentation
* [Architecture, diagrams and workflows](docs/ARCHITECTURE.md)
* [Domain model and events](docs/DOMAIN-MODEL.md)
* [Decisions, assumptions, known limitations](docs/TRADE-OFFS.md)

## Feature coverage (implemented in code; not yet executed)
Claimant portal (report, upload, track, respond, see decision) · Officer portal (queue with filters, assign,
review, assess, request info, approve/reject, history) · Manager dashboard (totals, by status/type/market,
exposure, workload, overdue, pending) · JWT + roles · state machine · outbox + Kafka · audit history ·
structured logging (Serilog) · centralized exception handling · Swagger · Docker Compose.
