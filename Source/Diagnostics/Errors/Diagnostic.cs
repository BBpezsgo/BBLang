using LanguageCore.Compiler;

namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class Diagnostic :
    IEquatable<Diagnostic>,
    IDiagnostic
{
    public DiagnosticsLevel Level { get; }
    public string Message { get; }
    public Position Position { get; }
    public Uri? File { get; }
    public ImmutableArray<IDiagnostic> SubErrors { get; }
    bool IsDebugged;

    IEnumerable<IDiagnostic> IDiagnostic.SubErrors => SubErrors;

    public Diagnostic(DiagnosticsLevel level, string message, Position position, Uri? file, bool @break, ImmutableArray<IDiagnostic> suberrors)
    {
        Level = level;
        Message = message;
        Position = position;
        File = file;
        SubErrors = suberrors;
        IsDebugged = false;

        if (@break)
        { Break(); }
    }

    public Diagnostic(DiagnosticsLevel level, string message, Position position, Uri? file, bool @break, ImmutableArray<Diagnostic> suberrors)
    {
        Level = level;
        Message = message;
        Position = position;
        File = file;
        SubErrors = suberrors.CastArray<IDiagnostic>();
        IsDebugged = false;

        if (@break)
        { Break(); }
    }

    public Diagnostic WithSuberrors(IDiagnostic? suberror) => suberror is null ? this : new(Level, Message, Position, File, false, ImmutableArray.Create<IDiagnostic>(suberror));
    public Diagnostic WithSuberrors(params IDiagnostic?[] suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public Diagnostic WithSuberrors(IEnumerable<IDiagnostic?> suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public Diagnostic WithSuberrors(ImmutableArray<IDiagnostic> suberrors)
    {
        if (SubErrors.IsDefaultOrEmpty)
        {
            return suberrors.IsDefaultOrEmpty ? this : new(Level, Message, Position, File, false, suberrors);
        }
        else
        {
            return suberrors.IsDefaultOrEmpty ? this : new(Level, Message, Position, File, false, SubErrors.AddRange(suberrors));
        }
    }

    [DoesNotReturn]
    public void Throw() => throw this.ToException();

    #region Internal

    public static Diagnostic Internal(string message, IPositioned? position, Uri? file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position?.Position ?? Position.UnknownPosition, file, @break, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Internal(string message, Position position, Uri? file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position, file, @break, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Internal(string message, ILocated location, bool @break = true)
        => new(DiagnosticsLevel.Error, message, location.Location.Position, location.Location.File, @break, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region Error

    public static Diagnostic Error(string message, IPositioned? position, Uri? file, bool @break = true)
        => new(DiagnosticsLevel.Error, message, position?.Position ?? Position.UnknownPosition, file, @break, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Error(string message, Position position, Uri? file, bool @break = false)
        => new(DiagnosticsLevel.Error, message, position, file, @break, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Error(string message, ILocated location, bool @break = true)
        => new(DiagnosticsLevel.Error, message, location.Location.Position, location.Location.File, @break, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region Warning

    public static Diagnostic Warning(string message, IPositioned? position, Uri? file)
        => new(DiagnosticsLevel.Warning, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Warning(string message, Position position, Uri? file)
        => new(DiagnosticsLevel.Warning, message, position, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Warning(string message, ILocated location)
        => new(DiagnosticsLevel.Warning, message, location.Location.Position, location.Location.File, false, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region Information

    public static Diagnostic Information(string message, IPositioned? position, Uri? file)
        => new(DiagnosticsLevel.Information, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Information(string message, Position position, Uri? file)
        => new(DiagnosticsLevel.Information, message, position, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Information(string message, ILocated location)
        => new(DiagnosticsLevel.Information, message, location.Location.Position, location.Location.File, false, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region Hint

    public static Diagnostic Hint(string message, IPositioned? position, Uri? file)
        => new(DiagnosticsLevel.Hint, message, position?.Position ?? Position.UnknownPosition, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Hint(string message, Position position, Uri? file)
        => new(DiagnosticsLevel.Hint, message, position, file, false, ImmutableArray<IDiagnostic>.Empty);

    public static Diagnostic Hint(string message, ILocated location)
        => new(DiagnosticsLevel.Hint, message, location.Location.Position, location.Location.File, false, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region OptimizationNotice

    public static Diagnostic OptimizationNotice(string message, ILocated location)
        => new(DiagnosticsLevel.OptimizationNotice, message, location.Location.Position, location.Location.File, false, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    #region FailedOptimization

    public static Diagnostic FailedOptimization(string message, ILocated location)
        => new(DiagnosticsLevel.FailedOptimization, message, location.Location.Position, location.Location.File, false, ImmutableArray<IDiagnostic>.Empty);

    #endregion

    public Diagnostic Break()
    {
        if (!IsDebugged)
        {
#if DEBUG && !UNITY
            Debugger.Break();
#endif
        }
        IsDebugged = true;

#if TESTING
        Throw();
#endif

        return this;
    }

    public (string SourceCode, string Arrows)? GetArrows(IEnumerable<ISourceProvider>? sourceProviders = null)
    {
        if (File == null) return null;
        if (!File.IsFile) return null;
        string? source = SourceCodeManager.LoadSourceSync(sourceProviders, File.ToString());
        return source is not null ? LanguageException.GetArrows(Position, source) : null;
    }

    public override string ToString()
        => LanguageException.Format(Message, Position, File);

    public bool Equals([NotNullWhen(true)] Diagnostic? other)
    {
        if (other is null) return false;
        if (Message != other.Message) return false;
        if (Position != other.Position) return false;
        if (File != other.File) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Diagnostic other && Equals(other);

    public override int GetHashCode() => Message.GetHashCode();
}
