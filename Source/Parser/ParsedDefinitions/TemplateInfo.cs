using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class TemplateInfo : IPositioned
{
    public TokenPair Brackets { get; }
    public ImmutableArray<Token> Parameters { get; }

    public Position Position =>
        new Position(Parameters.As<IPositioned>().DefaultIfEmpty(Brackets))
        .Union(Brackets);

    public TemplateInfo(TokenPair brackets, ImmutableArray<Token> typeParameters)
    {
        Brackets = brackets;
        Parameters = typeParameters;
    }

    public bool TryGetTypeArgumentIndex(string typeArgumentName, out int index)
    {
        index = -1;
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i].Content == typeArgumentName)
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    public override string ToString() => $"{Brackets.Start}{string.Join(", ", Parameters)}{Brackets.End}";
}
