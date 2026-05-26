using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreLint;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncBlockingCallCodeFix))]
[Shared]
public sealed class SyncBlockingCallCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SyncBlockingCallAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var blockingNode = root.FindNode(diagnostic.Location.SourceSpan);

        var efCall = ExtractEfCall(blockingNode);
        if (efCall is null)
            return;

        // Only offer the fix when the containing method is already async
        if (!ContainingMethodIsAsync(blockingNode))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with await",
                createChangedDocument: ct => ApplyFixAsync(context.Document, blockingNode, efCall, ct),
                equivalenceKey: "ReplaceBlockingWithAwait"),
            diagnostic);
    }

    // Extracts the inner EF Core async invocation from each blocking pattern.
    private static InvocationExpressionSyntax? ExtractEfCall(SyntaxNode blockingNode) =>
        blockingNode switch
        {
            // .Result — the blocking node IS the MemberAccessExpression (q.ToListAsync().Result)
            MemberAccessExpressionSyntax { Name.Identifier.Text: "Result" } resultAccess
                when resultAccess.Expression is InvocationExpressionSyntax efCall
                => efCall,

            // .Wait() — the blocking node is the InvocationExpression (q.ToListAsync().Wait())
            InvocationExpressionSyntax waitInvocation
                when waitInvocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Wait" } waitAccess &&
                     waitAccess.Expression is InvocationExpressionSyntax efCall
                => efCall,

            // .GetAwaiter().GetResult() — the blocking node is the outer InvocationExpression
            InvocationExpressionSyntax getResultInvocation
                when getResultInvocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetResult" } getResultAccess &&
                     getResultAccess.Expression is InvocationExpressionSyntax getAwaiterInvocation &&
                     getAwaiterInvocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetAwaiter" } getAwaiterAccess &&
                     getAwaiterAccess.Expression is InvocationExpressionSyntax efCall
                => efCall,

            _ => null
        };

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

    // Replaces the blocking expression with: await <efCall>
    private static async Task<Document> ApplyFixAsync(
        Document document,
        SyntaxNode blockingNode,
        InvocationExpressionSyntax efCall,
        CancellationToken ct)
    {
        var awaitExpr = SyntaxFactory
            .AwaitExpression(efCall.WithoutLeadingTrivia())
            .WithLeadingTrivia(blockingNode.GetLeadingTrivia());

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(blockingNode, awaitExpr);
        return document.WithSyntaxRoot(newRoot);
    }
}
