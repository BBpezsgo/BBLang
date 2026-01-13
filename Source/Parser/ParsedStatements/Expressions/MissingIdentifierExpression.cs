using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class MissingIdentifierExpression : IdentifierExpression, IMissingNode
{
    public MissingIdentifierExpression(Token token, Uri file) : base(token, file) { }

    public override string ToString() => "<missing>";
}
