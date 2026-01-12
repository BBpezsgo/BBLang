using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class MissingIdentifier : IdentifierExpression, IMissingNode
{
    public MissingIdentifier(Token token, Uri file) : base(token, file)
    {

    }
}
