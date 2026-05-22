using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<EFCoreLint.MissingAwaitAnalyzer>;

namespace EFCoreLint.Tests;

// EF Core async extension methods are stubbed inside each test source string
// so the test compilation is self-contained with no EF Core assembly reference.
public class MissingAwaitTests
{
    private const string EFStubs = """
        using System.Linq;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using System.Threading;

        static class QueryableExtensions
        {
            public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
                => Task.FromResult(source.ToList());

            public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
                => Task.FromResult(default(T));
        }
        """;

    // ── Positive cases ──────────────────────────────────────────────────────

    [Fact]
    public async Task ToListAsync_WithoutAwait_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                async System.Threading.Tasks.Task M(IQueryable<int> q)
                {
                    {|EFLINT005:q.ToListAsync()|};
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithoutAwait_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                async System.Threading.Tasks.Task M(IQueryable<int> q)
                {
                    {|EFLINT005:q.FirstOrDefaultAsync()|};
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_AssignedWithoutAwait_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                void M(IQueryable<int> q)
                {
                    var t = {|EFLINT005:q.ToListAsync()|};
                }
            }
            """);
    }

    // ── Negative cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task ToListAsync_WithAwait_DoesNotFire()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                async System.Threading.Tasks.Task M(IQueryable<int> q)
                {
                    var r = await q.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_Returned_DoesNotFire()
    {
        // Caller's responsibility to await the returned Task.
        // Note: no extra usings here — EFStubs already declares System.Collections.Generic
        // and a using after a type declaration is a CS1529 error.
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                System.Threading.Tasks.Task<System.Collections.Generic.List<int>> M(IQueryable<int> q)
                {
                    return q.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_Synchronous_DoesNotFire()
    {
        // Synchronous ToList is not in scope for this diagnostic
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task CustomAsyncMethod_NonEFCore_DoesNotFire()
    {
        // ToListAsync on a non-IQueryable source must not fire
        await Verify.VerifyAnalyzerAsync("""
            using System.Threading.Tasks;
            using System.Collections.Generic;

            class MyCollection
            {
                public Task<List<int>> ToListAsync() => Task.FromResult(new List<int>());
            }

            class C
            {
                void M(MyCollection col)
                {
                    var t = col.ToListAsync();
                }
            }
            """);
    }
}
