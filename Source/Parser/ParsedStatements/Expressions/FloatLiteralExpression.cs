using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class FloatLiteralExpression : LiteralExpression
{
    public float Value { get; }

    public override LiteralType Type => LiteralType.Float;

    public FloatLiteralExpression(float value, Token valueToken, Uri file) : base(valueToken, file)
    {
        Value = value;
    }

    public static FloatLiteralExpression CreateAnonymous(float value, Position position, Uri file) => new(
        value,
        Token.CreateAnonymous(value.ToString(), TokenType.LiteralFloat, position),
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
