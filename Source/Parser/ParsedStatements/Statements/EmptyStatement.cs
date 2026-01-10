namespace LanguageCore.Parser.Statements;

public class EmptyStatement : Statement
{
    public override Position Position { get; }

    public EmptyStatement(Position position, Uri file) : base(file)
    {
        Position = position;
    }
}
