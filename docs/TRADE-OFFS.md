# Design decisions, assumptions and known limitations

## Decisions
| Decision | Why |
|---|---|
| Services instead of MediatR/CQRS | Avoids ceremony; handlers would be one-line pass-throughs here. |
| EF Core exposed via `IClaimsDbContext`, no repositories | The DbContext already is unit-of-work + repository; tests use real SQLite instead of mocks. |
| Business rules inside the `Claim` aggregate | Cannot be bypassed from controllers/services; unit-testable without infrastructure. |
| UTC `DateTime`, not `DateTimeOffset` | SQLite (used in tests/fallback) cannot compare/order `DateTimeOffset` in SQL. |
| Optimistic concurrency via an int `Version` token | Works identically on SQL Server and SQLite. |
| Single Kafka topic keyed by claim id | Per-claim ordering; one consumer group demonstrates async processing. |
| Officers see all claims (no market scoping) | Simplest correct behaviour for the brief; market scoping would be a query filter. |
| SQL Server in Compose, SQLite supported via config | Brief allows either; SQLite keeps tests Docker-free. |

## Assumptions
* Policies come from an upstream system; they are pre-seeded and linked to a claimant by holder email at registration.
* Documents are uploaded after the claim is created (separate calls); the UI retries/reports failures.
* One decision per claim; no appeal or reopen flow. Reassignment is allowed until decided.
* FX rates for dashboard roll-up are fixed and indicative.

## Known limitations (be honest about these)
1. **Not compiled or run by the author of this repo snapshot.** Expect to fix a few compile/runtime issues on first build.
2. **No EF migrations**: schema is created with `EnsureCreated`. Next step: `dotnet ef migrations add Initial` and call `Migrate()`.
3. **Outbox relay is single-instance.** Running several API replicas needs row locking (`UPDLOCK, READPAST`) or a dedicated relay.
4. **No dead-letter topic.** Consumer retries 3 times then logs and skips; outbox parks after 10 failed publishes.
5. **JWT in sessionStorage** (XSS-exposed). Production would use httpOnly cookies + CSRF protection, refresh tokens and a real IdP.
6. **Local-disk file storage**, no virus scanning. Swap `IFileStorage` for blob storage + scanning.
7. **Dashboard aggregates exposure in memory** over outstanding claims (portability with SQLite). Move to SQL/materialised view at scale.
8. **No frontend automated tests**; Angular is verified only by `ng build`.
9. Swagger is enabled in every environment for the demo; restrict in production.
10. Claim numbers use a random 6-digit suffix guarded by a unique index; a DB sequence would be better.
