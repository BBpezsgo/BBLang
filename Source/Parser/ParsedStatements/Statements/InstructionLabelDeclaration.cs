using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class InstructionLabelDeclaration : Statement
{
    public Token Identifier { get; }
    public Token Colon { get; }

    public override Position Position => new(
        Identifier,
        Colon
    );

    public InstructionLabelDeclaration(
        Token identifier,
        Token colon,
        Uri file) : base(file)
    {
        Identifier = identifier;
        Colon = colon;
    }

    public InstructionLabelDeclaration(InstructionLabelDeclaration other) : base(other.File)
    {
        Identifier = other.Identifier;
        Colon = other.Colon;
    }

    public override string ToString() => $"{Identifier}{Colon}";
}
