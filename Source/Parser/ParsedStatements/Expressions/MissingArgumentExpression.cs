namespace LanguageCore.Parser.Statements;

public class MissingArgumentExpression : ArgumentExpression, IMissingNode
{
    public override Position Position { get; }

    public MissingArgumentExpression(Location location) : base(null, new MissingExpression(location), location.File) => Position = location.Position;
    public MissingArgumentExpression(ILocated location) : base(null, new MissingExpression(location), location.Location.File) => Position = location.Location.Position;
    public MissingArgumentExpression(Position position, Uri file) : base(null, new MissingExpression(position, file), file) => Position = position;
}
