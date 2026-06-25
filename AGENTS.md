\# HelpDeskHero agent instructions



Project:

\- ASP.NET Core API + Blazor WebAssembly UI.

\- Solution file: HelpDeskHero.slnx.

\- Target framework: net10.0.

\- API project: src/HelpDeskHero.Api.

\- UI project: src/HelpDeskHero.UI.

\- Shared contracts: src/HelpDeskHero.Shared.



Rules:

\- Work in small focused steps.

\- Do not rewrite unrelated files.

\- Keep existing namespaces and folder structure.

\- Do not rename existing DTOs, pages, routes or services unless explicitly asked.

\- Do not change database schema unless the task explicitly requires a migration.

\- Keep UI text mostly in Polish.

\- API errors should use ProblemDetails / ValidationProblemDetails where reasonable.

\- Use existing JWT, refresh token, roles and policies.

\- Use existing AppDbContext and domain namespace.

\- After changes run: dotnet build .\\HelpDeskHero.slnx.

\- If tests are affected run: dotnet test .\\HelpDeskHero.slnx.

\- Summarize changed files and explain what was changed.

