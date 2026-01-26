using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class IntLiteralExpression : LiteralExpression
{
    public int Value { get; }

    public override LiteralType Type => LiteralType.Integer;

    public IntLiteralExpression(int value, Token valueToken, Uri file) : base(valueToken, file)
    {
        Value = value;
    }

    public static IntLiteralExpression CreateAnonymous(int value, Position position, Uri file) => new(
        value,
        Token.CreateAnonymous(value.ToString(), TokenType.LiteralNumber, position),
        file
    );

    public override string ToString()
    {
        StringBuilder result = new();
        result.Append(SurroundingBrackets?.Start);

        result.Append(Value);

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);
        return result.ToString();
    }
}
