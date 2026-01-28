namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class LanguageException : Exception
{
    public LanguageException(string message)
        : this(message, ImmutableArray<Exception>.Empty)
    { }

    public LanguageException(string message, params Exception[] suberrors)
        : this(message, suberrors.ToImmutableArray())
    { }

    public LanguageException(string message, ImmutableArray<Exception> suberrors)
        : base(message, suberrors.Length switch
        {
            0 => null,
            1 => suberrors[0],
            _ => new AggregateException(suberrors),
        })
    {

    }

    public override string ToString()
    {
        StringBuilder result = new(Message);

        if (InnerException != null)
        { result.Append($" {InnerException}"); }

        return result.ToString();
    }

    public Diagnostic ToDiagnostic() => new(
        DiagnosticsLevel.Error,
        Message,
        InnerException switch
        {
            LanguageExceptionAt v => ImmutableArray.Create<Diagnostic>(v.ToDiagnostic()),
            LanguageException v => ImmutableArray.Create<Diagnostic>(v.ToDiagnostic()),
            _ => ImmutableArray<Diagnostic>.Empty,
        }
    );
}
