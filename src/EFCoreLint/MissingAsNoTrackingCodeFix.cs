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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingAsNoTrackingCodeFix))]
[Shared]
public sealed class MissingAsNoTrackingCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingAsNoTrackingAnalyzer.DiagnosticId);

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
                title: "Add AsNoTracking()",
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, ct),
                equivalenceKey: "AddAsNoTracking"),
            context.Diagnostics[0]);
    }

    // Transforms: source.ToList()  →  source.AsNoTracking().ToList()
    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        // invocation  = source.ToList()
        // memberAccess = source.ToList
        // source       = source
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var source = memberAccess.Expression;

        // Build source.AsNoTracking
        var asNoTrackingAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source,
            SyntaxFactory.IdentifierName("AsNoTracking"));

        // Build source.AsNoTracking()
        var asNoTrackingCall = SyntaxFactory.InvocationExpression(asNoTrackingAccess);

        // Build source.AsNoTracking().ToList
        var newMemberAccess = memberAccess.WithExpression(asNoTrackingCall);

        // Build source.AsNoTracking().ToList()
        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithTriviaFrom(invocation);

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}
