using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class MissingLiteral : LiteralExpression, IMissingNode
{
    public override Position Position { get; }

    public override LiteralType Type => LiteralType.Invalid;

    public MissingLiteral(Location location) : this(location.Position, location.File) { }
    public MissingLiteral(ILocated location) : this(location.Location.Position, location.Location.File) { }
    public MissingLiteral(Position position, Uri file) : base(new MissingToken(TokenType.Whitespace, position), file) => Position = position;

    public override string ToString() => "<missing>";
}
