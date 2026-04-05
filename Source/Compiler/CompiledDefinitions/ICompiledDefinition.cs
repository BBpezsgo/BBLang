namespace LanguageCore.Compiler;

public interface ICompiledDefinition<TDefinition>
{
    TDefinition Definition { get; }
}
