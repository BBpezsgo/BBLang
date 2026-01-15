namespace LanguageCore.Tokenizing;

public class MissingToken : Token
{
    internal MissingToken(TokenType type, Position position, string? content = null) : base(type, content ?? string.Empty, true, position) { }
}
