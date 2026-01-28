using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class ConstructorCallExpression : Expression, IReferenceableTo<CompiledConstructorDefinition>, IHaveType
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledConstructorDefinition? Reference { get; set; }

    public Token Keyword { get; }
    public TypeInstance Type { get; }
    public ArgumentListExpression Arguments { get; }

    public override Position Position =>
        new Position(Keyword, Type, Arguments)
        .Union(Arguments);

    public ConstructorCallExpression(
        Token keyword,
        TypeInstance typeName,
        ArgumentListExpression arguments,
        Uri file) : base(file)
    {
        Keyword = keyword;
        Type = typeName;
        Arguments = arguments;
    }

    public override string ToString()
    {
        StringBuilder result = new();
        result.Append(SurroundingBrackets?.Start);

        result.Append(Keyword);
        result.Append(' ');
        result.Append(Type);
        result.Append(Arguments.ToString());

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);
        return result.ToString();
    }
}
