using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreLint;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ClientSideFilteringCodeFix))]
[Shared]
public sealed class ClientSideFilteringCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ClientSideFilteringAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var node = root?.FindNode(context.Diagnostics[0].Location.SourceSpan) as InvocationExpressionSyntax;
        if (node is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Move filter before materialization",
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, ct),
                equivalenceKey: "MoveFilterBeforeMaterialization"),
            context.Diagnostics[0]);
    }

    // Transforms: source.ToList().Where(pred)  →  source.Where(pred).ToList()
    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax outerCall,
        CancellationToken ct)
    {
        // outerCall        = source.ToList().Where(pred)
        // outerAccess      = source.ToList().Where
        // innerCall        = source.ToList()
        // innerAccess      = source.ToList
        // source           = source
        var outerAccess = (MemberAccessExpressionSyntax)outerCall.Expression;
        var innerCall = (InvocationExpressionSyntax)outerAccess.Expression;
        var innerAccess = (MemberAccessExpressionSyntax)innerCall.Expression;
        var source = innerAccess.Expression;

        // Build source.Where  (swap the receiver of the outer access to be source directly)
        var newFilterAccess = outerAccess.WithExpression(source);
        // Build source.Where(pred)
        var newFilterCall = outerCall.WithExpression(newFilterAccess);
        // Build source.Where(pred).ToList  (swap receiver of inner access to be the filter call)
        var newMaterializeAccess = innerAccess.WithExpression(newFilterCall);
        // Build source.Where(pred).ToList()
        var newMaterializeCall = innerCall
            .WithExpression(newMaterializeAccess)
            .WithTriviaFrom(outerCall);

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(outerCall, newMaterializeCall);
        return document.WithSyntaxRoot(newRoot);
    }
}
