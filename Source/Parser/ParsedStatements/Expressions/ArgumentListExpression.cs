using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class ArgumentListExpression : Statement
{
    public ImmutableArray<ArgumentExpression> Arguments { get; }
    public ImmutableArray<Token> Commas { get; }
    public TokenPair Brackets { get; }

    public override Position Position => new Position(Arguments).Union(Brackets);

    public ArgumentListExpression(
        ImmutableArray<ArgumentExpression> arguments,
        ImmutableArray<Token> commas,
        TokenPair brackets,
        Uri file) : base(file)
    {
        Arguments = arguments;
        Commas = commas;
        Brackets = brackets;
    }

    public static ArgumentListExpression CreateAnonymous(TokenPair brackets, Uri file) => new(
        ImmutableArray<ArgumentExpression>.Empty,
        ImmutableArray<Token>.Empty,
        brackets,
        file
    );

    public static ArgumentListExpression CreateAnonymous(Uri file) => new(
        ImmutableArray<ArgumentExpression>.Empty,
        ImmutableArray<Token>.Empty,
        TokenPair.CreateAnonymous("(", ")"),
        file
    );

    public override string ToString() => $"({string.Join(", ", Arguments)})";
}
