namespace LanguageCore.Compiler;

public class CompiledDesctructorCall : CompiledExpression
{
    public required TemplateInstance<CompiledGeneralFunctionDefinition> Function { get; init; }
    public required CompiledExpression Value { get; init; }

    public override string Stringify(int depth = 0) => $"{Function.Template.Identifier}({Value.Stringify(depth + 1)})";
    public override string ToString() => $"{Function.Template.Identifier}({Value})";
}
