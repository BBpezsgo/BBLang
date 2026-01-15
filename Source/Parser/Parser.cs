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
        Tokens = tokens.ToList();
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
            while (CurrentToken != null && ParseCodeBlock())
            {
                SkipCrapTokens();
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

        if (CurrentToken == null) throw new SyntaxException($"Expected url after keyword `{DeclarationKeywords.Using}`", keyword.Position.After(), File);

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
            Diagnostics.Add(Diagnostic.Error($"Expected library name after `{DeclarationKeywords.Using}`", keyword, File));
            usingDefinition = new UsingDefinition(keyword, ImmutableArray<Token>.Empty, File);
            return true;
        }

        usingDefinition = new UsingDefinition(keyword, tokens.ToImmutableArray(), File);

        if (!ExpectOperator(";"))
        { Diagnostics.Add(Diagnostic.Warning($"You forgot the semicolon", usingDefinition.Position.After(), File)); }

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
            Diagnostics.Add(Diagnostic.Error($"Expected something but not `{CurrentToken}`", CurrentToken, File).WithSuberrors(diagnostics.Compile()));
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
            diagnostic.Add(1, Diagnostic.Critical($"Expected identifier after keyword `{keyword}`", keyword.Position.After(), File));
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

        if (ExpectOperator(">", out Token? endBracket))
        {
            templateInfo = new TemplateInfo(new TokenPair(startBracket, endBracket), ImmutableArray<Token>.Empty);
            Diagnostics.Add(Diagnostic.Warning($"Empty template", templateInfo, File));
            return true;
        }

        List<Token> parameters = new();
        Position lastPosition = startBracket.Position;

        while (true)
        {
            if (!ExpectIdentifier(out Token? parameter))
            {
                Diagnostics.Add(Diagnostic.Error("Expected identifier or `>`", lastPosition.After(), File));
                parameter = new MissingToken(TokenType.Identifier, lastPosition.After());
            }

            parameter.AnalyzedType = TokenAnalyzedType.TypeParameter;
            parameters.Add(parameter);

            if (ExpectOperator(">", out endBracket))
            { break; }

            if (!ExpectOperator(",", out Token? comma))
            {
                throw new SyntaxException("Expected `,` or `>`", parameter.Position.After(), File);
                Diagnostics.Add(Diagnostic.Error("Expected `,` or `>`", parameter.Position.After(), File));
                endBracket = new MissingToken(TokenType.Operator, parameter.Position.After(), ">");
                break;
            }

            lastPosition = comma.Position;
        }

        templateInfo = new TemplateInfo(new TokenPair(startBracket, endBracket), parameters.ToImmutableArray());
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
            possibleStructName = new MissingToken(TokenType.Identifier, keyword.Position.After());
            Diagnostics.Add(Diagnostic.Critical($"Expected identifier after keyword `{keyword}`", possibleStructName, File));
        }

        ExpectTemplateInfo(out TemplateInfo? templateInfo);

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, possibleStructName.Position.After(), "{");
            Diagnostics.Add(Diagnostic.Critical($"Expected `{{` after struct identifier `{keyword}`", bracketStart, File));
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
                {
                    field.Semicolon = semicolon;
                }
                else
                {
                    Diagnostics.Add(Diagnostic.Warning($"You forgot the `;`", field.Position.After(), File));
                }
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
                bracketEnd = new MissingToken(TokenType.Operator, PreviousToken!.Position.After(), "}");
                break;
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

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            parameterDefinitions = new ParameterDefinitionCollection(ImmutableArray<ParameterDefinition>.Empty, new TokenPair(bracketStart, bracketEnd));
            return true;
        }

        List<ParameterDefinition> parameters = new();
        Position lastPosition = bracketStart.Position;
        bool expectOptionalParameters = false;

        while (true)
        {
            ImmutableArray<Token> parameterModifiers = ExpectModifiers();

            foreach (Token modifier in parameterModifiers)
            {
                if (!allowedParameterModifiers.Contains(modifier.Content))
                { Diagnostics.Add(Diagnostic.Error($"Modifier `{modifier}` not valid in the current context", modifier, File)); }
                else if (modifier.Content == ModifierKeywords.This && parameters.Count > 0)
                { Diagnostics.Add(Diagnostic.Error($"Modifier `{modifier}` only valid on the first parameter", modifier, File)); }
            }

            if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? parameterType))
            {
                parameterType = new MissingTypeInstance(lastPosition.After(), File);
                diagnostic.Add(1, Diagnostic.Error("Expected parameter type", parameterType, false));
                savepoint.Restore();
                return false;
                //Diagnostics.Add(Diagnostic.Error("Expected parameter type", parameterType));
            }

            if (!ExpectIdentifier(out Token? parameterIdentifier))
            {
                parameterIdentifier = new MissingToken(TokenType.Identifier, parameterType.Position.After());
                diagnostic.Add(1, Diagnostic.Error("Expected a parameter name", parameterIdentifier, File, false));
                savepoint.Restore();
                return false;
                //Diagnostics.Add(Diagnostic.Error("Expected a parameter name", parameterIdentifier, File));
            }

            parameterIdentifier.AnalyzedType = TokenAnalyzedType.ParameterName;

            Expression? defaultValue = null;
            if (ExpectOperator("=", out Token? assignmentOperator))
            {
                if (!ExpectExpression(out defaultValue))
                {
                    defaultValue = new MissingExpression(assignmentOperator.Position.After(), File);
                    Diagnostics.Add(Diagnostic.Error("Expected expression", defaultValue));
                }

                if (!allowDefaultValues)
                {
                    Diagnostics.Add(Diagnostic.Error("Default parameter values are not valid in the current context", defaultValue));
                }

                expectOptionalParameters = true;
            }
            else if (expectOptionalParameters)
            {
                Diagnostics.Add(Diagnostic.Error("Parameters without default value after a parameter that has one is not supported", parameterIdentifier.Position.After(), File));
            }

            ParameterDefinition parameter = new(parameterModifiers, parameterType, parameterIdentifier, defaultValue, File);
            parameters.Add(parameter);

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(","))
            {
                diagnostic.Add(2, Diagnostic.Error("Expected `,` or `)`", parameter.Position.After(), File, false));
                savepoint.Restore();
                return false;
            }

            lastPosition = parameter.Position;
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

        if (ExpectOperator("]", out Token? bracketEnd))
        {
            listValue = new ListExpression(ImmutableArray<Expression>.Empty, new TokenPair(bracketStart, bracketEnd), File);
            return true;
        }

        ImmutableArray<Expression>.Builder values = ImmutableArray.CreateBuilder<Expression>();
        EndlessCheck endlessSafe = new();
        Position lastPosition = bracketStart.Position;

        while (true)
        {
            if (ExpectOperator("]", out bracketEnd))
            { break; }

            if (!ExpectExpression(out Expression? value))
            {
                value = new MissingExpression(lastPosition.After(), File);
                Diagnostics.Add(Diagnostic.Error("Expected expression or `]`", value));
            }

            values.Add(value);
            lastPosition = value.Position;

            if (!ExpectOperator(",", out Token? comma))
            {
                if (!ExpectOperator("]", out bracketEnd))
                {
                    bracketEnd = new MissingToken(TokenType.Operator, lastPosition.After(), "]");
                    Diagnostics.Add(Diagnostic.Error("Expected `,` or `]`", bracketEnd, File));
                }
                break;
            }

            lastPosition = comma.Position;

            endlessSafe.Step();
        }

        listValue = new ListExpression(values.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
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
                Diagnostics.Add(Diagnostic.Error($"Invalid hex literal `{CurrentToken}`", CurrentToken, File));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            if (!int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            {
                Diagnostics.Add(Diagnostic.Error($"Invalid hex number `{v}`", CurrentToken.Position[2..], File));
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
                Diagnostics.Add(Diagnostic.Error($"Invalid binary literal `{CurrentToken}`", CurrentToken, File));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            // if (!int.TryParse(v, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out int value))
            // {
            //     Diagnostics.Add(Diagnostic.Error($"Invalid binary number `{v}`", CurrentToken.Position[2..], File));
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
        {
            bracketEnd = new MissingToken(TokenType.Operator, expression.Position.After(), "]");
            Diagnostics.Add(Diagnostic.Error("Expected `]`", bracketEnd, File));
        }

        // fixme
        statement = new IndexCallExpression(prevStatement, ArgumentExpression.Wrap(expression), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectExpressionInBrackets([NotNullWhen(true)] out Expression? expressionInBrackets)
    {
        ParseRestorePoint savepoint = SavePoint();
        expressionInBrackets = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectExpression(out Expression? expression))
        { throw new SyntaxException("Expected expression after \"(\"", bracketStart.Position.After(), File); }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, expression.Position.After(), ")");
            Diagnostics.Add(Diagnostic.Error("Expected `)`", bracketEnd, File));
        }

        expression.SurroundingBrackets = new TokenPair(bracketStart, bracketEnd);

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
        {
            instanceTypeName = new MissingTypeInstance(keywordNew.Position.After(), File);
            Diagnostics.Add(Diagnostic.Error($"Expected instance constructor after keyword `{StatementKeywords.New}`", instanceTypeName));
        }

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
            fieldName = new MissingToken(TokenType.Identifier, tokenDot.Position.After());
            fieldAccessor = new FieldExpression(
                prevStatement,
                new MissingIdentifierExpression(fieldName, File),
                File
            );
            Diagnostics.Add(Diagnostic.Critical("Expected a symbol after `.`", fieldName, File));
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
        {
            type = new MissingTypeInstance(keyword.Position.After(), File);
            Diagnostics.Add(Diagnostic.Error($"Expected type after keyword `{keyword}`", type));
        }

        basicTypeCast = new ReinterpretExpression(prevStatement, keyword, type, File);
        return true;
    }

    bool ExpectIdentifierExpression([NotNullWhen(true)] out IdentifierExpression? expression)
    {
        ParseRestorePoint savepoint = SavePoint();
        expression = null;

        if (!ExpectIdentifier(out Token? simpleIdentifier))
        {
            savepoint.Restore();
            return false;
        }

        if (simpleIdentifier.Content
            is StatementKeywords.This
            or StatementKeywords.Sizeof)
        {
            simpleIdentifier.AnalyzedType = TokenAnalyzedType.Keyword;
            expression = new IdentifierExpression(simpleIdentifier, File);
            return true;
        }

        if (StatementKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (ProtectionKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (ModifierKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (DeclarationKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        expression = new IdentifierExpression(simpleIdentifier, File);
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
        else if (ExpectIdentifierExpression(out IdentifierExpression? identifierExpression))
        {
            statementWithValue = identifierExpression;
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
        if (statement
            is LiteralExpression
            or IdentifierExpression
            or NewInstanceExpression
            or ConstructorCallExpression
            or BinaryOperatorCallExpression
            or UnaryOperatorCallExpression
            or IndexCallExpression
            or FieldExpression
            or DereferenceExpression
            or GetReferenceExpression
            or LambdaExpression
            or ListExpression
            or ManagedTypeCastExpression
            or ReinterpretExpression)
        { Diagnostics.Add(Diagnostic.Warning("Unexpected expression", statement)); }

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
        Position lastPosition = bracketStart.Position;

        while (!ExpectOperator("}", out bracketEnd))
        {
            if (!ExpectStatement(out Statement? statement))
            {
                statement = new MissingStatement(lastPosition.After(), File);
                Diagnostics.Add(Diagnostic.Error($"Expected a statement", statement));
            }

            statements.Add(statement);
            lastPosition = statement.Position;

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
            {
                initialValue = new MissingExpression(eqOperatorToken.Position.After(), File);
                Diagnostics.Add(Diagnostic.Error("Expected initial value after `=` in variable declaration", initialValue));
            }
        }
        else
        {
            if (possibleType == StatementKeywords.Var)
            {
                Diagnostics.Add(Diagnostic.Error("Initial value for variable declaration with implicit type is required", possibleType, File));
            }
        }

        variableDeclaration = new VariableDefinition(
            attributes,
            modifiers,
            possibleType,
            new IdentifierExpression(possibleVariableName, File),
            initialValue,
            File
        );
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

        Diagnostic? error = null;

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, keyword.Position.After(), "(");
            error ??= Diagnostic.Error($"Expected `(` after `{keyword}`", bracketStart, File);
        }

        Statement? initialization;
        Expression? condition;
        Statement? step;
        Position lastPosition = bracketStart.Position;

        if (ExpectOperator(";", out Token? semicolon1))
        {
            initialization = null;
            lastPosition = semicolon1.Position;
        }
        else
        {
            if (!ExpectStatementUnchecked(out initialization))
            {
                initialization = new MissingStatement(lastPosition.After(), File);
                error ??= (Diagnostic.Error($"Expected a statement or `;`", initialization));
            }

            SetStatementThings(initialization);
            lastPosition = initialization.Position;

            if (!ExpectOperator(";", out semicolon1))
            {
                if (error is null) Diagnostics.Add(Diagnostic.Warning($"Expected `;`", lastPosition.After(), File));
            }
            else
            {
                initialization.Semicolon = semicolon1;
                lastPosition = semicolon1.Position;
            }
        }

        if (ExpectOperator(";", out Token? semicolon2))
        {
            condition = null;
            lastPosition = semicolon2.Position;
        }
        else
        {
            if (!ExpectExpression(out condition))
            {
                condition = new MissingExpression(lastPosition.After(), File);
                error ??= (Diagnostic.Error($"Expected a condition or `;`", condition));
            }

            lastPosition = condition.Position;

            if (!ExpectOperator(";", out semicolon2))
            {
                if (error is null) Diagnostics.Add(Diagnostic.Warning($"Expected `;`", lastPosition.After(), File));
            }
            else
            {
                condition.Semicolon = semicolon2;
                lastPosition = semicolon2.Position;
            }
        }

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            step = null;
            lastPosition = bracketEnd.Position;
        }
        else
        {
            if (!ExpectStatementUnchecked(out step))
            {
                step = new MissingStatement(lastPosition.After(), File);
                error ??= (Diagnostic.Error($"Expected a statement or `)`", step));
            }

            SetStatementThings(step);
            lastPosition = step.Position;

            if (!ExpectOperator(")", out bracketEnd))
            {
                error ??= (Diagnostic.Error($"Expected `)`", step.Position.After(), File));
            }
            else
            {
                lastPosition = bracketEnd.Position;
            }
        }

        if (!ExpectBlock(out Block? block))
        {
            block = new MissingBlock(lastPosition.After(), File);
            error ??= (Diagnostic.Error($"Expected block", block));
        }

        Diagnostics.Add(error);
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
        Diagnostic? error = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, keyword.Position.After(), "(");
            error ??= Diagnostic.Error($"Expected `(`", bracketStart, File);
        }

        if (!ExpectExpression(out Expression? condition))
        {
            condition = new MissingExpression(bracketStart.Position.After(), File);
            error ??= Diagnostic.Error($"Expected condition after `{bracketStart}`", condition);
        }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, condition.Position.After(), ")");
            error ??= Diagnostic.Error($"Expected `)`", bracketEnd, File);
        }

        if (!ExpectStatement(out Statement? block))
        {
            block = new MissingStatement(bracketEnd.Position.After(), File);
            error ??= Diagnostic.Error($"Expected a statement", block);
        }

        Diagnostics.Add(error);
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

        Diagnostic? error = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, ifKeyword.Position.After(), "(");
            error ??= Diagnostic.Error($"Expected a `(`", bracketStart, File);
        }

        if (!ExpectExpression(out Expression? condition))
        {
            condition = new MissingExpression(bracketStart.Position.After(), File);
            error ??= Diagnostic.Error($"Expected a condition", condition);
        }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, condition.Position.After(), ")");
            error ??= Diagnostic.Error($"Expected a `)`", bracketEnd, File);
        }

        if (!ExpectStatement(out Statement? ifBlock))
        {
            ifBlock = new MissingBlock(bracketEnd.Position.After(), File);
            error ??= Diagnostic.Error($"Expected a statement", ifBlock);
        }

        ElseBranchStatement? elseBranch = null;

        if (ExpectIdentifier(StatementKeywords.Else, out Token? elseKeyword))
        {
            elseKeyword.AnalyzedType = TokenAnalyzedType.Statement;

            if (!ExpectStatement(out Statement? elseBlock))
            {
                elseBlock = new MissingBlock(elseKeyword.Position.After(), File);
                error ??= Diagnostic.Error($"Expected a statement", elseBlock);
            }

            elseBranch = new ElseBranchStatement(elseKeyword, elseBlock, File);
        }

        Diagnostics.Add(error);
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
        { throw new SyntaxException($"Expected value after unary prefix operator `{unaryPrefixOperator}`", unaryPrefixOperator.Position.After(), File); }

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

        if (!ExpectArgument(out ArgumentExpression? leftStatement, GeneralStatementModifiers)) return false;

        while (true)
        {
            if (!ExpectOperator(BinaryOperators, out Token? binaryOperator)) break;

            if (!ExpectArgument(out ArgumentExpression? rightStatement, GeneralStatementModifiers))
            {
                if (!ExpectUnaryOperatorCall(out UnaryOperatorCallExpression? rightUnaryOperatorCall))
                {
                    rightStatement = new MissingArgumentExpression(binaryOperator.Position.After(), File);
                    Diagnostics.Add(Diagnostic.Error($"Expected value after binary operator `{binaryOperator}`", rightStatement));
                }
                else
                {
                    rightStatement = ArgumentExpression.Wrap(rightUnaryOperatorCall);
                }
            }

            binaryOperator.AnalyzedType = TokenAnalyzedType.MathOperator;

            int rightSidePrecedence = OperatorPrecedence(binaryOperator.Content);

            BinaryOperatorCallExpression? rightmostStatement = FindRightmostStatement(leftStatement, rightSidePrecedence);
            if (rightmostStatement != null)
            {
                rightmostStatement.Right = ArgumentExpression.Wrap(new BinaryOperatorCallExpression(binaryOperator, rightmostStatement.Right, rightStatement, File));
            }
            else
            {
                leftStatement = ArgumentExpression.Wrap(new BinaryOperatorCallExpression(binaryOperator, leftStatement, rightStatement, File));
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

        @operator.AnalyzedType = TokenAnalyzedType.OtherOperator;

        if (!ExpectExpression(out Expression? valueToAssign))
        {
            valueToAssign = new MissingExpression(@operator.Position.After(), File);
            Diagnostics.Add(Diagnostic.Error("Expected an expression after assignment operator", valueToAssign));
        }

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

        @operator.AnalyzedType = TokenAnalyzedType.MathOperator;

        if (!ExpectExpression(out Expression? valueToAssign))
        {
            valueToAssign = new MissingExpression(@operator.Position.After(), File);
            Diagnostics.Add(Diagnostic.Error("Expected an expression after compound assignment operator", valueToAssign));
        }

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

        if (!ExpectOperator(IncrementDecrementOperators, out Token? @operator))
        {
            savepoint.Restore();
            return false;
        }

        @operator.AnalyzedType = TokenAnalyzedType.MathOperator;

        shortOperatorCall = new ShortOperatorCall(@operator, expression, File);
        return true;
    }

    bool ExpectArgument([NotNullWhen(true)] out ArgumentExpression? argumentExpression, ImmutableArray<string> validModifiers)
    {
        if (ExpectIdentifier(out Token? modifier, validModifiers))
        {
            modifier.AnalyzedType = TokenAnalyzedType.Keyword;

            if (!ExpectOneValue(out Expression? value))
            { throw new SyntaxException($"Expected one value after modifier `{modifier}`", modifier.Position.After(), File); }

            argumentExpression = new ArgumentExpression(modifier, value, File);
            return true;
        }

        if (ExpectExpression(out Expression? simpleParameter))
        {
            argumentExpression = ArgumentExpression.Wrap(simpleParameter);
            return true;
        }

        argumentExpression = null;
        return false;
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

    static int OperatorPrecedence(string @operator) =>
        LanguageOperators.Precedencies.TryGetValue(@operator, out int precedence)
        ? precedence
        : throw new InternalExceptionWithoutContext($"Precedence for operator `{@operator}` not found");

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

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            argumentList = new ArgumentListExpression(ImmutableArray<ArgumentExpression>.Empty, ImmutableArray<Token>.Empty, new TokenPair(bracketStart, bracketEnd), File);
            return true;
        }

        ImmutableArray<ArgumentExpression>.Builder arguments = ImmutableArray.CreateBuilder<ArgumentExpression>();
        ImmutableArray<Token>.Builder commas = ImmutableArray.CreateBuilder<Token>();

        EndlessCheck endlessSafe = new();
        Position lastPosition = bracketStart.Position;

        while (true)
        {
            if (!ExpectArgument(out ArgumentExpression? argument, ArgumentModifiers))
            {
                argument = new MissingArgumentExpression(lastPosition.After(), File);
                Diagnostics.Add(Diagnostic.Error($"Expected expression as an argument", argument));
            }

            arguments.Add(argument);

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(",", out Token? comma))
            {
                throw new SyntaxException($"Expected `,` or `)`", argument.Location.After());
                Diagnostics.Add(Diagnostic.Error($"Expected `,` or `)`", argument.Location.After()));
                savepoint.Restore();
                return false;
            }
            commas.Add(comma);

            lastPosition = comma.Position;

            endlessSafe.Step();
        }

        argumentList = new ArgumentListExpression(arguments.DrainToImmutable(), commas.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectKeywordCall(string name, int parameterCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall)
        => ExpectKeywordCall(name, parameterCount, parameterCount, out keywordCall);
    bool ExpectKeywordCall(string name, int minArgumentCount, int maxArgumentCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall)
    {
        ParseRestorePoint savepoint = SavePoint();
        keywordCall = null;

        if (!ExpectIdentifier(out Token? keyword))
        {
            savepoint.Restore();
            return false;
        }

        if (keyword.Content != name)
        {
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        ImmutableArray<Expression>.Builder arguments = ImmutableArray.CreateBuilder<Expression>();

        EndlessCheck endlessSafe = new();
        while (arguments.Count < maxArgumentCount)
        {
            endlessSafe.Step();

            if (!ExpectExpression(out Expression? argument)) break;

            arguments.Add(argument);
        }

        keywordCall = new(keyword, arguments.DrainToImmutable(), File);

        if (minArgumentCount == maxArgumentCount)
        {
            if (keywordCall.Arguments.Length != minArgumentCount)
            {
                Diagnostics.Add(Diagnostic.Error($"Keyword-call `{keyword}` requires {minArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File));
            }
        }
        else
        {
            if (keywordCall.Arguments.Length < minArgumentCount)
            { Diagnostics.Add(Diagnostic.Error($"Keyword-call `{keyword}` requires minimum {minArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File)); }

            if (keywordCall.Arguments.Length > maxArgumentCount)
            { Diagnostics.Add(Diagnostic.Error($"Keyword-call `{keyword}` requires maximum {maxArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File)); }
        }

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

        List<LiteralExpression> parameters = new();
        Position lastPosition = attributeT.Position;
        if (ExpectOperator("(", out Token? bracketParametersStart))
        {
            lastPosition = bracketParametersStart.Position;

            if (!ExpectOperator(")", out Token? bracketParametersEnd))
            {
                EndlessCheck endlessSafe = new();

                while (true)
                {
                    if (!ExpectLiteral(out LiteralExpression? argument))
                    {
                        argument = new MissingLiteral(lastPosition.After(), File);
                        Diagnostics.Add(Diagnostic.Error($"Expected literal as an argument", argument));
                    }

                    parameters.Add(argument);

                    if (ExpectOperator(")", out bracketParametersEnd))
                    { break; }

                    if (!ExpectOperator(",", out Token? comma))
                    { throw new SyntaxException($"Expected `,` or `)`", argument.Location.After()); }

                    lastPosition = comma.Position;

                    endlessSafe.Step();
                }
            }

            lastPosition = bracketParametersEnd.Position;
        }

        if (!ExpectOperator("]", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, lastPosition.After(), "]");
            throw new SyntaxException("Expected `]`", bracketEnd, File);
        }

        attribute = new AttributeUsage(attributeT, parameters.ToImmutableArray(), new TokenPair(bracketStart, bracketEnd), File);
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

    void CheckModifiers(IEnumerable<Token> modifiers, ImmutableArray<string> validModifiers)
    {
        foreach (Token modifier in modifiers)
        {
            if (!validModifiers.Contains(modifier.Content))
            { Diagnostics.Add(Diagnostic.Error($"Modifier `{modifier}` is not valid in the current context", modifier, File)); }
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
               TokenType.Whitespace or
               TokenType.LineBreak or
               TokenType.Comment or
               TokenType.CommentMultiline or
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
                    error = Diagnostic.Error($"Type `{TypeKeywords.Any}` is not valid in the current context", possibleType, File);
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
