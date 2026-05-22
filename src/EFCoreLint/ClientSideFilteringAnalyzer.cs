using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCoreLint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClientSideFilteringAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFLINT001";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Client-side filtering after materialization",
        messageFormat: "'{0}' materializes the query before '{1}'. Move '{1}' before '{0}' to filter server-side.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling ToList() or ToArray() before a filtering operation pulls all rows into memory first. "
                   + "Move the filter before materialization so the database does the work.");

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
        var outerCall = (InvocationExpressionSyntax)context.Node;

        // Outer call must be a filtering/aggregation method chained onto something
        if (outerCall.Expression is not MemberAccessExpressionSyntax outerAccess)
            return;

        var outerMethod = outerAccess.Name.Identifier.Text;
        if (!IsFilteringMethod(outerMethod))
            return;

        // The receiver of that call must itself be a ToList() or ToArray() invocation
        if (outerAccess.Expression is not InvocationExpressionSyntax innerCall)
            return;

        if (innerCall.Expression is not MemberAccessExpressionSyntax innerAccess)
            return;

        var innerMethod = innerAccess.Name.Identifier.Text;
        if (innerMethod is not ("ToList" or "ToArray"))
            return;

        // The source of ToList/ToArray must implement IQueryable<T> — i.e. it's a DB query
        var sourceType = context.SemanticModel.GetTypeInfo(innerAccess.Expression, context.CancellationToken).Type;
        if (!ImplementsIQueryable(sourceType))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, outerCall.GetLocation(), innerMethod, outerMethod));
    }

    private static bool IsFilteringMethod(string name) => name is
        "Where" or "First" or "FirstOrDefault" or
        "Single" or "SingleOrDefault" or
        "Any" or "All" or "Count" or
        "Sum" or "Min" or "Max" or "Average";

    private static bool ImplementsIQueryable(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (IsIQueryable(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (IsIQueryable(iface))
                return true;
        }

        return false;
    }

    private static bool IsIQueryable(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "System.Linq" &&
        type.Name is "IQueryable" or "IOrderedQueryable";
}
