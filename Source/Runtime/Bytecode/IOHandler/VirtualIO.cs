namespace LanguageCore.Runtime;

public sealed class VirtualIO : IO
{
    public delegate void OnDataEventHandler(char data);
    public delegate void OnInputEventHandler();

    public event OnDataEventHandler? OnData;
    public event OnInputEventHandler? OnNeedInput;

    public bool IsAwaitingInput { get; private set; }
    readonly Queue<char> InputBuffer = new();

    public void SendKey(char key)
    {
        InputBuffer.Enqueue(key);
        IsAwaitingInput = false;
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(new ExternalFunctionAsync((ref ProcessorState processor, ReadOnlySpan<byte> parameters) =>
        {
            IsAwaitingInput = true;
            if (InputBuffer.Count == 0) OnNeedInput?.Invoke();
            return bool (ref ProcessorState processor, Span<byte> returnValue) =>
            {
                if (InputBuffer.TryDequeue(out char consumedKey))
                {
                    returnValue.Set(consumedKey);
                    IsAwaitingInput = false;
                    return true;
                }
                return false;
            };
        }, externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, 0, sizeof(char)));

        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, (char @char) =>
        {
            OnData?.Invoke(@char);
        }));
    }
}
