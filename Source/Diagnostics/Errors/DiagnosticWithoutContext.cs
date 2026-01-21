namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class DiagnosticWithoutContext :
    IEquatable<DiagnosticWithoutContext>,
    IDiagnostic
{
    public DiagnosticsLevel Level { get; }
    public string Message { get; }

    public ImmutableArray<IDiagnostic> SubErrors { get; }
    IEnumerable<IDiagnostic> IDiagnostic.SubErrors => SubErrors;

#if DEBUG
    bool _isDebugged;
#endif

    DiagnosticWithoutContext(DiagnosticsLevel level, string message, bool @break, ImmutableArray<IDiagnostic> suberrors)
    {
        Level = level;
        Message = message;
        SubErrors = suberrors;

        if (@break)
        { Break(); }
    }

    public DiagnosticWithoutContext(DiagnosticsLevel level, string message, ImmutableArray<IDiagnostic> suberrors)
        : this(level, message, level == DiagnosticsLevel.Error, suberrors) { }

    [DoesNotReturn]
    public void Throw() => throw new LanguageExceptionWithoutContext(Message);

    public static DiagnosticWithoutContext Internal(string message, bool @break = true)
        => new(DiagnosticsLevel.Error, message, @break, ImmutableArray<IDiagnostic>.Empty);

    public static DiagnosticWithoutContext Error(string message, bool @break = true)
        => new(DiagnosticsLevel.Error, message, @break, ImmutableArray<IDiagnostic>.Empty);

    public static DiagnosticWithoutContext Warning(string message, bool @break = true)
        => new(DiagnosticsLevel.Warning, message, @break, ImmutableArray<IDiagnostic>.Empty);

    public DiagnosticWithoutContext Break()
    {
#if DEBUG
        if (!_isDebugged)
        { Debugger.Break(); }
        _isDebugged = true;
#endif
        return this;
    }

    public override string ToString() => Message;

    public bool Equals([NotNullWhen(true)] DiagnosticWithoutContext? other)
    {
        if (other is null) return false;
        if (Message != other.Message) return false;
        return true;
    }
}
