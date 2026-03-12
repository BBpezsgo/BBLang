
namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class DiagnosticRelatedInformation
{
    public string Message { get; }

    public DiagnosticRelatedInformation(string message)
    {
        Message = message;
    }
}
