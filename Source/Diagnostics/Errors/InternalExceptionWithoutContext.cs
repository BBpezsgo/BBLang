namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class InternalExceptionWithoutContext : LanguageException
{
    public InternalExceptionWithoutContext() : base(string.Empty) { }
    public InternalExceptionWithoutContext(string message) : base(message) { }
}
