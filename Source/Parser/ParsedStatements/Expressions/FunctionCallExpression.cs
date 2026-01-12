using LanguageCore.Compiler;

namespace LanguageCore.Parser.Statements;

public class FunctionCallExpression : Expression, IReadable, IReferenceableTo<CompiledFunctionDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledFunctionDefinition? Reference { get; set; }

    public IdentifierExpression Identifier { get; }
    public ArgumentListExpression Arguments { get; }
    public ArgumentExpression? Object { get; }

    public bool IsMethodCall => Object != null;
    public ImmutableArray<ArgumentExpression> MethodArguments
    {
        get
        {
            if (Object == null) return Arguments.Arguments;
            return Arguments.Arguments.Insert(0, Object);
        }
    }
    public override Position Position => new(Identifier, Arguments, Object);

    public FunctionCallExpression(
        ArgumentExpression? @object,
        IdentifierExpression identifier,
        ArgumentListExpression arguments,
        Uri file) : base(file)
    {
        Object = @object;
        Identifier = identifier;
        Arguments = arguments;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append(SurroundingBrackets?.Start);

        if (Object != null)
        {
            result.Append(Object);
            result.Append('.');
        }
        result.Append(Identifier);
        result.Append(Arguments.ToString());

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);

        return result.ToString();
    }

    public string ToReadable(FindStatementType typeSearch)
    {
        StringBuilder result = new();
        if (Object != null)
        {
            result.Append(typeSearch.Invoke(Object, out GeneralType? type, new()) ? type.ToString() : '?');
            result.Append('.');
        }
        result.Append(Identifier.ToString());
        result.Append('(');
        for (int i = 0; i < Arguments.Arguments.Length; i++)
        {
            if (i > 0) result.Append(", ");
            result.Append(typeSearch.Invoke(Arguments.Arguments[i], out GeneralType? type, new()) ? type.ToString() : '?');
        }
        result.Append(')');
        return result.ToString();
    }
}
