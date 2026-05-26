using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<EFCoreLint.SyncBlockingCallAnalyzer>;

namespace EFCoreLint.Tests;

public class SyncBlockingCallTests
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

    // ── Positive cases — fires ───────────────────────────────────────────────

    [Fact]
    public async Task Result_Access_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT007:q.ToListAsync().Result|};
                }
            }
            """);
    }

    [Fact]
    public async Task Wait_Call_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                void M(IQueryable<int> q)
                {
                    {|EFLINT007:q.ToListAsync().Wait()|};
                }
            }
            """);
    }

    [Fact]
    public async Task GetAwaiter_GetResult_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT007:q.ToListAsync().GetAwaiter().GetResult()|};
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Result_Fires()
    {
        await Verify.VerifyAnalyzerAsync(EFStubs + """

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT007:q.FirstOrDefaultAsync().Result|};
                }
            }
            """);
    }

    // ── Negative cases — does not fire ───────────────────────────────────────

    [Fact]
    public async Task Awaited_DoesNotFire()
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
    public async Task Returned_DoesNotFire()
    {
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
    public async Task NonEFCore_Result_DoesNotFire()
    {
        // .Result on a Task from a non-EF source should not fire
        await Verify.VerifyAnalyzerAsync("""
            using System.Threading.Tasks;

            class C
            {
                void M()
                {
                    var t = Task.FromResult(42);
                    var r = t.Result;
                }
            }
            """);
    }
}
