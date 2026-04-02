using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LanguageCore.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ObjectEqualityCheckAnalyzer : DiagnosticAnalyzer
    {
        static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: "MY001",
            title: "No object comparision",
            messageFormat: "Don't compare values of type 'object'",
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

            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode, SyntaxKind.EqualsExpression | SyntaxKind.NotEqualsExpression);
        }

        void AnalyzeSyntaxNode(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is BinaryExpressionSyntax binary)) return;

            ITypeSymbol leftType = context.SemanticModel.GetTypeInfo(binary.Left).ConvertedType;
            ITypeSymbol rightType = context.SemanticModel.GetTypeInfo(binary.Right).ConvertedType;
            if (leftType == null || rightType == null) return;

            IMethodSymbol symbol = context.SemanticModel.GetSymbolInfo(binary).Symbol as IMethodSymbol;
            if (symbol?.MethodKind == MethodKind.UserDefinedOperator) return;

            if (IsObjectType(leftType) && IsObjectType(rightType))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
            }
        }

        static bool IsObjectType(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_Object;
        }
    }
}
