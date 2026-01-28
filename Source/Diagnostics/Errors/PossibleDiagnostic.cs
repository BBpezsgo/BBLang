namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class PossibleDiagnostic
{
    public string Message;
    public readonly ImmutableArray<PossibleDiagnostic> SubErrors;
    readonly Position Position;
    readonly Uri? File;
    readonly bool ShouldBreak;

    [MemberNotNullWhen(true, nameof(File))]
    bool IsPopulated => File is not null && Position != default;

    public PossibleDiagnostic(string message, bool shouldBreak = true)
        : this(message, ImmutableArray<PossibleDiagnostic>.Empty, shouldBreak)
    { }

    public PossibleDiagnostic(string message, params PossibleDiagnostic[] suberrors)
        : this(message, suberrors.ToImmutableArray())
    { }

    public PossibleDiagnostic(string message, ImmutableArray<PossibleDiagnostic> suberrors, bool shouldBreak = true)
    {
        Message = message;
        SubErrors = suberrors;
        ShouldBreak = shouldBreak;
    }

    public PossibleDiagnostic(string message, ILocated? location)
        : this(message, location, ImmutableArray<PossibleDiagnostic>.Empty)
    { }

    public PossibleDiagnostic(string message, ILocated? location, params PossibleDiagnostic[] suberrors)
        : this(message, location, suberrors.ToImmutableArray())
    { }

    public PossibleDiagnostic(string message, ILocated? location, ImmutableArray<PossibleDiagnostic> suberrors)
    {
        Message = message;
        SubErrors = suberrors;
        if (location is not null)
        {
            Position = location.Location.Position;
            File = location.Location.File;
        }
    }

    public void Throw()
    {
        if (IsPopulated)
        { throw new LanguageExceptionAt(Message, Position, File!); }
        else
        { throw new LanguageException(Message); }
    }

    public PossibleDiagnostic TrySetLocation(ILocated location)
    {
        if (IsPopulated) return this;
        return new(Message, location, SubErrors);
    }

    public Diagnostic ToError(bool? shouldBreak = null) =>
        IsPopulated ?
        new DiagnosticAt(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(shouldBreak))) :
        new Diagnostic(DiagnosticsLevel.Error, Message, SubErrors.ToImmutableArray(v => v.ToError(shouldBreak)));

    public DiagnosticAt ToError(IPositioned position, Uri file, bool? shouldBreak = null) =>
        IsPopulated ?
        new(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(position, file, shouldBreak))) :
        new(DiagnosticsLevel.Error, Message, position.Position, file, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(position, file, shouldBreak)));

    public DiagnosticAt ToWarning(IPositioned position, Uri file) =>
        IsPopulated ?
        new(DiagnosticsLevel.Warning, Message, Position, File!, false, SubErrors.ToImmutableArray(v => v.ToWarning(position, file))) :
        new(DiagnosticsLevel.Warning, Message, position.Position, file, false, SubErrors.ToImmutableArray(v => v.ToWarning(position, file)));

    public DiagnosticAt ToError(ILocated location, bool? shouldBreak = null) =>
        IsPopulated ?
        new(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(location, shouldBreak))) :
        new(DiagnosticsLevel.Error, Message, location.Location.Position, location.Location.File, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(location, shouldBreak)));

    public DiagnosticAt ToWarning(ILocated location) =>
        IsPopulated ?
        new(DiagnosticsLevel.Warning, Message, Position, File!, false, SubErrors.ToImmutableArray(v => v.ToWarning(location))) :
        new(DiagnosticsLevel.Warning, Message, location.Location.Position, location.Location.File, false, SubErrors.ToImmutableArray(v => v.ToWarning(location)));

    public override string ToString() => Message;
}
