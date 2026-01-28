namespace LanguageCore.Compiler;

public interface ICompiledFunctionDefinition :
    IHaveCompiledType,
    IInFile,
    IHaveInstructionOffset,
    IReadable,
    IMsilCompatible
{
    bool ReturnSomething { get; }
    ImmutableArray<CompiledParameter> Parameters { get; }
}
