using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<EFCoreLint.MissingAsNoTrackingAnalyzer>;

namespace EFCoreLint.Tests;

// EF Core extension methods stubbed inline in each test source so the
// test compilation succeeds without referencing Microsoft.EntityFrameworkCore.
public class MissingAsNoTrackingTests
{
    // ── Positive cases ──────────────────────────────────────────────────────

    [Fact]
    public async Task ToList_WithoutAsNoTracking_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT003:q.ToList()|};
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefault_WithoutAsNoTracking_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT003:q.FirstOrDefault()|};
                }
            }
            """);
    }

    [Fact]
    public async Task Chained_Where_ToList_WithoutAsNoTracking_Fires()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = {|EFLINT003:q.Where(x => x > 0).ToList()|};
                }
            }
            """);
    }

    // ── Negative cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task ToList_WithAsNoTracking_DoesNotFire()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            static class QueryableExtensions
            {
                public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
            }

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.AsNoTracking().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_WithAsNoTrackingWithIdentityResolution_DoesNotFire()
    {
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            static class QueryableExtensions
            {
                public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) => source;
            }

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.AsNoTrackingWithIdentityResolution().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_InMethodWithSaveChanges_DoesNotFire()
    {
        // When SaveChanges is called on a context in the same method, tracking is intentional.
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class FakeContext
            {
                public void SaveChanges() { }
            }

            class C
            {
                FakeContext _ctx = new FakeContext();

                void M(IQueryable<int> q)
                {
                    var r = q.ToList();
                    _ctx.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_InMethodWithSaveChanges_DirectCall_DoesNotFire()
    {
        // SaveChanges() without explicit receiver (class extends DbContext pattern).
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;

            class C
            {
                void M(IQueryable<int> q)
                {
                    var r = q.ToList();
                    SaveChanges();
                }

                void SaveChanges() { }
            }
            """);
    }

    [Fact]
    public async Task List_ToList_WithoutAsNoTracking_DoesNotFire()
    {
        // Source is List<T>, not IQueryable — no diagnostic expected
        await Verify.VerifyAnalyzerAsync("""
            using System.Linq;
            using System.Collections.Generic;

            class C
            {
                void M(List<int> list)
                {
                    var r = list.ToList();
                }
            }
            """);
    }
}
