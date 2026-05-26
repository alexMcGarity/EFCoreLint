using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCoreLint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncBlockingCallAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFLINT007";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Blocking on EF Core async operation",
        messageFormat: "Blocking on an EF Core async operation via '{0}' can deadlock in ASP.NET Core. Use 'await' instead.",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling .Result, .Wait(), or .GetAwaiter().GetResult() on a Task returned by an EF Core "
                   + "async method blocks the calling thread and can deadlock in ASP.NET Core applications. "
                   + "Use 'await' to asynchronously wait for the result.");

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
        var efCall = (InvocationExpressionSyntax)context.Node;

        if (efCall.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!AsyncQueryMethods.Contains(memberAccess.Name.Identifier.Text))
            return;

        // Verify return type is Task-like
        if (context.SemanticModel.GetSymbolInfo(efCall, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;

        if (method.ReturnType.Name is not ("Task" or "ValueTask"))
            return;

        // Receiver must be an EF Core type
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

        if (!EFCoreTypeHelpers.ImplementsIQueryable(receiverType) &&
            !EFCoreTypeHelpers.IsDbContextDerived(receiverType))
            return;

        // Check for blocking access patterns on the returned Task
        switch (efCall.Parent)
        {
            // q.ToListAsync().Result
            case MemberAccessExpressionSyntax resultAccess
                when resultAccess.Name.Identifier.Text == "Result":
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, resultAccess.GetLocation(), ".Result"));
                break;

            // q.ToListAsync().Wait()
            case MemberAccessExpressionSyntax waitAccess
                when waitAccess.Name.Identifier.Text == "Wait" &&
                     waitAccess.Parent is InvocationExpressionSyntax waitCall:
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, waitCall.GetLocation(), ".Wait()"));
                break;

            // q.ToListAsync().GetAwaiter().GetResult()
            case MemberAccessExpressionSyntax getAwaiterAccess
                when getAwaiterAccess.Name.Identifier.Text == "GetAwaiter" &&
                     getAwaiterAccess.Parent is InvocationExpressionSyntax getAwaiterCall &&
                     getAwaiterCall.Parent is MemberAccessExpressionSyntax getResultAccess &&
                     getResultAccess.Name.Identifier.Text == "GetResult" &&
                     getResultAccess.Parent is InvocationExpressionSyntax getResultCall:
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, getResultCall.GetLocation(), ".GetAwaiter().GetResult()"));
                break;
        }
    }
}
