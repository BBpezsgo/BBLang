using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class MissingBlock : Block, IMissingNode
{
    public override Position Position { get; }

    public MissingBlock(Location location) : this(location.Position, location.File) { }
    public MissingBlock(ILocated location) : this(location.Location.Position, location.Location.File) { }
    public MissingBlock(Position position, Uri file) : base(ImmutableArray<Statement>.Empty, new TokenPair(new MissingToken(TokenType.Operator, position, "{"), new MissingToken(TokenType.Operator, position, "}")), file) => Position = position;
}
