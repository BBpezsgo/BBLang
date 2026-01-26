using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public abstract class LiteralExpression : Expression
{
    public Token ValueToken { get; }
    public abstract LiteralType Type { get; }

    public override Position Position => ValueToken.Position;

    public LiteralExpression(Token valueToken, Uri file) : base(file)
    {
        ValueToken = valueToken;
    }
}
