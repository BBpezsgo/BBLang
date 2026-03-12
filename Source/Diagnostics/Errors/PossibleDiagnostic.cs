namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class PossibleDiagnostic
{
    public string Message;
    public readonly ImmutableArray<PossibleDiagnostic> SubErrors;
    public readonly ImmutableArray<DiagnosticRelatedInformation> RelatedInformation;
    readonly Position Position;
    readonly Uri? File;
    readonly bool ShouldBreak;

    [MemberNotNullWhen(true, nameof(File))]
    bool IsPopulated => File is not null && Position != default;

    public PossibleDiagnostic(string message, bool shouldBreak = true)
        : this(message, ImmutableArray<PossibleDiagnostic>.Empty, ImmutableArray<DiagnosticRelatedInformation>.Empty, shouldBreak)
    { }

    public PossibleDiagnostic(string message, params PossibleDiagnostic[] suberrors)
        : this(message, suberrors.ToImmutableArray(), ImmutableArray<DiagnosticRelatedInformation>.Empty)
    { }

    public PossibleDiagnostic(string message, ImmutableArray<PossibleDiagnostic> suberrors, bool shouldBreak = true)
    {
        Message = message;
        SubErrors = suberrors;
        ShouldBreak = shouldBreak;
        RelatedInformation = ImmutableArray<DiagnosticRelatedInformation>.Empty;
    }

    public PossibleDiagnostic(string message, ImmutableArray<PossibleDiagnostic> suberrors, ImmutableArray<DiagnosticRelatedInformation> relatedInformation, bool shouldBreak = true)
    {
        Message = message;
        SubErrors = suberrors;
        ShouldBreak = shouldBreak;
        RelatedInformation = relatedInformation;
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

    public virtual PossibleDiagnostic WithRelatedInfo(DiagnosticRelatedInformation? relatedInfo) => relatedInfo is null ? this : new(Message, SubErrors, ImmutableArray.Create(relatedInfo), false);
    public virtual PossibleDiagnostic WithRelatedInfo(params DiagnosticRelatedInformation?[] relatedInfo) => WithRelatedInfo(relatedInfo.Where(v => v is not null).ToImmutableArray()!);
    public virtual PossibleDiagnostic WithRelatedInfo(IEnumerable<DiagnosticRelatedInformation?> relatedInfo) => WithRelatedInfo(relatedInfo.Where(v => v is not null).ToImmutableArray()!);
    public virtual PossibleDiagnostic WithRelatedInfo(ImmutableArray<DiagnosticRelatedInformation> relatedInfo) => relatedInfo.IsDefaultOrEmpty ? this : new(Message, SubErrors, RelatedInformation.AddRange(relatedInfo), false);

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
        new DiagnosticAt(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(shouldBreak)), RelatedInformation, DiagnosticTag.None) :
        new Diagnostic(DiagnosticsLevel.Error, Message, SubErrors.ToImmutableArray(v => v.ToError(shouldBreak)), RelatedInformation);

    public DiagnosticAt ToError(IPositioned position, Uri file, bool? shouldBreak = null) =>
        IsPopulated ?
        new(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(position, file, shouldBreak)), RelatedInformation, DiagnosticTag.None) :
        new(DiagnosticsLevel.Error, Message, position.Position, file, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(position, file, shouldBreak)), RelatedInformation, DiagnosticTag.None);

    public DiagnosticAt ToWarning(IPositioned position, Uri file) =>
        IsPopulated ?
        new(DiagnosticsLevel.Warning, Message, Position, File!, false, SubErrors.ToImmutableArray(v => v.ToWarning(position, file)), RelatedInformation, DiagnosticTag.None) :
        new(DiagnosticsLevel.Warning, Message, position.Position, file, false, SubErrors.ToImmutableArray(v => v.ToWarning(position, file)), RelatedInformation, DiagnosticTag.None);

    public DiagnosticAt ToError(ILocated location, bool? shouldBreak = null) =>
        IsPopulated ?
        new(DiagnosticsLevel.Error, Message, Position, File!, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(location, shouldBreak)), RelatedInformation, DiagnosticTag.None) :
        new(DiagnosticsLevel.Error, Message, location.Location.Position, location.Location.File, shouldBreak ?? ShouldBreak, SubErrors.ToImmutableArray(v => v.ToError(location, shouldBreak)), RelatedInformation, DiagnosticTag.None);

    public DiagnosticAt ToWarning(ILocated location) =>
        IsPopulated ?
        new(DiagnosticsLevel.Warning, Message, Position, File!, false, SubErrors.ToImmutableArray(v => v.ToWarning(location)), RelatedInformation, DiagnosticTag.None) :
        new(DiagnosticsLevel.Warning, Message, location.Location.Position, location.Location.File, false, SubErrors.ToImmutableArray(v => v.ToWarning(location)), RelatedInformation, DiagnosticTag.None);

    public override string ToString() => Message;
}
