namespace LanguageCore.Tokenizing;

class TokenizerPreprocessorContext
{
    readonly DiagnosticsCollection Diagnostics;
    readonly Stack<PreprocessConditionItem> PreprocessConditionStack = new();
    readonly HashSet<string> PreprocessorVariables;
    readonly Uri? File;

    public bool IsPreprocessSkipping => PreprocessConditionStack.Any(v => !v.PreviousConditions[^1]);

    enum PreprocessConditionPhase
    {
        If,
        Else,
    }

    class PreprocessConditionItem
    {
        public PreprocessConditionPhase Phase;
        public List<bool> PreviousConditions;

        public PreprocessConditionItem(PreprocessConditionPhase phase)
        {
            Phase = phase;
            PreviousConditions = new List<bool>();
        }
    }

    public TokenizerPreprocessorContext(DiagnosticsCollection diagnostics, IEnumerable<string> variables, Uri? file)
    {
        Diagnostics = diagnostics;
        PreprocessorVariables = new HashSet<string>(variables);
        File = file;
    }

    public void HandlePreprocess(Token name, Token? argument)
    {
        switch (name.Content)
        {
            case "#if":
            {
                if (argument is null)
                { Diagnostics.Add(Diagnostic.Error($"Argument expected after preprocessor tag \"{name}\"", name.Position.After(), File)); }

                PreprocessConditionItem v = PreprocessConditionStack.Push(new PreprocessConditionItem(PreprocessConditionPhase.If));
                v.PreviousConditions.Add(PreprocessorVariables.Contains(argument?.Content ?? string.Empty));

                break;
            }

            case "#elseif":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(Diagnostic.Error($"Unexpected preprocessor tag \"{name}\"", name.Position, File));
                    break;
                }

                if (PreprocessConditionStack.Last.Phase == PreprocessConditionPhase.Else)
                { Diagnostics.Add(Diagnostic.Error($"Unexpected preprocessor tag \"{name}\"", name.Position, File)); }

                PreprocessConditionStack.Last.Phase = PreprocessConditionPhase.Else;
                PreprocessConditionStack.Last.PreviousConditions.Add(PreprocessConditionStack.Last.PreviousConditions.All(v => !v) && PreprocessorVariables.Contains(argument?.Content ?? string.Empty));

                break;
            }

            case "#else":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(Diagnostic.Error($"Unexpected preprocessor tag \"{name}\"", name.Position, File));
                    break;
                }

                if (PreprocessConditionStack.Last.Phase == PreprocessConditionPhase.Else)
                { Diagnostics.Add(Diagnostic.Error($"Unexpected preprocessor tag \"{name}\"", name.Position, File)); }

                PreprocessConditionStack.Last.Phase = PreprocessConditionPhase.Else;
                PreprocessConditionStack.Last.PreviousConditions.Add(PreprocessConditionStack.Last.PreviousConditions.All(v => !v));

                break;
            }

            case "#endif":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(Diagnostic.Error($"Unexpected preprocessor tag \"{name}\"", name.Position, File));
                    break;
                }

                PreprocessConditionStack.Pop();

                break;
            }

            case "#define":
            {
                if (IsPreprocessSkipping)
                { break; }

                if (argument is null)
                {
                    Diagnostics.Add(Diagnostic.Error($"Argument expected after preprocessor tag \"{name}\"", name.Position.After(), File));
                    break;
                }

                PreprocessorVariables.Add(argument.Content);

                break;
            }

            case "#undefine":
            {
                if (IsPreprocessSkipping)
                { break; }

                if (argument is null)
                {
                    Diagnostics.Add(Diagnostic.Error($"Argument expected after preprocessor tag \"{name}\"", name.Position.After(), File));
                    break;
                }

                PreprocessorVariables.Remove(argument.Content);

                break;
            }

            default:
            {
                Diagnostics.Add(Diagnostic.Error($"Unknown preprocessor tag \"{name}\"", name.Position.After(), File));
                break;
            }
        }
    }
}
