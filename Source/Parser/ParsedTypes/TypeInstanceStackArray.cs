using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Parser;

public class TypeInstanceStackArray : TypeInstance, IEquatable<TypeInstanceStackArray?>
{
    /// <summary> Set by the compiler </summary>
    public ArrayType? CompiledType { get; set; }

    public Expression? StackArraySize { get; }
    public TypeInstance StackArrayOf { get; }
    public TokenPair SquareBrackets { get; }

    public TypeInstanceStackArray(TypeInstance stackArrayOf, Expression? sizeValue, TokenPair squareBrackets, Uri file) : base(file)
    {
        StackArrayOf = stackArrayOf;
        StackArraySize = sizeValue;
        SquareBrackets = squareBrackets;
    }

    public override bool Equals(object? obj) => obj is TypeInstanceStackArray other && Equals(other);
    public override bool Equals(TypeInstance? other) => other is TypeInstanceStackArray other_ && Equals(other_);
    public bool Equals(TypeInstanceStackArray? other)
    {
        if (other is null) return false;
        if (!StackArrayOf.Equals(other.StackArrayOf)) return false;

        if ((StackArraySize is null) != (other.StackArraySize is null)) return false;

        return true;
    }

    public override int GetHashCode() => HashCode.Combine((byte)1, StackArrayOf, StackArraySize);

    public override Position Position => new(StackArrayOf, StackArraySize, SquareBrackets);

    public override string ToString() => $"{StackArrayOf}[{StackArraySize}]";
    public override string ToString(IReadOnlyDictionary<string, GeneralType>? typeArguments) => $"{StackArrayOf.ToString(typeArguments)}[{StackArraySize}]";
}
