using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed class Parser
{
    int CurrentTokenIndex;
    readonly List<Token> Tokens;
    readonly ImmutableArray<Token> OriginalTokens;
    readonly Uri File;

    Location CurrentLocation => new(CurrentPosition, File);
    Position CurrentPosition => CurrentToken?.Position ?? PreviousToken?.Position.After() ?? Position.UnknownPosition;
    Token? CurrentToken => (CurrentTokenIndex >= 0 && CurrentTokenIndex < Tokens.Count) ? Tokens[CurrentTokenIndex] : null;
    Token? PreviousToken => (CurrentTokenIndex >= 1 && CurrentTokenIndex <= Tokens.Count) ? Tokens[CurrentTokenIndex - 1] : null;

    static readonly ImmutableArray<string> AllModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ProtectionKeywords.Private,

        ModifierKeywords.Inline,
        ModifierKeywords.Const,
        ModifierKeywords.Ref,
        ModifierKeywords.Temp,
        ModifierKeywords.This
    );

    static readonly ImmutableArray<string> FunctionModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ModifierKeywords.Inline
    );

    static readonly ImmutableArray<string> AliasModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> FieldModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Private
    );

    static readonly ImmutableArray<string> GeneralStatementModifiers = ImmutableArray.Create
    (
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> VariableModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ModifierKeywords.Temp,
        ModifierKeywords.Const
    );

    static readonly ImmutableArray<string> ParameterModifiers = ImmutableArray.Create
    (
        ModifierKeywords.This,
        ModifierKeywords.Ref,
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> ArgumentModifiers = ImmutableArray.Create
    (
        ModifierKeywords.Ref,
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> StructModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> ConstructorModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> GeneralFunctionModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> OverloadableOperators = ImmutableArray.Create
    (
        "<<", ">>",
        "+", "-", "*", "/", "%",
        "&", "|", "^",
        "<", ">", ">=", "<=", "!=", "==",
        "&&", "||"
    );

    static readonly ImmutableArray<string> CompoundAssignmentOperators = ImmutableArray.Create
    (
        "+=", "-=", "*=", "/=", "%=",
        "&=", "|=", "^="
    );

    static readonly ImmutableArray<string> BinaryOperators = ImmutableArray.Create
    (
        "<<", ">>",
        "+", "-", "*", "/", "%",
        "&", "|", "^",
        "<", ">", ">=", "<=", "!=", "==", "&&", "||"
    );

    static readonly ImmutableArray<string> UnaryPrefixOperators = ImmutableArray.Create
    (
        "!", "~",
        "-", "+"
    );

    static readonly ImmutableArray<string> IncrementDecrementOperators = ImmutableArray.Create
    (
        "++", "--"
    );

#pragma warning disable RCS1213, IDE0052, CA1823 // Remove unread private members
    static readonly ImmutableArray<string> UnaryPostfixOperators = ImmutableArray<string>.Empty;
#pragma warning restore RCS1213, IDE0052, CA1823

    readonly bool IsExpression;
    readonly DiagnosticsCollection Diagnostics;
    readonly List<FunctionDefinition> Functions = new();
    readonly List<FunctionDefinition> Operators = new();
    readonly Dictionary<string, StructDefinition> Structs = new();
    readonly List<UsingDefinition> Usings = new();
    readonly List<AliasDefinition> AliasDefinitions = new();
    readonly List<Statement> TopLevelStatements = new();

    Parser(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics, bool isExpression)
    {
        OriginalTokens = tokens;
        Tokens = tokens
            .Where(v => v.TokenType
                is not TokenType.Comment
                and not TokenType.CommentMultiline
                and not TokenType.Whitespace
                and not TokenType.LineBreak
                and not TokenType.PreprocessArgument
                and not TokenType.PreprocessIdentifier
                and not TokenType.PreprocessSkipped)
            .ToList();
        File = file;
        IsExpression = isExpression;
        Diagnostics = diagnostics;
    }

    readonly struct ParseRestorePoint
    {
        readonly Parser Parser;
        readonly int TokenIndex;

        public ParseRestorePoint(Parser parser, int tokenIndex)
        {
            Parser = parser;
            TokenIndex = tokenIndex;
        }

        public void Restore()
        {
            Parser.CurrentTokenIndex = TokenIndex;
        }
    }

    ParseRestorePoint SavePoint() => new(this, CurrentTokenIndex);

    public static ParserResult Parse(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics)
        => new Parser(tokens, file, diagnostics, false).ParseInternal();

    public static ParserResult ParseExpression(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics)
        => new Parser(tokens, file, diagnostics, true).ParseInternal();

#if UNITY
    static readonly Unity.Profiling.ProfilerMarker _marker = new("LanguageCore.Parser");
#endif
    ParserResult ParseInternal()
    {
#if UNITY
        using Unity.Profiling.ProfilerMarker.AutoScope _1 = _marker.Auto();
#endif
        CurrentTokenIndex = 0;

        try
        {
            ParseCodeHeader();

            SkipCrapTokens();

            EndlessCheck endlessSafe = new();
            while (ParseCodeBlock())
            {
                endlessSafe.Step();
            }

            SkipCrapTokens();

            if (CurrentToken != null)
            {
                Diagnostics.Add(Diagnostic.Error($"Unexpected token `{CurrentToken}`", CurrentToken, File));
            }
        }
        catch (SyntaxException syntaxException)
        {
            Diagnostics.Add(syntaxException.ToDiagnostic());
        }

        return new ParserResult(
            Functions.ToImmutableArray(),
            Operators.ToImmutableArray(),
            Structs.Values.ToImmutableArray(),
            Usings.ToImmutableArray(),
            AliasDefinitions.ToImmutableArray(),
            TopLevelStatements.ToImmutableArray(),
            OriginalTokens,
            Tokens.ToImmutableArray()
        );
    }

    #region Parse top level

    bool ExpectUsing([NotNullWhen(true)] out UsingDefinition? usingDefinition)
    {
        usingDefinition = null;
        if (!ExpectIdentifier(DeclarationKeywords.Using, out Token? keyword))
        { return false; }

        SkipCrapTokens();

        if (CurrentToken == null) throw new SyntaxException($"Expected url after keyword \"{DeclarationKeywords.Using}\"", keyword.Position.After(), File);

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        List<Token> tokens = new();
        if (CurrentToken.TokenType == TokenType.LiteralString)
        {
            tokens.Add(CurrentToken);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;
            CurrentTokenIndex++;
        }
        else
        {
            EndlessCheck endlessSafe = new();
            while (ExpectIdentifier(out Token? pathIdentifier))
            {
                tokens.Add(pathIdentifier);

                if (!ExpectOperator(".")) break;

                endlessSafe.Step();
            }
        }

        if (tokens.Count == 0)
        {
            if (!ExpectOperator(";"))
            {
                throw new SyntaxException($"Expected library name after \"{DeclarationKeywords.Using}\"", keyword, File);
            }
            else
            {
                Diagnostics.Add(Diagnostic.Error($"Expected library name after \"{DeclarationKeywords.Using}\"", keyword, File));
            }
            return false;
        }

        usingDefinition = new UsingDefinition(keyword, tokens.ToImmutableArray(), File);

        if (!ExpectOperator(";"))
        { Diagnostics.Add(Diagnostic.Warning($"You forgot the semicolon (after \"{DeclarationKeywords.Using}\")", usingDefinition.Position.After(), File)); }

        return true;
    }

    void ParseCodeHeader()
    {
        while (true)
        {
            if (!ExpectUsing(out UsingDefinition? usingDefinition)) break;

            Usings.Add(usingDefinition);
        }
    }

    bool ParseCodeBlock()
    {
        OrderedDiagnosticCollection diagnostics = new();
        if (ExpectStructDefinition(diagnostics)) { }
        else if (ExpectFunctionDefinition(out FunctionDefinition? functionDefinition, diagnostics))
        { Functions.Add(functionDefinition); }
        else if (ExpectOperatorDefinition(out FunctionDefinition? operatorDefinition, diagnostics))
        { Operators.Add(operatorDefinition); }
        else if (ExpectAliasDefinition(out AliasDefinition? aliasDefinition, diagnostics))
        { AliasDefinitions.Add(aliasDefinition); }
        else if (ExpectStatement(out Statement? statement))
        { TopLevelStatements.Add(statement); }
        else
        {
            Diagnostics.Add(Diagnostic.Error($"Expected something but not \"{CurrentToken}\"", CurrentToken, File).WithSuberrors(diagnostics.Compile()));
            return false;
        }

        return true;
    }

    bool ExpectOperatorDefinition([NotNullWhen(true)] out FunctionDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? possibleType, out _))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected type for operator definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(OverloadableOperators, out Token? possibleName))
        {
            if (OverloadableOperators.Contains("*") &&
                possibleType is TypeInstancePointer _possibleTypePointer)
            {
                possibleType = _possibleTypePointer.To;
                possibleName = _possibleTypePointer.Operator;
            }
            else
            {
                int callOperatorParseStart = CurrentTokenIndex;
                if (ExpectOperator("(", out Token? opening) && ExpectOperator(")", out Token? closing) && CurrentToken?.Content == "(")
                {
                    possibleName = opening + closing;
                }
                else
                {
                    CurrentTokenIndex = callOperatorParseStart;

                    diagnostic.Add(0, Diagnostic.Critical($"Expected an operator for operator definition", CurrentLocation, false));
                    savepoint.Restore();
                    return false;
                }
            }
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), false, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected parameter list for operator", CurrentLocation, false), parameterDiagnostics);
            savepoint.Restore();
            return false;
        }

        possibleName.AnalyzedType = TokenAnalyzedType.FunctionName;

        CheckModifiers(modifiers, FunctionModifiers);

        Block? block = null;

        if (!ExpectOperator(";") && !ExpectBlock(out block))
        {
            diagnostic.Add(1, Diagnostic.Critical($"Expected \";\" or block", parameters.Brackets.End.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        function = new FunctionDefinition(
            attributes,
            modifiers,
            possibleType,
            possibleName,
            parameters,
            null,
            File)
        {
            Block = block
        };
        return true;
    }

    bool ExpectAliasDefinition([NotNullWhen(true)] out AliasDefinition? aliasDefinition, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        aliasDefinition = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(DeclarationKeywords.Alias, out Token? keyword))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected keyword `{DeclarationKeywords.Alias}` for alias definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectIdentifier(out Token? identifier))
        {
            diagnostic.Add(1, Diagnostic.Critical($"Expected identifier after keyword \"{keyword}\"", keyword.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        identifier.AnalyzedType = TokenAnalyzedType.TypeAlias;

        if (!ExpectType(AllowedType.Any | AllowedType.FunctionPointer | AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        {
            diagnostic.Add(2, Diagnostic.Critical($"Expected type after alias identifier", identifier.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        CheckModifiers(modifiers, AliasModifiers);

        aliasDefinition = new AliasDefinition(
            attributes,
            modifiers,
            keyword,
            identifier,
            type,
            File
        );

        if (!ExpectOperator(";"))
        {
            diagnostic.Add(3, Diagnostic.Warning($"You forgot the semicolon", aliasDefinition.Position.After(), File));
            //savepoint.Restore();
            //return false;
        }

        return true;
    }

    bool ExpectTemplateInfo([NotNullWhen(true)] out TemplateInfo? templateInfo)
    {
        if (!ExpectOperator("<", out Token? startBracket))
        {
            templateInfo = null;
            return false;
        }

        List<Token> parameters = new();

        Token? endBracket;

        bool expectParameter = false;
        while (!ExpectOperator(">", out endBracket) || expectParameter)
        {
            if (!ExpectIdentifier(out Token? parameter))
            { throw new SyntaxException("Expected identifier or \">\"", PreviousToken!.Position.After(), File); }

            parameter.AnalyzedType = TokenAnalyzedType.TypeParameter;

            parameters.Add(parameter);

            if (ExpectOperator(">", out endBracket))
            { break; }

            if (!ExpectOperator(","))
            { throw new SyntaxException("Expected \",\" or \">\"", parameter.Position.After(), File); }
            else
            { expectParameter = true; }
        }

        templateInfo = new(new TokenPair(startBracket, endBracket), parameters.ToImmutableArray());

        return true;
    }

    bool ExpectFunctionDefinition([NotNullWhen(true)] out FunctionDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? possibleType, out _))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected type for function definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? possibleNameT))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected identifier for function definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        ExpectTemplateInfo(out TemplateInfo? templateInfo);

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ParameterModifiers, true, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected parameter list for function definition", CurrentLocation, false), parameterDiagnostics);
            savepoint.Restore();
            return false;
        }

        possibleNameT.AnalyzedType = TokenAnalyzedType.FunctionName;

        CheckModifiers(modifiers, FunctionModifiers);

        Block? block = null;

        if (!ExpectOperator(";") && !ExpectBlock(out block))
        {
            diagnostic.Add(1, Diagnostic.Critical($"Expected \";\" or block", parameters.Brackets.End.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        function = new FunctionDefinition(
            attributes,
            modifiers,
            possibleType,
            possibleNameT,
            parameters,
            templateInfo,
            File)
        {
            Block = block
        };
        return true;
    }

    bool ExpectGeneralFunctionDefinition([NotNullWhen(true)] out GeneralFunctionDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(out Token? possibleNameT))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected identifier for general function definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (possibleNameT.Content is
            not BuiltinFunctionIdentifiers.IndexerGet and
            not BuiltinFunctionIdentifiers.IndexerSet and
            not BuiltinFunctionIdentifiers.Destructor)
        {
            diagnostic.Add(0, Diagnostic.Critical($"Invalid identifier `{possibleNameT.Content}` for general function definition", possibleNameT, File, false));
            savepoint.Restore();
            return false;
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), false, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(1, Diagnostic.Critical($"Expected parameter list for general function definition", CurrentLocation), parameterDiagnostics);
            savepoint.Restore();
            return false;
        }

        possibleNameT.AnalyzedType = TokenAnalyzedType.FunctionName;

        CheckModifiers(modifiers, GeneralFunctionModifiers);

        if (!ExpectBlock(out Block? block))
        {
            diagnostic.Add(2, Diagnostic.Error($"Body is required for general function definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        function = new GeneralFunctionDefinition(
            possibleNameT,
            modifiers,
            parameters,
            File)
        {
            Block = block
        };
        return true;
    }

    bool ExpectConstructorDefinition([NotNullWhen(true)] out ConstructorDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? type))
        {
            diagnostic.Add(0, Diagnostic.Error($"Expected a type for constructor definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), true, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(0, Diagnostic.Error($"Expected a parameter list for constructor definition", CurrentPosition, File, false), parameterDiagnostics);
            savepoint.Restore();
            return false;
        }

        CheckModifiers(modifiers, ConstructorModifiers);

        if (!ExpectBlock(out Block? block))
        {
            diagnostic.Add(0, Diagnostic.Error($"Body is required for constructor definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        function = new ConstructorDefinition(
            type,
            modifiers,
            parameters,
            File)
        {
            Block = block
        };

        return true;
    }

    bool ExpectStructDefinition(OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(DeclarationKeywords.Struct, out Token? keyword))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected keyword `{DeclarationKeywords.Struct}` for struct definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? possibleStructName))
        {
            diagnostic.Add(1, Diagnostic.Critical($"Expected struct identifier after keyword `{keyword}`", keyword.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        ExpectTemplateInfo(out TemplateInfo? templateInfo);

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            diagnostic.Add(2, Diagnostic.Critical($"Expected `{{` after struct identifier `{keyword}`", possibleStructName.Position.After(), File));
            savepoint.Restore();
            return false;
        }

        possibleStructName.AnalyzedType = TokenAnalyzedType.Struct;
        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        List<FieldDefinition> fields = new();
        List<FunctionDefinition> methods = new();
        List<FunctionDefinition> operators = new();
        List<GeneralFunctionDefinition> generalMethods = new();
        List<ConstructorDefinition> constructors = new();

        Token? bracketEnd;
        EndlessCheck endlessSafe = new();
        while (!ExpectOperator("}", out bracketEnd))
        {
            OrderedDiagnosticCollection diagnostics = new();
            if (ExpectField(out FieldDefinition? field, diagnostics))
            {
                fields.Add(field);
                if (ExpectOperator(";", out Token? semicolon))
                { field.Semicolon = semicolon; }
            }
            else if (ExpectFunctionDefinition(out FunctionDefinition? methodDefinition, diagnostics))
            {
                methods.Add(methodDefinition);
            }
            else if (ExpectGeneralFunctionDefinition(out GeneralFunctionDefinition? generalMethodDefinition, diagnostics))
            {
                generalMethods.Add(generalMethodDefinition);
            }
            else if (ExpectConstructorDefinition(out ConstructorDefinition? constructorDefinition, diagnostics))
            {
                constructors.Add(constructorDefinition);
            }
            else if (ExpectOperatorDefinition(out FunctionDefinition? operatorDefinition, diagnostics))
            {
                operators.Add(operatorDefinition);
            }
            else
            {
                Diagnostics.Add(Diagnostic.Critical("Expected field definition or \"}\"", CurrentToken?.Position ?? PreviousToken!.Position.After(), File).WithSuberrors(diagnostics.Compile()));
                return false;
            }

            endlessSafe.Step();
        }

        CheckModifiers(modifiers, StructModifiers);

        StructDefinition structDefinition = new(
            possibleStructName,
            bracketStart,
            bracketEnd,
            attributes,
            modifiers,
            fields.ToImmutableArray(),
            methods.ToImmutableArray(),
            generalMethods.ToImmutableArray(),
            operators.ToImmutableArray(),
            constructors.ToImmutableArray(),
            File)
        {
            Template = templateInfo,
        };

        Structs.Add(structDefinition.Identifier.Content, structDefinition);

        return true;
    }

    bool ExpectParameters(ImmutableArray<string> allowedParameterModifiers, bool allowDefaultValues, [NotNullWhen(true)] out ParameterDefinitionCollection? parameterDefinitions, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        parameterDefinitions = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            diagnostic.Add(0, Diagnostic.Error("Expected a `(` for parameter list", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        List<ParameterDefinition> parameters = new();

        bool expectParameter = false;
        bool expectOptionalParameters = false;
        Token? bracketEnd;
        while (!ExpectOperator(")", out bracketEnd) || expectParameter)
        {
            ImmutableArray<Token> parameterModifiers = ExpectModifiers();
            CheckParameterModifiers(parameterModifiers, parameters.Count, allowedParameterModifiers);

            if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? parameterType))
            {
                diagnostic.Add(1, Diagnostic.Error("Expected parameter type", CurrentLocation, false));
                savepoint.Restore();
                return false;
            }

            if (!ExpectIdentifier(out Token? parameterIdentifier))
            {
                diagnostic.Add(1, Diagnostic.Error("Expected a parameter name", CurrentLocation, false));
                savepoint.Restore();
                return false;
            }

            parameterIdentifier.AnalyzedType = TokenAnalyzedType.ParameterName;

            Expression? defaultValue = null;
            if (ExpectOperator("=", out Token? assignmentOperator))
            {
                if (!allowDefaultValues)
                {
                    diagnostic.Add(2, Diagnostic.Error("Default parameter values are not valid in the current context", assignmentOperator, File, false));
                    savepoint.Restore();
                    return false;
                }
                if (!ExpectExpression(out defaultValue))
                {
                    diagnostic.Add(2, Diagnostic.Error("Expected expression after \"=\" in parameter definition", assignmentOperator, File, false));
                    savepoint.Restore();
                    return false;
                }
                expectOptionalParameters = true;
            }
            else if (expectOptionalParameters)
            {
                diagnostic.Add(2, Diagnostic.Error("Parameters without default value after a parameter that has one is not supported", parameterIdentifier.Position.After(), File));
                savepoint.Restore();
                return false;
            }

            parameters.Add(new ParameterDefinition(parameterModifiers, parameterType, parameterIdentifier, defaultValue));

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(","))
            {
                diagnostic.Add(2, Diagnostic.Error("Expected \",\" or \")\"", PreviousToken!.Position.After(), File, false));
                savepoint.Restore();
                return false;
            }
            else
            { expectParameter = true; }
        }

        parameterDefinitions = new ParameterDefinitionCollection(parameters.ToImmutableArray(), new TokenPair(bracketStart, bracketEnd));
        return true;
    }

    #endregion

    #region Parse low level

    bool ExpectLambda([NotNullWhen(true)] out LambdaExpression? lambdaStatement)
    {
        ParseRestorePoint savepoint = SavePoint();
        lambdaStatement = null;

        OrderedDiagnosticCollection parametersDiagnostics = new();
        if (!ExpectParameters(ParameterModifiers, false, out ParameterDefinitionCollection? parameters, parametersDiagnostics))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("=>", out Token? arrow))
        {
            savepoint.Restore();
            return false;
        }

        Statement body;

        if (ExpectBlock(out Block? block, false))
        {
            body = block;
        }
        else if (ExpectExpression(out Expression? expression))
        {
            body = expression;
        }
        else
        {
            savepoint.Restore();
            return false;
        }

        arrow.AnalyzedType = TokenAnalyzedType.OtherOperator;

        lambdaStatement = new LambdaExpression(
            parameters,
            arrow,
            body,
            File
        );
        return true;
    }

    bool ExpectListValue([NotNullWhen(true)] out ListExpression? listValue)
    {
        ParseRestorePoint savepoint = SavePoint();
        listValue = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        ImmutableArray<Expression>.Builder? values = null;

        Token? bracketEnd;
        EndlessCheck endlessSafe = new();
        Position lastPosition = bracketStart.Position;
        while (true)
        {
            if (ExpectExpression(out Expression? v))
            {
                values ??= ImmutableArray.CreateBuilder<Expression>();
                values.Add(v);
                lastPosition = v.Position;

                if (!ExpectOperator(","))
                {
                    if (!ExpectOperator("]", out bracketEnd))
                    { throw new SyntaxException("Unbalanced \"[\"", bracketStart, File); }
                    break;
                }
            }
            else
            {
                if (!ExpectOperator("]", out bracketEnd))
                { throw new SyntaxException("Unbalanced \"[\"", bracketStart, File); }
                break;
            }

            endlessSafe.Step();
        }

        listValue = new ListExpression(values?.DrainToImmutable() ?? ImmutableArray<Expression>.Empty, new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectLiteral([NotNullWhen(true)] out LiteralExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();

        SkipCrapTokens();

        string v = CurrentToken?.Content ?? string.Empty;

        if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralFloat)
        {
            v = v.Replace("_", string.Empty, StringComparison.Ordinal);

            LiteralExpression literal = new(LiteralType.Float, v, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralNumber)
        {
            v = v.Replace("_", string.Empty, StringComparison.Ordinal);

            LiteralExpression literal = new(LiteralType.Integer, v, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralHex)
        {
            if (v.Length < 3)
            {
                Diagnostics.Add(Diagnostic.Error($"Invalid hex literal \"{CurrentToken}\"", CurrentToken, File));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            if (!int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            {
                Diagnostics.Add(Diagnostic.Error($"Invalid hex number \"{v}\"", CurrentToken.Position[2..], File));
                value = 0;
            }

            LiteralExpression literal = new(LiteralType.Integer, value.ToString(CultureInfo.InvariantCulture), CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralBinary)
        {
            if (v.Length < 3)
            {
                Diagnostics.Add(Diagnostic.Error($"Invalid binary literal \"{CurrentToken}\"", CurrentToken, File));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            // if (!int.TryParse(v, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out int value))
            // {
            //     Diagnostics.Add(Diagnostic.Error($"Invalid binary number \"{v}\"", CurrentToken.Position[2..], File));
            //     value = 0;
            // }
            int value = Convert.ToInt32(v, 2);

            LiteralExpression literal = new(LiteralType.Integer, value.ToString(CultureInfo.InvariantCulture), CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralString)
        {
            LiteralExpression literal = new(LiteralType.String, v, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralCharacter)
        {
            LiteralExpression literal = new(LiteralType.Char, v, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }

        savepoint.Restore();

        statement = null;
        return false;
    }

    bool ExpectIndex(Expression prevStatement, [NotNullWhen(true)] out IndexCallExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectExpression(out Expression? expression))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("]", out Token? bracketEnd))
        { throw new SyntaxException("Unbalanced [", bracketStart, File); }

        // fixme
        statement = new IndexCallExpression(prevStatement, ArgumentExpression.Wrap(expression), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectExpressionInBrackets([NotNullWhen(true)] out Expression? expressionInBrackets)
    {
        ParseRestorePoint savepoint = SavePoint();
        expressionInBrackets = null;

        if (!ExpectOperator("(", out Token? bracketStart1))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectExpression(out Expression? expression))
        { throw new SyntaxException("Expected expression after \"(\"", bracketStart1.Position.After(), File); }

        if (!ExpectOperator(")", out Token? bracketEnd1))
        { throw new SyntaxException("Unbalanced \"(\"", bracketStart1, File); }

        expression.SurroundingBrackets = new TokenPair(bracketStart1, bracketEnd1);

        expressionInBrackets = expression;
        return true;
    }

    bool ExpectNewExpression([NotNullWhen(true)] out Expression? newExpression)
    {
        ParseRestorePoint savepoint = SavePoint();
        newExpression = null;

        if (!ExpectIdentifier(StatementKeywords.New, out Token? keywordNew))
        {
            savepoint.Restore();
            return false;
        }

        keywordNew.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectType(AllowedType.None, out TypeInstance? instanceTypeName))
        { throw new SyntaxException($"Expected instance constructor after keyword \"{StatementKeywords.New}\"", keywordNew, File); }

        if (ExpectArguments(out ArgumentListExpression? argumentList))
        {
            newExpression = new ConstructorCallExpression(keywordNew, instanceTypeName, argumentList, File);
            return true;
        }
        else
        {
            newExpression = new NewInstanceExpression(keywordNew, instanceTypeName, File);
            return true;
        }
    }

    bool ExpectFieldAccessor(Expression prevStatement, [NotNullWhen(true)] out FieldExpression? fieldAccessor)
    {
        ParseRestorePoint savepoint = SavePoint();
        fieldAccessor = null;

        if (!ExpectOperator(".", out Token? tokenDot))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? fieldName))
        {
            fieldAccessor = new FieldExpression(
                prevStatement,
                new MissingIdentifier(Token.CreateAnonymous(string.Empty, TokenType.Identifier, tokenDot.Position.After()), File),
                File
            );
            Diagnostics.Add(Diagnostic.Critical("Expected a symbol after `.`", tokenDot.Position.After(), File));
            return true;
        }

        fieldAccessor = new FieldExpression(
            prevStatement,
            new(fieldName, File),
            File
        );
        return true;
    }

    bool ExpectAsStatement(Expression prevStatement, [NotNullWhen(true)] out ReinterpretExpression? basicTypeCast)
    {
        ParseRestorePoint savepoint = SavePoint();
        basicTypeCast = null;

        if (!ExpectIdentifier(StatementKeywords.As, out Token? keyword))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectType(AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        { throw new SyntaxException($"Expected type after keyword \"{keyword}\"", keyword.Position.After(), File); }

        basicTypeCast = new ReinterpretExpression(prevStatement, keyword, type, File);
        return true;
    }

    bool ExpectOneValue([NotNullWhen(true)] out Expression? statementWithValue, bool allowAsStatement = true)
    {
        statementWithValue = null;

        if (ExpectLambda(out LambdaExpression? lambdaStatement))
        {
            statementWithValue = lambdaStatement;
        }
        else if (ExpectListValue(out ListExpression? listValue))
        {
            statementWithValue = listValue;
        }
        else if (ExpectLiteral(out LiteralExpression? literal))
        {
            statementWithValue = literal;
        }
        else if (ExpectTypeCast(out ManagedTypeCastExpression? typeCast))
        {
            statementWithValue = typeCast;
        }
        else if (ExpectExpressionInBrackets(out Expression? expressionInBrackets))
        {
            statementWithValue = expressionInBrackets;
        }
        else if (ExpectNewExpression(out Expression? newExpression))
        {
            statementWithValue = newExpression;
        }
        else if (ExpectVariableAddressGetter(out GetReferenceExpression? memoryAddressGetter))
        {
            statementWithValue = memoryAddressGetter;
        }
        else if (ExpectVariableAddressFinder(out DereferenceExpression? pointer))
        {
            statementWithValue = pointer;
        }
        else if (ExpectIdentifier(out Token? simpleIdentifier))
        {
            IdentifierExpression identifierStatement = new(simpleIdentifier, File);

            if (simpleIdentifier.Content == StatementKeywords.This)
            { simpleIdentifier.AnalyzedType = TokenAnalyzedType.Keyword; }

            statementWithValue = identifierStatement;
        }

        if (statementWithValue == null)
        { return false; }

        while (true)
        {
            if (ExpectFieldAccessor(statementWithValue, out FieldExpression? fieldAccessor))
            {
                statementWithValue = fieldAccessor;
            }
            else if (ExpectIndex(statementWithValue, out IndexCallExpression? statementIndex))
            {
                statementWithValue = statementIndex;
            }
            else if (ExpectAnyCall(statementWithValue, out AnyCallExpression? anyCall))
            {
                statementWithValue = anyCall;
            }
            else
            {
                break;
            }
        }

        if (allowAsStatement && ExpectAsStatement(statementWithValue, out ReinterpretExpression? basicTypeCast))
        {
            statementWithValue = basicTypeCast;
        }

        return statementWithValue != null;
    }

    bool ExpectTypeCast([NotNullWhen(true)] out ManagedTypeCastExpression? typeCast)
    {
        ParseRestorePoint savepoint = SavePoint();
        typeCast = default;

        if (!ExpectOperator("(", out Token? leftBracket))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectType(AllowedType.Any | AllowedType.FunctionPointer | AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(")", out Token? rightBracket))
        // { throw new SyntaxException($"Expected ')' after type of the type cast", type.Position.After(), File); }
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? value, false))
        // { throw new SyntaxException($"Expected one value for the type cast", rightTypeBracket.Position.After(), File); }
        {
            savepoint.Restore();
            return false;
        }

        typeCast = new ManagedTypeCastExpression(value, type, new TokenPair(leftBracket, rightBracket), File);
        return true;
    }

    bool ExpectVariableAddressGetter([NotNullWhen(true)] out GetReferenceExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("&", out Token? refToken))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? prevStatement, false))
        {
            savepoint.Restore();
            return false;
        }

        refToken.AnalyzedType = TokenAnalyzedType.OtherOperator;

        statement = new GetReferenceExpression(refToken, prevStatement, File);
        return true;
    }

    bool ExpectVariableAddressFinder([NotNullWhen(true)] out DereferenceExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("*", out Token? refToken))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? prevStatement, false))
        {
            savepoint.Restore();
            return false;
        }

        refToken.AnalyzedType = TokenAnalyzedType.OtherOperator;

        statement = new DereferenceExpression(refToken, prevStatement, File);
        return true;
    }

    void SetStatementThings(Statement statement)
    {
        if (statement == null)
        {
            if (CurrentToken != null)
            { throw new SyntaxException($"Unknown statement null", CurrentToken, File); }
            else
            { throw new SyntaxException($"Unknown statement null", Position.UnknownPosition, File); }
        }

        if (statement is LiteralExpression)
        { throw new SyntaxException($"Unexpected kind of statement \"{statement.GetType().Name}\"", statement, File); }

        if (statement is IdentifierExpression)
        { throw new SyntaxException($"Unexpected kind of statement \"{statement.GetType().Name}\"", statement, File); }

        if (statement is NewInstanceExpression)
        { throw new SyntaxException($"Unexpected kind of statement \"{statement.GetType().Name}\"", statement, File); }

        if (statement is Expression statementWithReturnValue)
        { statementWithReturnValue.SaveValue = false; }
    }

    bool ExpectBlock([NotNullWhen(true)] out Block? block, bool consumeSemicolon = true)
    {
        ParseRestorePoint savepoint = SavePoint();
        block = null;

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        ImmutableArray<Statement>.Builder statements = ImmutableArray.CreateBuilder<Statement>();

        EndlessCheck endlessSafe = new();
        Token? bracketEnd;
        while (!ExpectOperator("}", out bracketEnd))
        {
            if (!ExpectStatement(out Statement? statement))
            {
                SkipCrapTokens();
                throw new SyntaxException($"Expected a statement", CurrentToken?.Position ?? bracketStart.Position.After(), File);
            }

            statements.Add(statement);

            endlessSafe.Step();
        }

        block = new Block(statements.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);

        if (consumeSemicolon && ExpectOperator(";", out Token? semicolon))
        {
            block.Semicolon = semicolon;
            Diagnostics.Add(Diagnostic.Warning("Unnecessary semicolon", semicolon, File));
        }

        return true;
    }

    bool ExpectVariableDeclaration([NotNullWhen(true)] out VariableDefinition? variableDeclaration)
    {
        ParseRestorePoint savepoint = SavePoint();
        variableDeclaration = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers(VariableModifiers);

        TypeInstance? possibleType;
        if (ExpectIdentifier(StatementKeywords.Var, out Token? implicitTypeKeyword))
        {
            implicitTypeKeyword.AnalyzedType = TokenAnalyzedType.Keyword;
            possibleType = new TypeInstanceSimple(implicitTypeKeyword, File);
        }
        else if (!ExpectType(AllowedType.StackArrayWithoutLength | AllowedType.FunctionPointer, out possibleType))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? possibleVariableName))
        {
            savepoint.Restore();
            return false;
        }

        possibleVariableName.AnalyzedType = TokenAnalyzedType.VariableName;

        Expression? initialValue = null;

        if (ExpectOperator("=", out Token? eqOperatorToken))
        {
            if (!ExpectExpression(out initialValue))
            { throw new SyntaxException("Expected initial value after \"=\" in variable declaration", eqOperatorToken, File); }
        }
        else
        {
            if (possibleType == StatementKeywords.Var)
            { throw new SyntaxException("Initial value for variable declaration with implicit type is required", possibleType, File); }
        }

        variableDeclaration = new VariableDefinition(
            attributes,
            modifiers,
            possibleType,
            new(possibleVariableName, File),
            initialValue,
            File);
        return true;
    }

    bool ExpectForStatement([NotNullWhen(true)] out ForLoopStatement? forLoop)
    {
        ParseRestorePoint savepoint = SavePoint();
        forLoop = null;

        if (!ExpectIdentifier(StatementKeywords.For, out Token? keyword))
        {
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        if (!ExpectOperator("(", out Token? bracketStart))
        { throw new SyntaxException($"Expected \"(\" after \"{keyword}\" keyword", keyword.Position.After(), File); }

        Statement? initialization;
        Expression? condition;
        Statement? step;

        if (ExpectOperator(";", out Token? semicolon1))
        {
            initialization = null;
        }
        else
        {
            if (!ExpectStatementUnchecked(out initialization))
            { throw new SyntaxException("Expected a statement", bracketStart.Position.After(), File); }

            SetStatementThings(initialization);

            if (!ExpectOperator(";", out semicolon1))
            { throw new SyntaxException($"Expected \";\" after for-loop initialization", initialization.Position.After(), File); }
            initialization.Semicolon = semicolon1;
        }

        if (ExpectOperator(";", out Token? semicolon2))
        {
            condition = null;
        }
        else
        {
            if (!ExpectExpression(out condition))
            { throw new SyntaxException($"Expected condition after \"{keyword}\" initialization", semicolon1.Position.After(), File); }

            if (!ExpectOperator(";", out semicolon2))
            { throw new SyntaxException($"Expected \";\" after \"{keyword}\" condition", condition.Position.After(), File); }
            condition.Semicolon = semicolon2;
        }

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            step = null;
        }
        else
        {
            if (!ExpectStatementUnchecked(out step))
            { throw new SyntaxException($"Expected a statement after \"{keyword}\" condition", semicolon2.Position.After(), File); }

            SetStatementThings(step);

            if (!ExpectOperator(")", out bracketEnd))
            { throw new SyntaxException($"Expected \")\" after \"{keyword}\" assignment", step.Position.After(), File); }
            step.Semicolon = semicolon2;
        }

        if (!ExpectBlock(out Block? block))
        { throw new SyntaxException($"Expected block", bracketEnd.Position.After(), File); }

        forLoop = new ForLoopStatement(keyword, initialization, condition, step, block, File);
        return true;
    }

    bool ExpectWhileStatement([NotNullWhen(true)] out WhileLoopStatement? whileLoop)
    {
        ParseRestorePoint savepoint = SavePoint();
        whileLoop = null;

        if (!ExpectIdentifier(StatementKeywords.While, out Token? keyword))
        {
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        if (!ExpectOperator("(", out Token? bracketStart))
        { throw new SyntaxException($"Expected \"(\" after \"{keyword}\" keyword", keyword.Position.After(), File); }

        if (!ExpectExpression(out Expression? condition))
        { throw new SyntaxException($"Expected condition after \"{bracketStart}\"", bracketStart.Position.After(), File); }

        if (!ExpectOperator(")", out Token? bracketEnd))
        { throw new SyntaxException($"Expected \")\" after while-loop condition", condition.Position.After(), File); }

        if (!ExpectStatement(out Statement? block))
        { throw new SyntaxException($"Expected a statement after \"{keyword}\" condition", bracketEnd.Position.After(), File); }

        whileLoop = new WhileLoopStatement(keyword, condition, block, File);
        return true;
    }

    bool ExpectIfStatement([NotNullWhen(true)] out IfBranchStatement? ifStatement)
    {
        ParseRestorePoint savepoint = SavePoint();
        ifStatement = null;

        if (!ExpectIdentifier(StatementKeywords.If, out Token? ifKeyword))
        {
            savepoint.Restore();
            return false;
        }

        ifKeyword.AnalyzedType = TokenAnalyzedType.Statement;

        if (!ExpectOperator("(", out Token? bracketStart))
        { throw new SyntaxException($"Expected \"(\" after keyword \"{ifKeyword}\"", ifKeyword.Position.After(), File); }

        if (!ExpectExpression(out Expression? condition))
        { throw new SyntaxException($"Expected condition after \"{ifKeyword} (\"", bracketStart.Position.After(), File); }

        if (!ExpectOperator(")", out Token? bracketEnd))
        { throw new SyntaxException($"Expected \")\" after \"{ifKeyword}\" condition", condition.Position.After(), File); }

        if (!ExpectStatement(out Statement? ifBlock))
        { throw new SyntaxException($"Expected a statement after \"{ifKeyword}\" condition", bracketEnd.Position.After(), File); }

        ElseBranchStatement? elseBranch = null;

        if (ExpectIdentifier(StatementKeywords.Else, out Token? elseKeyword))
        {
            elseKeyword.AnalyzedType = TokenAnalyzedType.Statement;

            if (!ExpectStatement(out Statement? elseBlock))
            { throw new SyntaxException($"Expected a statement after \"{elseKeyword}\" condition", elseKeyword.Position.After(), File); }

            elseBranch = new ElseBranchStatement(elseKeyword, elseBlock, File);
        }

        ifStatement = new IfBranchStatement(ifKeyword, condition, ifBlock, elseBranch, File);
        return true;
    }

    bool ExpectStatement([NotNullWhen(true)] out Statement? statement)
    {
        if (ExpectOperator(";", out Token? semicolon))
        {
            statement = new EmptyStatement(semicolon.Position.Before(), File);
            Diagnostics.Add(Diagnostic.Warning($"Empty statement?", semicolon, File));
            return true;
        }

        if (!ExpectStatementUnchecked(out statement))
        {
            return false;
        }

        if (!IsExpression) SetStatementThings(statement);

        if (NeedSemicolon(statement))
        {
            if (!ExpectOperator(";", out semicolon) && !IsExpression)
            { Diagnostics.Add(Diagnostic.Warning($"You forgot the semicolon", statement.Position.After(), File)); }
        }
        else
        {
            if (ExpectOperator(";", out semicolon))
            { Diagnostics.Add(Diagnostic.Warning($"Unecessary semicolon", semicolon, File)); }
        }

        statement.Semicolon = semicolon;

        return true;
    }

    bool ExpectStatementUnchecked([NotNullWhen(true)] out Statement? statement)
    {
        if (ExpectInstructionLabel(out InstructionLabelDeclaration? instructionLabel))
        {
            statement = instructionLabel;
            return true;
        }

        if (ExpectWhileStatement(out WhileLoopStatement? whileLoop))
        {
            statement = whileLoop;
            return true;
        }

        if (ExpectForStatement(out ForLoopStatement? forLoop))
        {
            statement = forLoop;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Return, 0, 1, out KeywordCallStatement? keywordCallReturn))
        {
            statement = keywordCallReturn;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Yield, 0, 1, out KeywordCallStatement? keywordCallYield))
        {
            statement = keywordCallYield;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Goto, 1, out KeywordCallStatement? keywordCallGoto))
        {
            statement = keywordCallGoto;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Crash, 1, out KeywordCallStatement? keywordCallThrow))
        {
            statement = keywordCallThrow;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Break, 0, out KeywordCallStatement? keywordCallBreak))
        {
            statement = keywordCallBreak;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Delete, 1, out KeywordCallStatement? keywordCallDelete))
        {
            statement = keywordCallDelete;
            return true;
        }

        if (ExpectIfStatement(out IfBranchStatement? ifStatement))
        {
            statement = ifStatement;
            return true;
        }

        if (ExpectVariableDeclaration(out VariableDefinition? variableDeclaration))
        {
            statement = variableDeclaration;
            return true;
        }

        if (ExpectAnySetter(out AssignmentStatement? assignment))
        {
            statement = assignment;
            return true;
        }

        if (ExpectExpression(out Expression? expression))
        {
            statement = expression;
            return true;
        }

        if (ExpectBlock(out Block? block))
        {
            statement = block;
            return true;
        }

        statement = null;
        return false;
    }

    bool ExpectUnaryOperatorCall([NotNullWhen(true)] out UnaryOperatorCallExpression? result)
    {
        ParseRestorePoint savepoint = SavePoint();
        result = null;

        if (!ExpectOperator(UnaryPrefixOperators, out Token? unaryPrefixOperator))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? statement))
        { throw new SyntaxException($"Expected value after operator \"{unaryPrefixOperator}\" (not \"{CurrentToken}\")", unaryPrefixOperator.Position.After(), File); }

        unaryPrefixOperator.AnalyzedType = TokenAnalyzedType.MathOperator;

        result = new UnaryOperatorCallExpression(unaryPrefixOperator, statement, File);
        return true;
    }

    bool ExpectExpression([NotNullWhen(true)] out Expression? result)
    {
        result = null;

        if (ExpectUnaryOperatorCall(out UnaryOperatorCallExpression? unaryOperatorCall))
        {
            result = unaryOperatorCall;
            return true;
        }

        if (!ExpectModifiedOrOneValue(out Expression? leftStatement, GeneralStatementModifiers)) return false;

        while (true)
        {
            if (!ExpectOperator(BinaryOperators, out Token? binaryOperator)) break;

            if (!ExpectModifiedOrOneValue(out Expression? rightStatement, GeneralStatementModifiers))
            {
                if (!ExpectUnaryOperatorCall(out UnaryOperatorCallExpression? rightUnaryOperatorCall))
                { throw new SyntaxException($"Expected value after operator \"{binaryOperator}\" (not \"{CurrentToken}\")", binaryOperator.Position.After(), File); }
                else
                { rightStatement = rightUnaryOperatorCall; }
            }

            binaryOperator.AnalyzedType = TokenAnalyzedType.MathOperator;

            int rightSidePrecedence = OperatorPrecedence(binaryOperator.Content);

            BinaryOperatorCallExpression? rightmostStatement = FindRightmostStatement(leftStatement, rightSidePrecedence);
            if (rightmostStatement != null)
            {
                rightmostStatement.Right = new BinaryOperatorCallExpression(binaryOperator, rightmostStatement.Right, rightStatement, File);
            }
            else
            {
                leftStatement = new BinaryOperatorCallExpression(binaryOperator, leftStatement, rightStatement, File);
            }
        }

        result = leftStatement;
        return true;
    }

    bool ExpectAnySetter([NotNullWhen(true)] out AssignmentStatement? assignment)
    {
        if (ExpectShortOperator(out ShortOperatorCall? shortOperatorCall))
        {
            assignment = shortOperatorCall;
            return true;
        }

        if (ExpectCompoundSetter(out CompoundAssignmentStatement? compoundAssignment))
        {
            assignment = compoundAssignment;
            return true;
        }

        if (ExpectSetter(out SimpleAssignmentStatement? simpleSetter))
        {
            assignment = simpleSetter;
            return true;
        }

        assignment = null;
        return false;
    }

    bool ExpectSetter([NotNullWhen(true)] out SimpleAssignmentStatement? assignment)
    {
        ParseRestorePoint savepoint = SavePoint();
        assignment = null;

        if (!ExpectExpression(out Expression? leftStatement))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("=", out Token? @operator))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectExpression(out Expression? valueToAssign))
        { throw new SyntaxException("Expected expression after assignment operator", @operator, File); }

        @operator.AnalyzedType = TokenAnalyzedType.OtherOperator;

        assignment = new SimpleAssignmentStatement(@operator, leftStatement, valueToAssign, File);
        return true;
    }

    bool ExpectCompoundSetter([NotNullWhen(true)] out CompoundAssignmentStatement? compoundAssignment)
    {
        ParseRestorePoint savepoint = SavePoint();
        compoundAssignment = null;

        if (!ExpectExpression(out Expression? leftStatement))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(CompoundAssignmentOperators, out Token? @operator))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectExpression(out Expression? valueToAssign))
        { throw new SyntaxException("Expected expression after compound assignment operator", @operator, File); }

        @operator.AnalyzedType = TokenAnalyzedType.MathOperator;

        compoundAssignment = new CompoundAssignmentStatement(@operator, leftStatement, valueToAssign, File);
        return true;
    }

    bool ExpectShortOperator([NotNullWhen(true)] out ShortOperatorCall? shortOperatorCall)
    {
        ParseRestorePoint savepoint = SavePoint();
        shortOperatorCall = null;

        if (!ExpectExpression(out Expression? expression))
        {
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator(IncrementDecrementOperators, out Token? @operator))
        {
            @operator.AnalyzedType = TokenAnalyzedType.MathOperator;
            shortOperatorCall = new ShortOperatorCall(@operator, expression, File);
            return true;
        }

        savepoint.Restore();
        shortOperatorCall = null;
        return false;
    }

    bool ExpectModifiedOrOneValue([NotNullWhen(true)] out Expression? oneValue, ImmutableArray<string> validModifiers)
    {
        if (!ExpectIdentifier(out Token? modifier, validModifiers))
        {
            return ExpectOneValue(out oneValue);
        }

        modifier.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectOneValue(out Expression? expression))
        {
            oneValue = new IdentifierExpression(modifier, File);
            Diagnostics.Add(Diagnostic.Warning($"is this ok?", oneValue));
            return true;
            // throw new SyntaxException($"Expected one value after modifier \"{modifier}\"", modifier.Position.After(), File);
        }

        oneValue = new ArgumentExpression(modifier, expression, File);
        return true;
    }

    bool ExpectModifiedValue([NotNullWhen(true)] out ArgumentExpression? modifiedStatement, ImmutableArray<string> validModifiers)
    {
        if (!ExpectIdentifier(out Token? modifier, validModifiers))
        {
            modifiedStatement = null;
            return false;
        }

        modifier.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectOneValue(out Expression? value))
        { throw new SyntaxException($"Expected one value after modifier \"{modifier}\"", modifier.Position.After(), File); }

        modifiedStatement = new ArgumentExpression(modifier, value, File);
        return true;
    }

    static BinaryOperatorCallExpression? FindRightmostStatement(Statement? statement, int rightSidePrecedence)
    {
        if (statement is not BinaryOperatorCallExpression leftSide) return null;
        if (OperatorPrecedence(leftSide.Operator.Content) >= rightSidePrecedence) return null;
        if (leftSide.SurroundingBrackets.HasValue) return null;

        BinaryOperatorCallExpression? right = FindRightmostStatement(leftSide.Right, rightSidePrecedence);

        if (right == null) return leftSide;
        return right;
    }

    static int OperatorPrecedence(string @operator)
    {
        if (LanguageOperators.Precedencies.TryGetValue(@operator, out int precedence))
        { return precedence; }
        throw new InternalExceptionWithoutContext($"Precedence for operator \"{@operator}\" not found");
    }

    bool ExpectAnyCall(Expression prevStatement, [NotNullWhen(true)] out AnyCallExpression? anyCall)
    {
        ParseRestorePoint savepoint = SavePoint();
        anyCall = null;

        if (!ExpectArguments(out ArgumentListExpression? argumentList))
        {
            savepoint.Restore();
            return false;
        }

        anyCall = new AnyCallExpression(prevStatement, argumentList, File);
        return true;
    }

    bool ExpectArguments([NotNullWhen(true)] out ArgumentListExpression? argumentList)
    {
        ParseRestorePoint savepoint = SavePoint();
        argumentList = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        bool expectParameter = false;
        ImmutableArray<ArgumentExpression>.Builder parameters = ImmutableArray.CreateBuilder<ArgumentExpression>();
        ImmutableArray<Token>.Builder commas = ImmutableArray.CreateBuilder<Token>();

        EndlessCheck endlessSafe = new();
        Token? bracketEnd;
        while (!ExpectOperator(")", out bracketEnd) || expectParameter)
        {
            ArgumentExpression? parameter;

            if (ExpectModifiedValue(out ArgumentExpression? modifiedStatement, ArgumentModifiers))
            {
                parameter = modifiedStatement;
            }
            else if (ExpectExpression(out Expression? simpleParameter))
            {
                parameter = ArgumentExpression.Wrap(simpleParameter);
            }
            else
            {
                throw new SyntaxException($"Expected expression as an argument", CurrentLocation);
                Diagnostics.Add(Diagnostic.Error($"Expected expression as an argument", CurrentLocation));
                savepoint.Restore();
                return false;
            }

            parameters.Add(parameter);

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(",", out Token? comma))
            {
                throw new SyntaxException($"Expected `,` to separate arguments", parameter.Location.After());
                Diagnostics.Add(Diagnostic.Error($"Expected `,` to separate arguments", parameter.Location.After()));
                savepoint.Restore();
                return false;
            }
            else
            { expectParameter = true; }
            commas.Add(comma);

            endlessSafe.Step();
        }

        argumentList = new ArgumentListExpression(parameters.DrainToImmutable(), commas.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectKeywordCall(string name, int parameterCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall)
        => ExpectKeywordCall(name, parameterCount, parameterCount, out keywordCall);
    bool ExpectKeywordCall(string name, int minParameterCount, int maxParameterCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall)
    {
        ParseRestorePoint savepoint = SavePoint();
        keywordCall = null;

        if (!ExpectIdentifier(out Token? possibleFunctionName))
        {
            savepoint.Restore();
            return false;
        }

        if (possibleFunctionName.Content != name)
        {
            savepoint.Restore();
            return false;
        }

        possibleFunctionName.AnalyzedType = TokenAnalyzedType.Statement;

        ImmutableArray<Expression>.Builder? parameters = null;

        EndlessCheck endlessSafe = new();
        while (true)
        {
            endlessSafe.Step();

            if (!ExpectExpression(out Expression? parameter)) break;

            parameters ??= ImmutableArray.CreateBuilder<Expression>();
            parameters.Add(parameter);
        }

        keywordCall = new(possibleFunctionName, parameters?.DrainToImmutable() ?? ImmutableArray<Expression>.Empty, File);

        if (keywordCall.Arguments.Length < minParameterCount)
        { Diagnostics.Add(Diagnostic.Error($"This keyword-call (\"{possibleFunctionName}\") requires minimum {minParameterCount} parameters but you passed {parameters?.Count ?? 0}", keywordCall, File)); }

        if (keywordCall.Arguments.Length > maxParameterCount)
        { Diagnostics.Add(Diagnostic.Error($"This keyword-call (\"{possibleFunctionName}\") requires maximum {maxParameterCount} parameters but you passed {parameters?.Count ?? 0}", keywordCall, File)); }

        return true;
    }

    #endregion

    bool ExpectAttribute([NotNullWhen(true)] out AttributeUsage? attribute)
    {
        ParseRestorePoint savepoint = SavePoint();
        attribute = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? attributeT))
        {
            savepoint.Restore();
            return false;
        }

        attributeT.AnalyzedType = TokenAnalyzedType.Attribute;

        List<LiteralExpression>? parameters = null;
        if (ExpectOperator("(", out Token? bracketParametersStart))
        {
            EndlessCheck endlessSafe = new();
            while (!ExpectOperator(")"))
            {
                ExpectLiteral(out LiteralExpression? param);
                if (param == null)
                { throw new SyntaxException("Expected parameter", bracketParametersStart, File); }
                ExpectOperator(",");

                parameters ??= new();
                parameters.Add(param);

                endlessSafe.Step();
            }
        }

        if (!ExpectOperator("]"))
        { throw new SyntaxException("Unbalanced ]", bracketStart, File); }

        attribute = new AttributeUsage(attributeT, parameters?.ToImmutableArray() ?? ImmutableArray<LiteralExpression>.Empty, File);
        return true;
    }
    ImmutableArray<AttributeUsage> ExpectAttributes()
    {
        ImmutableArray<AttributeUsage>.Builder? attributes = null;
        while (ExpectAttribute(out AttributeUsage? attr))
        {
            attributes ??= ImmutableArray.CreateBuilder<AttributeUsage>();
            attributes.Add(attr);
        }
        return attributes?.DrainToImmutable() ?? ImmutableArray<AttributeUsage>.Empty;
    }

    bool ExpectField([NotNullWhen(true)] out FieldDefinition? field, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        field = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? possibleType))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected type for field definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? fieldName))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Expected identifier for field definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator("(", out Token? unexpectedThing))
        {
            diagnostic.Add(0, Diagnostic.Critical($"Unexpected `(` after field identifier", unexpectedThing, File, false));
            savepoint.Restore();
            return false;
        }

        fieldName.AnalyzedType = TokenAnalyzedType.FieldName;

        CheckModifiers(modifiers, FieldModifiers);

        field = new(fieldName, possibleType, modifiers, attributes);
        return true;
    }

    #region Basic parsing

    ImmutableArray<Token> ExpectModifiers() => ExpectModifiers(AllModifiers);

    ImmutableArray<Token> ExpectModifiers(ImmutableArray<string> modifiers)
    {
        ImmutableArray<Token>.Builder? result = null;

        EndlessCheck endlessSafe = new();
        while (true)
        {
            if (ExpectIdentifier(out Token? modifier, modifiers))
            {
                result ??= ImmutableArray.CreateBuilder<Token>();
                modifier.AnalyzedType = TokenAnalyzedType.Keyword;
                result.Add(modifier);
            }
            else
            { break; }

            endlessSafe.Step();
        }

        return result?.DrainToImmutable() ?? ImmutableArray<Token>.Empty;
    }

    void CheckParameterModifiers(IEnumerable<Token> modifiers, int parameterIndex, ImmutableArray<string> validModifiers)
    {
        foreach (Token modifier in modifiers)
        {
            if (!validModifiers.Contains(modifier.Content))
            { Diagnostics.Add(Diagnostic.Error($"Modifier \"{modifier}\" not valid in the current context", modifier, File)); }

            if (modifier.Content == ModifierKeywords.This &&
                parameterIndex != 0)
            { Diagnostics.Add(Diagnostic.Error($"Modifier \"{ModifierKeywords.This}\" only valid on the first parameter", modifier, File)); }
        }
    }

    void CheckModifiers(IEnumerable<Token> modifiers, ImmutableArray<string> validModifiers)
    {
        foreach (Token modifier in modifiers)
        {
            if (!validModifiers.Contains(modifier.Content))
            { Diagnostics.Add(Diagnostic.Error($"Modifier \"{modifier}\" not valid in the current context", modifier, File)); }
        }
    }

    bool ExpectInstructionLabel([NotNullWhen(true)] out InstructionLabelDeclaration? instructionLabel)
    {
        instructionLabel = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectIdentifier(out Token? identifier))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(":", out Token? colon))
        {
            savepoint.Restore();
            return false;
        }

        identifier.AnalyzedType = TokenAnalyzedType.InstructionLabel;

        instructionLabel = new InstructionLabelDeclaration(
            new IdentifierExpression(identifier, File),
            colon,
            File
        );
        return true;
    }

    bool ExpectIdentifier([NotNullWhen(true)] out Token? result) => ExpectIdentifier("", out result);
    bool ExpectIdentifier(string name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken == null) return false;
        if (CurrentToken.TokenType != TokenType.Identifier) return false;
        if (name.Length > 0 && CurrentToken.Content != name) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }
    bool ExpectIdentifier([NotNullWhen(true)] out Token? result, ImmutableArray<string> names)
    {
        foreach (string name in names)
        {
            if (ExpectIdentifier(name, out result))
            { return true; }
        }
        result = null;
        return false;
    }

    bool ExpectOperator(string name) => ExpectOperator(name, out _);
    bool ExpectOperator(ImmutableArray<string> name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken == null) return false;
        if (CurrentToken.TokenType != TokenType.Operator) return false;
        if (!name.Contains(CurrentToken.Content)) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }
    bool ExpectOperator(string name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken == null) return false;
        if (CurrentToken.TokenType != TokenType.Operator) return false;
        if (name.Length > 0 && CurrentToken.Content != name) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }

    void SkipCrapTokens()
    {
        while (CurrentToken is not null &&
               CurrentToken.TokenType is
               TokenType.PreprocessIdentifier or
               TokenType.PreprocessArgument or
               TokenType.PreprocessSkipped)
        { CurrentTokenIndex++; }
    }

    [Flags]
    enum AllowedType
    {
        None = 0x0,
        Any = 0x1,
        FunctionPointer = 0x2,
        StackArrayWithoutLength = 0x4,
    }

    static readonly ImmutableArray<string> TheseCharactersIndicateThatTheIdentifierWillBeFollowedByAComplexType = ImmutableArray.Create("<", "(", "[");

    bool ExpectType(AllowedType flags, [NotNullWhen(true)] out TypeInstance? type)
    {
        if (ExpectType(flags, out type, out Diagnostic? error))
        { return true; }
        if (error is not null)
        { Diagnostics.Add(error.Break()); }
        return false;
    }

    bool ExpectType(AllowedType flags, [NotNullWhen(true)] out TypeInstance? type, [MaybeNullWhen(true)] out Diagnostic? error)
    {
        type = default;
        error = null;

        if (!ExpectIdentifier(out Token? possibleType)) return false;

        if (possibleType.Equals(StatementKeywords.Return))
        { return false; }

        Token? closureModifier = null;

        if (CurrentToken is not null
            && CurrentToken.TokenType == TokenType.Identifier
            && CurrentToken.Position.AbsoluteRange.Start == possibleType.Position.AbsoluteRange.End)
        {
            closureModifier = possibleType;
            possibleType = CurrentToken;
            CurrentTokenIndex++;
        }
        else if (possibleType.Content.StartsWith('@'))
        {
            int slicedIndex = CurrentTokenIndex - 1;
            closureModifier = possibleType[..1];
            closureModifier.AnalyzedType = TokenAnalyzedType.TypeModifier;
            possibleType = possibleType[1..];
            Tokens.RemoveAt(slicedIndex);
            Tokens.Insert(slicedIndex, possibleType);
            Tokens.Insert(slicedIndex, closureModifier);
            CurrentTokenIndex++;
        }

        type = new TypeInstanceSimple(possibleType, File);

        if (possibleType.Content.Equals(TypeKeywords.Any))
        {
            possibleType.AnalyzedType = TokenAnalyzedType.Keyword;

            if (ExpectOperator(TheseCharactersIndicateThatTheIdentifierWillBeFollowedByAComplexType, out Token? illegalT))
            { Diagnostics.Add(Diagnostic.Error($"This is not allowed", illegalT, File)); }

            if (ExpectOperator("*", out Token? pointerOperator))
            {
                pointerOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstancePointer(type, pointerOperator, File);
            }
            else
            {
                if ((flags & AllowedType.Any) == 0)
                {
                    error = Diagnostic.Error($"Type \"{TypeKeywords.Any}\" is not valid in the current context", possibleType, File);
                    return false;
                }
            }

            goto end;
        }

        if (TypeKeywords.List.Contains(possibleType.Content))
        {
            possibleType.AnalyzedType = TokenAnalyzedType.BuiltinType;
        }
        else
        {
            possibleType.AnalyzedType = TokenAnalyzedType.Type;
        }

        int afterIdentifier = CurrentTokenIndex;
        bool withGenerics = false;

        while (true)
        {
            if (ExpectOperator("*", out Token? pointerOperator))
            {
                pointerOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstancePointer(type, pointerOperator, File);
            }
            else if (ExpectOperator("<"))
            {
                if (type is not TypeInstanceSimple)
                { throw new NotImplementedException(); }

                List<TypeInstance> genericTypes = new();

                while (true)
                {
                    if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? typeParameter))
                    {
                        CurrentTokenIndex = afterIdentifier;
                        goto end;
                    }

                    genericTypes.Add(typeParameter);

                    if (ExpectOperator(">"))
                    { break; }

                    if (ExpectOperator(">>", out Token? doubleEnd))
                    {
                        (Token? newA, Token? newB) = doubleEnd.Slice(1);
                        if (newA == null || newB == null)
                        { throw new UnreachableException($"I failed at token splitting :("); }
                        CurrentTokenIndex--;
                        Tokens[CurrentTokenIndex] = newB;
                        break;
                    }

                    if (ExpectOperator(","))
                    { continue; }
                }

                type = new TypeInstanceSimple(possibleType, File, genericTypes.ToImmutableArray());
                withGenerics = true;
            }
            else if (!withGenerics && ExpectOperator("(", out Token? bracketStart))
            {
                if (!flags.HasFlag(AllowedType.FunctionPointer))
                {
                    CurrentTokenIndex--;
                    goto end;
                }

                List<TypeInstance> parameterTypes = new();
                Token? bracketEnd;
                while (!ExpectOperator(")", out bracketEnd))
                {
                    if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? subtype))
                    {
                        CurrentTokenIndex = afterIdentifier;
                        goto end;
                    }

                    parameterTypes.Add(subtype);

                    if (ExpectOperator(")", out bracketEnd))
                    { break; }

                    if (ExpectOperator(","))
                    { continue; }
                }

                type = new TypeInstanceFunction(type, parameterTypes.ToImmutableArray(), closureModifier, File, new(bracketStart, bracketEnd));
            }
            else if (ExpectOperator("[", out _))
            {
                if (ExpectOperator("]"))
                {
                    type = new TypeInstanceStackArray(type, null, File);
                }
                else if (ExpectExpression(out Expression? sizeValue))
                {
                    if (!ExpectOperator("]"))
                    { return false; }

                    type = new TypeInstanceStackArray(type, sizeValue, File);
                }
                else
                {
                    return false;
                }
            }
            else
            { break; }
        }

    end:
        if (type is not TypeInstanceFunction && closureModifier is not null)
        {
            error = Diagnostic.Error($"This type modifier is bruh", closureModifier, File);
            return false;
        }

        return true;
    }

    static bool NeedSemicolon(Statement statement) => statement is not (
        ForLoopStatement or
        WhileLoopStatement or
        Block or
        BranchStatementBase or
        InstructionLabelDeclaration or
        LambdaExpression
    );

    #endregion
}
