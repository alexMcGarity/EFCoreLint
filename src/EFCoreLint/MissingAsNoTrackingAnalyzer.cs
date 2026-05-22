using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCoreLint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFLINT003";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Read-only query missing AsNoTracking",
        messageFormat: "Query is materialized without AsNoTracking(). Add AsNoTracking() to skip unnecessary change-tracking overhead.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "EF Core tracks all queried entities by default. For read-only queries where the result "
                   + "is never passed back to SaveChanges, call AsNoTracking() to skip the tracking snapshot "
                   + "and reduce memory allocations.");

    private static readonly HashSet<string> MaterializingMethods = new HashSet<string>
    {
        "ToList", "ToArray", "First", "FirstOrDefault", "Single", "SingleOrDefault",
        "ToListAsync", "ToArrayAsync", "FirstAsync", "FirstOrDefaultAsync",
        "SingleAsync", "SingleOrDefaultAsync"
    };

    private static readonly HashSet<string> SaveMethods = new HashSet<string>
    {
        "SaveChanges", "SaveChangesAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!MaterializingMethods.Contains(memberAccess.Name.Identifier.Text))
            return;

        // Source must implement IQueryable<T>
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (!EFCoreTypeHelpers.ImplementsIQueryable(receiverType))
            return;

        // Skip if AsNoTracking is already in the call chain
        if (ChainContainsAsNoTracking(memberAccess.Expression))
            return;

        // Skip if the enclosing method calls SaveChanges — entities may be intentionally tracked
        if (EnclosingMethodHasSaveChanges(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    private static bool ChainContainsAsNoTracking(ExpressionSyntax expr)
    {
        while (expr is InvocationExpressionSyntax inv &&
               inv.Expression is MemberAccessExpressionSyntax ma)
        {
            if (ma.Name.Identifier.Text is "AsNoTracking" or "AsNoTrackingWithIdentityResolution")
                return true;
            expr = ma.Expression;
        }
        return false;
    }

    private static bool EnclosingMethodHasSaveChanges(SyntaxNode node)
    {
        var enclosing = node.Ancestors().FirstOrDefault(static n =>
            n is MethodDeclarationSyntax or
                 LocalFunctionStatementSyntax or
                 AnonymousFunctionExpressionSyntax);

        if (enclosing is null)
            return false;

        return enclosing.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(static inv =>
                // context.SaveChanges()
                (inv.Expression is MemberAccessExpressionSyntax m &&
                 SaveMethods.Contains(m.Name.Identifier.Text)) ||
                // SaveChanges() — when the class itself extends DbContext
                (inv.Expression is IdentifierNameSyntax id &&
                 SaveMethods.Contains(id.Identifier.Text)));
    }
}
