using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<EFCoreLint.ClientSideFilteringAnalyzer>;

namespace EFCoreLint.Tests;

public class ClientSideFilteringTests
{
    // ── Positive cases ──────────────────────────────────────────────────────

    [Fact]
    public async Task IQueryable_ToList_Where_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;
            using System.Collections.Generic;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT001:q.ToList().Where(x => x > 0)|};
                }
            }
            """);
    }

    [Fact]
    public async Task IQueryable_ToArray_Where_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT001:q.ToArray().Where(x => x > 0)|};
                }
            }
            """);
    }

    [Fact]
    public async Task IQueryable_ToList_FirstOrDefault_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT001:q.ToList().FirstOrDefault(x => x > 0)|};
                }
            }
            """);
    }

    [Fact]
    public async Task IQueryable_ToList_Any_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT001:q.ToList().Any(x => x > 0)|};
                }
            }
            """);
    }

    // ── Negative cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task List_ToList_Where_DoesNotFire()
    {
        // Source is List<T>, not IQueryable — no diagnostic expected
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;
            using System.Collections.Generic;

            class C
            {
                void M(List<int> list)
                {
                    var r = list.ToList().Where(x => x > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task IQueryable_Where_Without_ToList_DoesNotFire()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.Where(x => x > 0).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IQueryable_ToList_Select_DoesNotFire()
    {
        // Select is a projection, not a filter — no diagnostic
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.ToList().Select(x => x * 2);
                }
            }
            """);
    }
}
