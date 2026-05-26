using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreLint;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseAnyInsteadOfCountCodeFix))]
[Shared]
public sealed class UseAnyInsteadOfCountCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UseAnyInsteadOfCountAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];
        var node = root?.FindNode(diagnostic.Location.SourceSpan) as BinaryExpressionSyntax;
        if (node is null)
            return;

        bool negate = diagnostic.Properties.TryGetValue(UseAnyInsteadOfCountAnalyzer.NegateKey, out var val)
            && val == "True";

        var title = negate ? "Replace with !Any()" : "Replace with Any()";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, negate, ct),
                equivalenceKey: title),
            diagnostic);
    }

    // Transforms: source.Count() > 0        →  source.Any()
    //             source.Count(pred) == 0    →  !source.Any(pred)
    private static async Task<Document> ApplyFixAsync(
        Document document,
        BinaryExpressionSyntax binary,
        bool negate,
        CancellationToken ct)
    {
        // Find the Count() call within the binary expression
        var countCall = (binary.Left as InvocationExpressionSyntax ?? binary.Right as InvocationExpressionSyntax)!;
        var countAccess = (MemberAccessExpressionSyntax)countCall.Expression;

        // Build source.Any  (swap "Count" → "Any", keep the receiver)
        var anyName = SyntaxFactory.IdentifierName("Any");
        var anyAccess = countAccess.WithName(anyName);

        // Build source.Any(pred) — copy arguments from Count() if any
        var anyCall = (ExpressionSyntax)countCall.WithExpression(anyAccess);

        // Optionally wrap in !
        ExpressionSyntax replacement = negate
            ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, anyCall)
            : anyCall;

        replacement = replacement.WithTriviaFrom(binary);

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(binary, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}
