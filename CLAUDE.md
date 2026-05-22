# EFCoreLint

A Roslyn analyzer + code-fix NuGet package that catches common Entity Framework Core anti-patterns at compile time.

## Purpose and context

This is a **portfolio project for a Microsoft SWE II application**. It is intentionally a small, shippable artifact — not a long-term product. Optimize every decision against this question: *"Will this make the project more credible and demonstrable in a 45-minute interview conversation?"*

The owner is a software engineer with 2 years of professional experience, strongest in C#/.NET. Time budget is tight; ship velocity matters more than architectural purity.

## Instructions for Claude when working on this project

1. **Ship over polish.** v0.1.0 on NuGet beats a perfect v0.0.0 on disk. After every phase, pause and confirm we still have a working, demoable state.
2. **No scope creep.** If a feature is not in the MVP list below, push back before implementing. New ideas go in a `BACKLOG.md`, not into the current sprint.
3. **Lean on tests as documentation.** Each diagnostic should have a paired test that doubles as a usage example.
4. **Track the portfolio surface as we go.** Anything that would make a great screenshot, GIF, or blog-post paragraph — flag it explicitly so the owner can capture it before context fades.
5. **Be honest about what's shippable.** If a diagnostic has a high false-positive rate, mark it experimental rather than including it in the headline list.
6. **Prefer dotnet CLI over Visual Studio workflows** when scaffolding so commands are reproducible and CI-friendly.

## Goal and success criteria

MVP is complete when all of the following are true:

- [ ] Published to NuGet.org under a real package ID (suggested: `EFCoreLint`)
- [ ] 3–5 high-value diagnostics, each with a working code fix
- [ ] README with before/after code samples and a Marketplace-ready description
- [ ] Tests covering positive and negative cases for every diagnostic
- [ ] At least one integration smoke-test against a real EF Core sample (e.g., a fresh `dotnet new webapi` + `dotnet ef`) showing no false positives in idiomatic code
- [ ] One technical blog post or website page explaining one analyzer's implementation in depth

## Initial diagnostic catalog

Pick 3 of these for v0.1.0; defer the rest to v0.2.0.

| ID | Name | Detects | Code fix |
|---|---|---|---|
| EFLINT001 | ClientSideFiltering | `.ToList()` / `.ToArray()` immediately followed by `.Where(...)` on a `DbSet` or `IQueryable` | Swap the call order |
| EFLINT002 | PossibleNPlusOne | `foreach` over an `IQueryable` whose body contains another DB query | Suggest `.Include(...)` or eager-load projection |
| EFLINT003 | MissingAsNoTracking | Read-only query with no `SaveChanges` in scope and not assigned to a tracked field | Insert `.AsNoTracking()` |
| EFLINT004 | OverProjection | Query materializes full entity but only specific properties are read after | Add `.Select(...)` projection |
| EFLINT005 | MissingAwait | Async query method (`ToListAsync` etc.) called without `await` | Add `await` |

Start with **EFLINT001, EFLINT003, EFLINT005** — they have the cleanest detection logic and lowest false-positive risk.

## Tech stack

- .NET 8 SDK, C# 12
- Analyzer assembly: **netstandard2.0** (required for Roslyn host compatibility across VS / Rider / `dotnet build`)
- `Microsoft.CodeAnalysis.CSharp` (Roslyn)
- `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` + xUnit
- GitHub Actions for CI and release publishing

## Planned project structure

```
EFCoreLint/
  src/
    EFCoreLint/                  # Analyzer + code-fix, netstandard2.0
    EFCoreLint.Package/          # NuGet packaging project (analyzer + props/targets)
  tests/
    EFCoreLint.Tests/            # Per-diagnostic unit tests
    EFCoreLint.IntegrationTests/ # Runs against a sample EF Core project
  samples/
    EFCoreLint.Samples/          # Code that intentionally triggers each diagnostic
  .github/workflows/
    ci.yml                       # Build + test on every push
    release.yml                  # Pack + push to NuGet on v* tag
  README.md
  BACKLOG.md
```

## Phased plan

### Phase 1 — Scaffold and prove the pipeline (Day 1)
- Create the solution and projects using `dotnet new analyzer`
- Implement **EFLINT001** end-to-end (analyzer + code fix + unit test)
- Wire up GitHub Actions CI
- **Demo gate:** run `dotnet test` green; manually reference the analyzer DLL from a throwaway console project and confirm the warning shows in `dotnet build`

### Phase 2 — Core diagnostics (Days 2–4)
- Implement EFLINT003 and EFLINT005
- Add `.editorconfig` severity override support (free with the analyzer template, just document it)
- Write README with full diagnostic catalog, install instructions, and before/after snippets
- **Demo gate:** install the local `.nupkg` into a fresh ASP.NET Core + EF Core webapi project, take screenshots of all three warnings

### Phase 3 — Ship v0.1.0 (Day 5)
- Reserve the package ID on NuGet.org
- Tag `v0.1.0`, release workflow publishes
- Verify install from public NuGet works end-to-end

### Phase 4 — Portfolio polish (Day 6+)
- Write the blog post / portfolio page (focus on EFLINT002 — the N+1 detection is the most interesting AST-walking story)
- Add the download-count badge to the README
- Add EFLINT002 and EFLINT004 to a v0.2.0 release

## Key engineering decisions

- **Target framework for the analyzer:** netstandard2.0. Do not change this without a specific reason — Roslyn hosts (VS, Rider, `dotnet build`) load analyzers into processes that may not support newer TFMs.
- **Type detection:** use `INamedTypeSymbol` comparison against well-known EF Core type names resolved via `Compilation.GetTypeByMetadataName`. Never pattern-match on `IdentifierName` strings — it breaks under aliases.
- **Scope:** if EF Core is not referenced in the project, all diagnostics must be silent. Check assembly name `Microsoft.EntityFrameworkCore` once per compilation and cache.
- **Code fixes vs refactorings:** ship code fixes (triggered by diagnostic). Refactorings are nicer UX but more code and not required for portfolio signal.
- **Severity defaults:** all diagnostics default to `Warning`. Allow `.editorconfig` overrides.

## Out of scope (resist these — push to BACKLOG.md)

- MSBuild custom build tasks beyond the analyzer template defaults
- Querying actual database schemas — pure static analysis only
- EF6 support — EF Core only
- A Visual Studio extension UI — diagnostics surface natively
- A web-based playground — nice-to-have, not portfolio-critical

## Commands

To be populated once the solution is scaffolded. Expected:

- `dotnet build` — build all projects
- `dotnet test` — run analyzer unit tests
- `dotnet pack -c Release src/EFCoreLint.Package` — produce the NuGet package locally
- Release: push a `v*` tag, GitHub Actions handles publish

## Related projects

- [[AllocationLens]] — sibling portfolio piece, also Roslyn-based but distributed as a VSIX. Share testing patterns and CI workflow templates where it makes sense.
