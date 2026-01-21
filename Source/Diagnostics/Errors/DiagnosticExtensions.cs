namespace LanguageCore;

public static class DiagnosticExtensions
{
    public static LanguageException ToException(this IDiagnostic diagnostic) => diagnostic is Diagnostic _diagnostic
        ? new LanguageException(_diagnostic.Message, _diagnostic.Position, _diagnostic.File, _diagnostic.SubErrors.ToImmutableArray(v => v.ToException()))
        : new LanguageException(diagnostic.Message, Position.UnknownPosition, null, diagnostic.SubErrors.ToImmutableArray(v => v.ToException()));
}
