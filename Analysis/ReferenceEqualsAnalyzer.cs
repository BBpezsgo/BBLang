using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LanguageCore.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ReferenceEqualsAnalyzer : DiagnosticAnalyzer
    {
        static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: "MY002",
            title: "No object.ReferenceEquals comparision",
            messageFormat: "Don't use 'object.ReferenceEquals'",
            category: "Quality",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode, SyntaxKind.InvocationExpression);
        }

        void AnalyzeSyntaxNode(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is InvocationExpressionSyntax invocation)) return;

            if (!(context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol is IMethodSymbol symbol)) return;

            if (symbol.Name == "ReferenceEquals" && symbol.ContainingType.SpecialType == SpecialType.System_Object)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
            }
        }
    }
}
