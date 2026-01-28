using LanguageCore.Compiler;

namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class DiagnosticAt :
    Diagnostic,
    IEquatable<DiagnosticAt>,
    IPositioned,
    IInFile,
    ILocated
{
    public Position Position { get; }
    public Uri File { get; }
    public Location Location => new(Position, File);

    public DiagnosticAt(DiagnosticsLevel level, string message, Position position, Uri file, bool @break, ImmutableArray<Diagnostic> suberrors)
        : base(level, message, false, suberrors)
    {
        Position = position;
        File = file;

        if (@break)
        { Break(); }
    }

    public DiagnosticAt(DiagnosticsLevel level, string message, Position position, Uri file, bool @break, ImmutableArray<DiagnosticAt> suberrors)
        : base(level, message, false, suberrors.ToImmutableArray(v => (Diagnostic)v))
    {
        Position = position;
        File = file;

        if (@break)
        { Break(); }
    }

#if UNITY
    public new DiagnosticAt WithSuberrors(Diagnostic? suberror) => suberror is null ? this : new(Level, Message, Position, File, false, ImmutableArray.Create(suberror));
    public new DiagnosticAt WithSuberrors(params Diagnostic?[] suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public new DiagnosticAt WithSuberrors(IEnumerable<Diagnostic?> suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public new DiagnosticAt WithSuberrors(ImmutableArray<Diagnostic> suberrors) => suberrors.IsDefaultOrEmpty ? this : new(Level, Message, Position, File, false, SubErrors.AddRange(suberrors));
#else
    public override DiagnosticAt WithSuberrors(Diagnostic? suberror) => suberror is null ? this : new(Level, Message, Position, File, false, ImmutableArray.Create(suberror));
    public override DiagnosticAt WithSuberrors(params Diagnostic?[] suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public override DiagnosticAt WithSuberrors(IEnumerable<Diagnostic?> suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public override DiagnosticAt WithSuberrors(ImmutableArray<Diagnostic> suberrors) => suberrors.IsDefaultOrEmpty ? this : new(Level, Message, Position, File, false, SubErrors.AddRange(suberrors));
#endif

    [DoesNotReturn]
    public override void Throw() => throw ToException();

#if UNITY
    public new LanguageExceptionAt ToException() => new(Message, Position, File, SubErrors.ToImmutableArray(v => v.ToException() as Exception));
#else
    public override LanguageExceptionAt ToException() => new(Message, Position, File, SubErrors.ToImmutableArray(v => v.ToException() as Exception));
#endif

    #region Internal

    public static DiagnosticAt Internal(string message, IPositioned? position, Uri file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position?.Position ?? Position.UnknownPosition, file, @break, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Internal(string message, Position position, Uri file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position, file, @break, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Internal(string message, ILocated location, bool @break = true)
        => new(DiagnosticsLevel.Error, message, location.Location.Position, location.Location.File, @break, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region Error

    public static DiagnosticAt Error(string message, IPositioned? position, Uri file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position?.Position ?? Position.UnknownPosition, file, @break, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Error(string message, Position position, Uri file, bool @break = false)
        => new(DiagnosticsLevel.Error, message, position, file, @break, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Error(string message, ILocated location, bool @break = true)
        => new(DiagnosticsLevel.Error, message, location.Location.Position, location.Location.File, @break, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region Warning

    public static DiagnosticAt Warning(string message, IPositioned? position, Uri file)
        => new(DiagnosticsLevel.Warning, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Warning(string message, Position position, Uri file)
        => new(DiagnosticsLevel.Warning, message, position, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Warning(string message, ILocated location)
        => new(DiagnosticsLevel.Warning, message, location.Location.Position, location.Location.File, false, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region Information

    public static DiagnosticAt Information(string message, IPositioned? position, Uri file)
        => new(DiagnosticsLevel.Information, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Information(string message, Position position, Uri file)
        => new(DiagnosticsLevel.Information, message, position, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Information(string message, ILocated location)
        => new(DiagnosticsLevel.Information, message, location.Location.Position, location.Location.File, false, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region Hint

    public static DiagnosticAt Hint(string message, IPositioned? position, Uri file)
        => new(DiagnosticsLevel.Hint, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Hint(string message, Position position, Uri file)
        => new(DiagnosticsLevel.Hint, message, position, file, false, ImmutableArray<Diagnostic>.Empty);

    public static DiagnosticAt Hint(string message, ILocated location)
        => new(DiagnosticsLevel.Hint, message, location.Location.Position, location.Location.File, false, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region OptimizationNotice

    public static DiagnosticAt OptimizationNotice(string message, ILocated location)
        => new(DiagnosticsLevel.OptimizationNotice, message, location.Location.Position, location.Location.File, false, ImmutableArray<Diagnostic>.Empty);

    #endregion

    #region FailedOptimization

    public static DiagnosticAt FailedOptimization(string message, ILocated location)
        => new(DiagnosticsLevel.FailedOptimization, message, location.Location.Position, location.Location.File, false, ImmutableArray<Diagnostic>.Empty);

    #endregion

    public (string SourceCode, string Arrows)? GetArrows(IEnumerable<ISourceProvider>? sourceProviders = null)
    {
        if (File == null) return null;
        if (!File.IsFile) return null;
        string? source = SourceCodeManager.LoadSource(sourceProviders, File.ToString());
        return source is not null ? LanguageExceptionAt.GetArrows(Position, source) : null;
    }

    public override string ToString()
        => LanguageExceptionAt.Format(Message, Position, File);

    public bool Equals([NotNullWhen(true)] DiagnosticAt? other)
    {
        if (other is null) return false;
        if (Message != other.Message) return false;
        if (Position != other.Position) return false;
        if (File != other.File) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is DiagnosticAt other && Equals(other);

    public override int GetHashCode() => Message.GetHashCode();
}
