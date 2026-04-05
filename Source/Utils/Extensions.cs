using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Tokenizing;

namespace LanguageCore;

public static class Extensions
{
    public static ImmutableArray<TItem> DefaultIfEmpty<TItem>(this ImmutableArray<TItem> self, TItem @default)
    {
        if (self.IsDefaultOrEmpty)
        { return ImmutableArray.Create(@default); }
        return self;
    }

    public static bool IsSame<TFunction>(this TFunction a, TFunction b)
        where TFunction : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        if (!a.Type.Equals(b.Type)) return false;
        if (!a.Definition.Identifier.Equals(b.Definition.Identifier)) return false;
        if (!Utils.SequenceEquals(a.Parameters.Select(v => v.Type), b.Parameters.Select(v => v.Type))) return false;
        return true;
    }

    public static bool Contains(this ImmutableArray<Token> tokens, string value)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i].Content, value))
            { return true; }
        }
        return false;
    }

    public static int IndexOf(this ImmutableArray<Token> tokens, string value)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i].Content, value))
            { return i; }
        }
        return -1;
    }
}
