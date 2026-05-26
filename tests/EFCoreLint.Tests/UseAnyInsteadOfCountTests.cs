using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<EFCoreLint.UseAnyInsteadOfCountAnalyzer>;

namespace EFCoreLint.Tests;

public class UseAnyInsteadOfCountTests
{
    // ── Positive cases — fires ───────────────────────────────────────────────

    [Fact]
    public async Task Count_GreaterThan_Zero_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count() > 0|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_NotEquals_Zero_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count() != 0|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_GreaterThanOrEqual_One_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count() >= 1|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_Equals_Zero_Fires_Negated()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count() == 0|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_LessThan_One_Fires_Negated()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count() < 1|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Flipped_Zero_LessThan_Count_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:0 < q.Count()|}) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_WithPredicate_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if ({|EFLINT006:q.Count(x => x > 5) > 0|}) { }
                }
            }
            """);
    }

    // ── Negative cases — does not fire ───────────────────────────────────────

    [Fact]
    public async Task Count_GreaterThan_One_DoesNotFire()
    {
        // Checking for more than one result is not an existence check
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if (q.Count() > 1) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_Equals_One_DoesNotFire()
    {
        // Checking for exactly one result is not a simple existence check
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    if (q.Count() == 1) { }
                }
            }
            """);
    }

    [Fact]
    public async Task Count_NotInComparison_DoesNotFire()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var n = q.Count();
                }
            }
            """);
    }

    [Fact]
    public async Task Count_OnList_DoesNotFire()
    {
        // Source is List<T>, not IQueryable
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;
            using System.Collections.Generic;

            class C
            {
                void M(List<int> list)
                {
                    if (list.Count() > 0) { }
                }
            }
            """);
    }
}
