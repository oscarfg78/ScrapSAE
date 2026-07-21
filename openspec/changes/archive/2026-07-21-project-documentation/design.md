## Context

The solution `ScrapSAE.sln` has multiple projects representing different layers and applications:
- **Core**: Entities, DTOs, Enums, and Interfaces (DDD core).
- **Infrastructure**: Implementations of scraping (Playwright), DB access (Supabase), AI integrations, and SAE integration.
- **Worker**: Background service processing scraping jobs.
- **Api**: RESTful API exposing services.
- **Desktop**: Desktop client application.
- **Web/Extension**: Front-end applications (currently look incomplete or deprecated).

## Goals / Non-Goals

**Goals:**
- Provide a clear project layout description in a central `README.md`.
- Detail how to run `ScrapSAE.Api` and `ScrapSAE.Worker` locally.
- Document the current status of each module (Ready vs In Development).

**Non-Goals:**
- We will not write API reference documentation (Swagger should handle this).
- We will not change any source code.

## Decisions

- We will place the `README.md` at the project root for maximum visibility on GitHub/GitLab.
- It will follow a standard structure: Introduction, Architecture/Modules, Prerequisites, Running Locally, and Module Status.

## Risks / Trade-offs
None.
