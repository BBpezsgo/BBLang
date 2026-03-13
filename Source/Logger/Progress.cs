namespace LanguageCore;

[ExcludeFromCodeCoverage]
public static class ConsoleProgressUtils
{
    public static void Report(this IProgress<float> progress, int index, int length) => progress.Report((float)index / (float)length);
}
