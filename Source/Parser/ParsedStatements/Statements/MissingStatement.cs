namespace LanguageCore.Parser.Statements;

public class MissingStatement : Statement, IMissingNode
{
    public override Position Position { get; }

    public MissingStatement(Location location) : base(location.File) => Position = location.Position;
    public MissingStatement(ILocated location) : base(location.Location.File) => Position = location.Location.Position;
    public MissingStatement(Position position, Uri file) : base(file) => Position = position;
}
