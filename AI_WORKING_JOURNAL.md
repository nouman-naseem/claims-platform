# AI Working Journal

Tool: Claude (chat). Running log of what I asked, what I accepted, what I challenged, and what I overrode.

## Log

| # | What I asked | What the AI did | Accepted / Challenged / Overrode | Why |
|---|---|---|---|---|
| 1 | Gave the full brief: build the Claims Platform (.NET 8, Angular, SQL Server, Kafka outbox, JWT roles, tests, Docker). Asked it to inspect the repo and plan first. | Found an empty repo and no .NET/Docker in its sandbox, so it could not build or test. Proposed a plan and asked how to proceed. | Accepted | It was honest that nothing could be compiled there. |
| 2 | Told it to generate the code anyway and zip it. | Generated backend, tests, Angular app, Docker Compose and docs. Said clearly it was untested. | Accepted | I would build and test it locally myself. |
| 3 | Ran `dotnet build`, `dotnet test` and the frontend build myself. | n/a | n/a | Reported them as done. |
| 4 | Asked where to run `docker compose up --build`. | Explained to run it in the folder with `docker-compose.yml`. | Accepted | |
| 5 | Docker failed: could not connect to the Docker engine. | Said Docker Desktop was not running and gave steps. | Accepted | Started Docker Desktop and it worked. |
| 6 | API image build failed at `dotnet publish`. | First guessed leaked `bin/obj` folders, asked for the real log, then confirmed it from the Windows NuGet path in the error. Added a `.dockerignore`. | Accepted | The AI had missed the `.dockerignore`; I created it locally and the build worked. |
| 7 | Said it was "not working" and I thought I had to run images. | Explained that compose starts the images and `docker compose up -d` keeps it running. | Challenged, then accepted | My assumption was wrong. The stack had stopped when I closed the terminal. |
| 8 | Checked Kafka was running, then logged in as a claimant and created a claim. | Gave commands and credentials. | Accepted | The claim was assigned to an officer automatically, so the Kafka flow works. |
| 9 | Asked for officer and manager logins, how to see the database, and why `sa` login failed. | Gave the demo accounts, SQL client settings and a checklist. | Accepted | |
| 10 | Asked why SQL Server instead of SQLite. | SQL Server suits concurrent writers and the outbox transaction; SQLite is only for tests and a fallback. | Challenged, then accepted | The reasoning held up for this scope. |
| 11 | Asked why a monolith. | The brief asks for one API; one domain and one transaction boundary; Kafka is the first seam to split later. | Challenged, then accepted | The reasoning held up for this scope. |
| 12 | Asked if its first journal was correct. | Said it overstated my role and was out of date. | Challenged | I wanted a record of what I actually did. |
| 13 | Asked for a simple journal and removed the AI's git history. | Wrote a simpler version, then this one. | Overrode | I wanted a normal journal and my own commit history. |

## Design decisions the AI proposed

Reviewed by me (asked the AI to justify them and accepted the reasoning):
- SQL Server for the app, SQLite for tests only.
- Modular monolith rather than separate services.

Not yet reviewed in detail:
- Services using the DbContext directly instead of MediatR or repositories.
- Business rules and the claim state machine inside the `Claim` class.
- Outbox pattern: claim and event saved in one transaction, then published to Kafka.
- Auto-assigning submitted claims through a Kafka consumer.

## What the AI got wrong
- The first zip had no `.dockerignore`, so the Docker build failed on my machine.
- Early scaffolding commands failed silently in its sandbox; it noticed and redid them.

## My own changes
(Add anything you change here, with the reason.)

-