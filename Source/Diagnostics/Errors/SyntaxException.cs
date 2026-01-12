namespace LanguageCore.Parser;

[ExcludeFromCodeCoverage]
public sealed class SyntaxException : LanguageException
{
    public SyntaxException(string message, ILocated location) : base(message, location.Location.Position, location.Location.File) { }
    public SyntaxException(string message, Location location) : base(message, location.Position, location.File) { }
    public SyntaxException(string message, Position position, Uri file) : base(message, position, file) { }
    public SyntaxException(string message, IPositioned position, Uri file) : base(message, position.Position, file) { }
}
