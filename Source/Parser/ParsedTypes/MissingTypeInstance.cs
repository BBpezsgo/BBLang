namespace LanguageCore.Parser;

public class MissingTypeInstance : TypeInstance
{
    public override Position Position { get; }

    public MissingTypeInstance(ILocated location) : base(location.Location.File)
    {
        Position = location.Location.Position;
    }

    public MissingTypeInstance(IPositioned position, Uri file) : base(file)
    {
        Position = position.Position;
    }

    public MissingTypeInstance(Position position, Uri file) : base(file)
    {
        Position = position;
    }

    public override bool Equals(TypeInstance? other) => Utils.ReferenceEquals(this, other);
    public override int GetHashCode() => (this as object).GetHashCode();
    public override string ToString() => "<missing type>";
}
