namespace LanguageCore.Compiler;

public static class ReferenceExtensions
{
    public static void AddReference<TSource>(this List<Reference<TSource>> references, TSource source, Uri sourceFile)
        => references.Add(new Reference<TSource>(source, sourceFile));

    public static void AddReference<TSource>(this List<Reference<TSource>> references, TSource source)
        where TSource : IInFile
        => references.Add(new Reference<TSource>(source, source.File));
}

public readonly struct Reference
{
    public Uri SourceFile { get; }
    public bool IsImplicit { get; }

    public Reference(Uri sourceFile, bool isImplicit = false)
    {
        SourceFile = sourceFile;
        IsImplicit = isImplicit;
    }
}

public readonly struct Reference<TSource>
{
    public TSource Source { get; }
    public Uri SourceFile { get; }
    public bool IsImplicit { get; }

    public Reference(TSource source, Uri sourceFile, bool isImplicit = false)
    {
        Source = source;
        SourceFile = sourceFile;
        IsImplicit = isImplicit;
    }

    public static implicit operator Reference(Reference<TSource> v) => new(v.SourceFile, v.IsImplicit);
}

public interface IReferenceable
{
    IEnumerable<Reference> References { get; }
}

public interface IReferenceable<TBy> : IReferenceable
{
    new List<Reference<TBy>> References { get; }
    IEnumerable<Reference> IReferenceable.References => References.Select(v => (Reference)v);
}
