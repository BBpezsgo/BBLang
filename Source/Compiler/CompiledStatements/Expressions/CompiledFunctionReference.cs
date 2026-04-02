namespace LanguageCore.Compiler;

public class CompiledFunctionReference : CompiledExpression
{
    public required TemplateInstance<ICompiledFunctionDefinition> Function { get; init; }

    public override string Stringify(int depth = 0) => $"&{Function.Template switch
    {
        CompiledFunctionDefinition v => v.Identifier,
        _ => Function.ToString(),
    }}";
    public override string ToString() => $"&{Function.Template switch
    {
        CompiledFunctionDefinition v => v.Identifier,
        _ => Function.ToString(),
    }}";
}
