using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreLint;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingAwaitCodeFix))]
[Shared]
public sealed class MissingAwaitCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingAwaitAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var node = root?.FindNode(context.Diagnostics[0].Location.SourceSpan) as InvocationExpressionSyntax;
        if (node is null)
            return;

        // Only offer the fix when the containing method is already async;
        // making a method async changes its return type, which is out of scope for a single-node fix.
        if (!ContainingMethodIsAsync(node))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add await",
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, ct),
                equivalenceKey: "AddAwait"),
            context.Diagnostics[0]);
    }

    private static bool ContainingMethodIsAsync(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax method)
                return method.Modifiers.Any(SyntaxKind.AsyncKeyword);

            if (ancestor is LocalFunctionStatementSyntax local)
                return local.Modifiers.Any(SyntaxKind.AsyncKeyword);

            if (ancestor is AnonymousFunctionExpressionSyntax anon)
                return anon.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        }
        return false;
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        var awaitExpr = SyntaxFactory.AwaitExpression(
            invocation.WithoutLeadingTrivia())
            .WithLeadingTrivia(invocation.GetLeadingTrivia());

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(invocation, awaitExpr);
        return document.WithSyntaxRoot(newRoot);
    }
}
