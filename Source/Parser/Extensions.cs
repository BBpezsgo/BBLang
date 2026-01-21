using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Parser;

public static class Extensions
{
    public static IEnumerable<Statement> EnumerateStatements(this ParserResult parserResult)
    {
        foreach (Statement v in parserResult.TopLevelStatements.IsDefault ? Enumerable.Empty<Statement>() : parserResult.TopLevelStatements.SelectMany(StatementWalker.Visit))
        { yield return v; }

        foreach (Statement statement in parserResult.Functions.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (Statement statement in parserResult.Operators.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (StructDefinition structs in parserResult.Structs.IsDefault ? Enumerable.Empty<StructDefinition>() : parserResult.Structs)
        {
            foreach (Statement statement in structs.GeneralFunctions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Constructors.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }
        }
    }

    public static bool EnumerateStatements(this CompilerResult parserResult, Func<CompiledStatement, bool> callback)
    {
        if (!parserResult.Statements.IsDefault)
        {
            if (!StatementWalker.Visit(parserResult.Statements, callback)) return false;
        }

        if (!parserResult.Functions.IsDefault)
        {
            foreach (CompiledFunction function in parserResult.Functions)
            {
                if (!StatementWalker.Visit(function.Body, callback)) return false;
            }
        }

        return true;
    }
}
