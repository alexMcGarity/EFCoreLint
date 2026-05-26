# EFCoreLint

[![CI](https://github.com/alexMcGarity/EFCoreLint/actions/workflows/ci.yml/badge.svg)](https://github.com/alexMcGarity/EFCoreLint/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EFCoreLint.svg)](https://www.nuget.org/packages/EFCoreLint)
[![NuGet Downloads](https://img.shields.io/nuget/dt/EFCoreLint.svg)](https://www.nuget.org/packages/EFCoreLint)

Roslyn analyzers that catch common **Entity Framework Core anti-patterns** at compile time — before they reach production.

## Installation

```
dotnet add package EFCoreLint
```

No configuration required. Diagnostics appear automatically in your IDE and in `dotnet build` output.

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| [EFLINT001](#eflint001-client-side-filtering) | Warning | Client-side filtering after materialization |
| [EFLINT003](#eflint003-missing-asnotracking) | Info | Read-only query missing `AsNoTracking` |
| [EFLINT005](#eflint005-missing-await-on-async-query) | Warning | EF Core async method called without `await` |
| [EFLINT006](#eflint006-use-any-instead-of-count) | Warning | `Count()` used for existence check instead of `Any()` |
| [EFLINT007](#eflint007-blocking-on-async-operation) | Warning | Blocking on EF Core async operation with `.Result` or `.Wait()` |

All severities can be overridden per-project via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.EFLINT001.severity = error
dotnet_diagnostic.EFLINT003.severity = none
```

---

### EFLINT001: Client-side filtering

Calling `ToList()` or `ToArray()` before `Where()` pulls every row into memory and filters client-side. Move the filter before materialization so the database does the work.

```csharp
// ❌ Warning EFLINT001 — loads all Products into memory first
var expensive = db.Products.ToList().Where(p => p.Price > 100);

// ✅ Fixed — WHERE clause executes on the database
var expensive = db.Products.Where(p => p.Price > 100).ToList();
```

A **code fix** is available: applying it rewrites the call order automatically.

---

### EFLINT003: Missing AsNoTracking

EF Core tracks every queried entity by default, allocating a change-tracking snapshot even when you never call `SaveChanges`. For read-only queries, `AsNoTracking()` skips this overhead.

```csharp
// ℹ️ Info EFLINT003 — change-tracking snapshot allocated unnecessarily
var products = db.Products.Where(p => p.IsActive).ToList();

// ✅ Fixed — no tracking overhead
var products = db.Products.AsNoTracking().Where(p => p.IsActive).ToList();
```

A **code fix** is available: it inserts `.AsNoTracking()` before the materializing call.

The diagnostic is suppressed when `SaveChanges` or `SaveChangesAsync` is called in the same method, because tracking is needed there.

---

### EFLINT005: Missing await on async query

EF Core async methods (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, etc.) return a `Task<T>` that must be awaited to actually execute the query. Without `await`, the operation is silently discarded.

```csharp
// ❌ Warning EFLINT005 — query never executes, result discarded
db.Products.ToListAsync();

// ✅ Fixed
var products = await db.Products.ToListAsync();
```

A **code fix** is available when the containing method is already `async`: it adds the `await` keyword.

---

---

### EFLINT006: Use Any() instead of Count()

Calling `Count()` and comparing against 0 or 1 to check existence issues a `COUNT(*)` query that scans every matching row. `Any()` translates to `EXISTS`, which stops at the first match.

```csharp
// ❌ Warning EFLINT006 — COUNT(*) scans all matching rows
if (db.Orders.Where(o => o.IsOpen).Count() > 0) { }

// ✅ Fixed — EXISTS short-circuits on the first match
if (db.Orders.Where(o => o.IsOpen).Any()) { }
```

Detected patterns: `Count() > 0`, `Count() != 0`, `Count() >= 1`, `Count() == 0`, `Count() < 1`, and their flipped equivalents. A **code fix** is available for all patterns, including `Count(predicate)` → `Any(predicate)`.

---

### EFLINT007: Blocking on async operation

Calling `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on a Task returned by an EF Core async method blocks the calling thread and can deadlock in ASP.NET Core applications.

```csharp
// ❌ Warning EFLINT007 — blocks the thread, risks deadlock
var products = db.Products.ToListAsync().Result;

// ✅ Fixed — asynchronous, no deadlock risk
var products = await db.Products.ToListAsync();
```

A **code fix** is available when the containing method is already `async`: it replaces the blocking call with `await`.

---

## Suppressing a diagnostic

```csharp
// In code
#pragma warning disable EFLINT001
var list = db.Products.ToList().Where(p => p.IsActive);
#pragma warning restore EFLINT001

// Or project-wide in .editorconfig
[*.cs]
dotnet_diagnostic.EFLINT001.severity = none
```

## Contributing

Issues and pull requests welcome. Open an issue to discuss a bug or new diagnostic before submitting a PR.
