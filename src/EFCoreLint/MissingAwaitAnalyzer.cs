using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCoreLint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAwaitAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFLINT005";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "EF Core async method called without await",
        messageFormat: "'{0}' returns a Task that is not awaited. The query will not execute and the result will be discarded.",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EF Core async query methods return a Task<T> that must be awaited to actually "
                   + "execute the database query. Without await, the query never runs and the result is silently discarded.");

    private static readonly HashSet<string> AsyncQueryMethods = new HashSet<string>
    {
        "ToListAsync", "ToArrayAsync",
        "FirstAsync", "FirstOrDefaultAsync",
        "SingleAsync", "SingleOrDefaultAsync",
        "AnyAsync", "AllAsync", "CountAsync",
        "SumAsync", "MinAsync", "MaxAsync", "AverageAsync",
        "FindAsync", "SaveChangesAsync"
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

        var methodName = memberAccess.Name.Identifier.Text;
        if (!AsyncQueryMethods.Contains(methodName))
            return;

        // Must return Task<T> or ValueTask<T>
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol methodSymbol)
            return;

        if (!IsTaskLike(methodSymbol.ReturnType))
            return;

        // Skip if already awaited
        if (invocation.Parent is AwaitExpressionSyntax)
            return;

        // Skip if returned to caller — caller's responsibility to await
        if (invocation.Parent is ReturnStatementSyntax)
            return;

        // Receiver must be an EF Core type: IQueryable<T> or a DbContext-derived type
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

        if (!EFCoreTypeHelpers.ImplementsIQueryable(receiverType) &&
            !EFCoreTypeHelpers.IsDbContextDerived(receiverType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), methodName));
    }

    private static bool IsTaskLike(ITypeSymbol type) =>
        type.Name is "Task" or "ValueTask";
}
