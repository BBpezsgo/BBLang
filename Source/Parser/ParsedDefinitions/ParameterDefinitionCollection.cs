using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Parser;

public class ParameterDefinitionCollection :
    IPositioned,
    IInFile
{
    public TokenPair Brackets { get; }
    public Uri File { get; }

    public int Count => Parameters.Length;
    public Position Position =>
        new Position(Brackets)
        .Union(Parameters);

    public ParameterDefinition this[int index] => Parameters[index];
    public ParameterDefinition this[Index index] => Parameters[index];

    public readonly ImmutableArray<ParameterDefinition> Parameters;

    public ParameterDefinitionCollection(ParameterDefinitionCollection other)
    {
        Parameters = other.Parameters;
        Brackets = other.Brackets;
        File = other.File;
    }

    public ParameterDefinitionCollection(ImmutableArray<ParameterDefinition> parameterDefinitions, TokenPair brackets, Uri file)
    {
        Parameters = parameterDefinitions;
        Brackets = brackets;
        File = file;
    }

    public bool TypeEquals(ParameterDefinitionCollection? other)
    {
        if (other is null) return false;
        if (Parameters.Length != other.Parameters.Length) return false;
        for (int i = 0; i < Parameters.Length; i++)
        { if (!Parameters[i].Type.Equals(other.Parameters[i].Type)) return false; }
        return true;
    }

    public static ParameterDefinitionCollection CreateAnonymous(ImmutableArray<ParameterDefinition> parameterDefinitions, Uri file)
        => new(parameterDefinitions, TokenPair.CreateAnonymous(new Position(parameterDefinitions), "(", ")"), file);

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append(Brackets.Start);

        for (int i = 0; i < Parameters.Length; i++)
        {
            if (i > 0) result.Append(", ");
            if (Parameters[i].Modifiers.Length > 0)
            {
                result.AppendJoin(", ", Parameters[i].Modifiers);
                result.Append(' ');
            }
            result.Append(Parameters[i].Type);
        }

        result.Append(Brackets.End);

        return result.ToString();
    }

    public string ToString(ImmutableArray<GeneralType> types)
    {
        StringBuilder result = new();

        result.Append(Brackets.Start);

        for (int i = 0; i < Parameters.Length; i++)
        {
            if (i > 0) result.Append(", ");
            if (Parameters[i].Modifiers.Length > 0)
            {
                result.AppendJoin(' ', Parameters[i].Modifiers);
                result.Append(' ');
            }
            result.Append(types[i].ToString());
        }

        result.Append(Brackets.End);

        return result.ToString();
    }
}
