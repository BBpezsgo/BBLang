
namespace LanguageCore.Runtime;

public sealed class CallbackIO : IO
{
    readonly Action<char> Out;
    readonly Func<char> In;

    public CallbackIO(Action<char> @out, Func<char> @in)
    {
        Out = @out;
        In = @in;
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, In));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, Out));
    }
}
