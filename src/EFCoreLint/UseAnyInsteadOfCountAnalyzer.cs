using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCoreLint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseAnyInsteadOfCountAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFLINT006";
    internal const string NegateKey = "Negate";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use Any() instead of Count() for existence checks",
        messageFormat: "Use 'Any()' instead of 'Count()' for existence checks — COUNT(*) scans all matching rows, EXISTS short-circuits on the first.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling Count() on an IQueryable and comparing against 0 or 1 issues a COUNT(*) query "
                   + "that must scan every matching row. Any() translates to EXISTS, which stops at the first match.");

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
        var countCall = (InvocationExpressionSyntax)context.Node;

        if (countCall.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "Count")
            return;

        // Must return int (excludes LongCount)
        if (context.SemanticModel.GetSymbolInfo(countCall, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;

        if (method.ReturnType.SpecialType != SpecialType.System_Int32)
            return;

        // Receiver must be IQueryable<T>
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

        if (!EFCoreTypeHelpers.ImplementsIQueryable(receiverType))
            return;

        // Parent must be a binary expression in an existence-check pattern
        if (countCall.Parent is not BinaryExpressionSyntax binary)
            return;

        var isNegated = GetExistencePattern(binary, countCall);
        if (isNegated is null)
            return;

        var props = ImmutableDictionary<string, string?>.Empty
            .Add(NegateKey, isNegated.Value.ToString());

        context.ReportDiagnostic(Diagnostic.Create(Rule, binary.GetLocation(), props));
    }

    // Returns true if the existence check requires negation (!Any()), false for Any(), null if not an existence pattern.
    // Handles both orientations: Count() > 0 and 0 < Count().
    internal static bool? GetExistencePattern(
        BinaryExpressionSyntax binary,
        InvocationExpressionSyntax countCall)
    {
        bool countIsLeft = binary.Left == countCall;
        bool countIsRight = binary.Right == countCall;

        if (!countIsLeft && !countIsRight)
            return null;

        var literal = countIsLeft ? binary.Right : binary.Left;

        if (literal is not LiteralExpressionSyntax lit ||
            !int.TryParse(lit.Token.ValueText, out int value) ||
            (value != 0 && value != 1))
            return null;

        var kind = binary.Kind();

        if (countIsLeft)
        {
            // q.Count() OP literal
            return (kind, value) switch
            {
                (SyntaxKind.GreaterThanExpression, 0) => false,          // Count() > 0  → Any()
                (SyntaxKind.GreaterThanOrEqualExpression, 1) => false,   // Count() >= 1 → Any()
                (SyntaxKind.NotEqualsExpression, 0) => false,            // Count() != 0 → Any()
                (SyntaxKind.EqualsExpression, 0) => true,                // Count() == 0 → !Any()
                (SyntaxKind.LessThanExpression, 1) => true,              // Count() < 1  → !Any()
                (SyntaxKind.LessThanOrEqualExpression, 0) => true,       // Count() <= 0 → !Any()
                _ => (bool?)null
            };
        }
        else
        {
            // literal OP q.Count()
            return (kind, value) switch
            {
                (SyntaxKind.LessThanExpression, 0) => false,             // 0 < Count()  → Any()
                (SyntaxKind.LessThanOrEqualExpression, 1) => false,      // 1 <= Count() → Any()
                (SyntaxKind.NotEqualsExpression, 0) => false,            // 0 != Count() → Any()
                (SyntaxKind.GreaterThanExpression, 1) => true,           // 1 > Count()  → !Any()
                (SyntaxKind.GreaterThanOrEqualExpression, 0) => true,    // 0 >= Count() → !Any()
                (SyntaxKind.EqualsExpression, 0) => true,                // 0 == Count() → !Any()
                _ => (bool?)null
            };
        }
    }
}
