using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class AnyCallExpression : Expression, IReadable, IReferenceableTo<CompiledFunctionDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledFunctionDefinition? Reference { get; set; }

    public Expression Expression { get; }
    public ArgumentListExpression Arguments { get; }

    public override Position Position => new(Expression, Arguments);

    public AnyCallExpression(
        Expression expression,
        ArgumentListExpression arguments,
        Uri file) : base(file)
    {
        Expression = expression;
        Arguments = arguments;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append(SurroundingBrackets?.Start);

        result.Append(Expression);
        result.Append(Arguments.ToString());

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);

        return result.ToString();
    }

    public string ToReadable(FindStatementType typeSearch)
    {
        StringBuilder result = new();
        result.Append("...");
        result.Append('(');
        for (int i = 0; i < Arguments.Arguments.Length; i++)
        {
            if (i > 0) { result.Append(", "); }
            result.Append(typeSearch.Invoke(Arguments.Arguments[i], out GeneralType? type, new()) ? type.ToString() : '?');
        }
        result.Append(')');
        return result.ToString();
    }
}
