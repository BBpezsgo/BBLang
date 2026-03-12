
namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class DiagnosticRelatedInformationAt : DiagnosticRelatedInformation
{
    public Location Location { get; }

    public DiagnosticRelatedInformationAt(string message, Location location) : base(message)
    {
        Location = location;
    }
}
