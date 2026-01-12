namespace LanguageCore.Parser.Statements;

public class MissingExpression : Expression, IMissingNode
{
    public override Position Position { get; }

    public MissingExpression(Location location) : base(location.File) => Position = location.Position;
    public MissingExpression(ILocated location) : base(location.Location.File) => Position = location.Location.Position;
    public MissingExpression(Position position, Uri file) : base(file) => Position = position;
}
