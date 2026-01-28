using LanguageCore.Compiler;

namespace LanguageCore.Parser.Statements;

public class IndexCallExpression : Expression, IReferenceableTo<CompiledFunctionDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledFunctionDefinition? Reference { get; set; }

    public Expression Object { get; }
    public ArgumentExpression Index { get; }
    public TokenPair Brackets { get; }

    public override Position Position => new(Object, Index);

    public IndexCallExpression(
        Expression @object,
        ArgumentExpression indexStatement,
        TokenPair brackets,
        Uri file) : base(file)
    {
        Object = @object;
        Index = indexStatement;
        Brackets = brackets;
    }

    public override string ToString()
        => $"{SurroundingBrackets?.Start}{Object}{Brackets.Start}{Index}{Brackets.End}{SurroundingBrackets?.End}{Semicolon}";
}
