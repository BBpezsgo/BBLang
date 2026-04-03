using System.Reflection;
using System.Reflection.Emit;
using LanguageCore.IL.Reflection;

namespace LanguageCore.IL.Generator;

public struct ILGeneratorResult
{
    public DynamicMethod EntryPoint;
    public Func<int> EntryPointDelegate;
    public ImmutableArray<DynamicMethod> Methods;
    public ModuleBuilder Module;

    public readonly void Stringify(StringBuilder builder, int indentation = 0)
    {
        ImmutableArray<DynamicMethod> methods = Methods;
        foreach (Type type in Module.GetTypes()) Stringify(builder, indentation, type);
        foreach (MethodInfo method in Module.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Where(v => !methods.Any(w => v.Equals(w)))) Stringify(builder, indentation, method);
        foreach (FieldInfo field in Module.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)) Stringify(builder, indentation, field);
        foreach (DynamicMethod method in methods) Stringify(builder, indentation, method);
        Stringify(builder, indentation, EntryPoint);
    }

    static void StringifyValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case string v:
                builder.Append('"');
                builder.Append(v.Escape());
                builder.Append('"');
                break;
            case Array array:
                Type type = array.GetType();
                Type elementType = type.GetElementType()!;
                int length = array.GetLength(0);
                builder.Append($"new {elementType}[{length}]");
                if (length > 0)
                {
                    builder.Append(" { ");
                    for (int i = 0; i < length; i++)
                    {
                        if (i > 0) builder.Append(", ");
                        StringifyValue(builder, array.GetValue(i));
                    }
                    builder.Append(" }");
                }
                break;
            default:
                builder.Append(value);
                break;
        }
    }

    static void Stringify(StringBuilder builder, int indentation, CustomAttributeData attribute)
    {
        builder.Indent(indentation);
        builder.Append('[');
        string name = attribute.AttributeType.ToString();
        if (name.EndsWith("Attribute")) name = name[..^"Attribute".Length];
        builder.Append(name);
        builder.Append('(');
        bool w = false;
        foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments)
        {
            if (w) builder.Append(", ");
            else w = true;
            StringifyValue(builder, argument.Value);
        }
        foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
        {
            if (w) builder.Append(", ");
            else w = true;
            builder.Append(argument.MemberName);
            builder.Append(": ");
            StringifyValue(builder, argument.TypedValue.Value);
        }
        builder.Append(')');
        builder.Append(']');
        builder.AppendLine();
    }

    static void Stringify(StringBuilder builder, int indentation, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        foreach (CustomAttributeData item in type.GetCustomAttributesData())
        {
            Stringify(builder, indentation, item);
        }

        builder.Indent(indentation);
        builder.Append($"{type.Attributes & TypeAttributes.VisibilityMask} ");

        foreach (TypeAttributes attribute in CompatibilityUtils.GetEnumValues<TypeAttributes>()
            .Where(v => (v & TypeAttributes.VisibilityMask) == 0 && v != 0))
        {
            if (type.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(type.Name);
        builder.AppendLine();

        builder.Indent(indentation);
        builder.Append('{');
        builder.AppendLine();

        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            switch (member)
            {
                case FieldInfo field:
                    Stringify(builder, indentation + 1, field);
                    break;
                case MethodInfo method:
                    Stringify(builder, indentation + 1, method);
                    break;
                case ConstructorInfo constructor:
                    Stringify(builder, indentation + 1, constructor);
                    break;
                default:
                    throw new NotImplementedException(member.GetType().ToString());
            }
        }

        builder.Indent(indentation);
        builder.Append('}');
        builder.AppendLine();
        builder.AppendLine();
    }

    static void Stringify(StringBuilder builder, int indentation, FieldInfo field)
    {
        foreach (CustomAttributeData item in field.GetCustomAttributesData())
        {
            Stringify(builder, indentation, item);
        }

        builder.Indent(indentation);
        builder.Append($"{field.Attributes & FieldAttributes.FieldAccessMask} ");

        foreach (FieldAttributes attribute in CompatibilityUtils.GetEnumValues<FieldAttributes>()
            .Where(v => (v & FieldAttributes.FieldAccessMask) == 0 && v != 0))
        {
            if (field.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(field.FieldType.ToString());
        builder.Append(' ');
        builder.Append(field.Name);

        object? value = field.IsStatic ? field.GetValue(null) : null;
        if (value is not null)
        {
            builder.Append(" = ");
            StringifyValue(builder, value);
        }

        builder.Append(';');
        builder.AppendLine();
    }

    static void StringifySignature(StringBuilder builder, int indentation, MethodInfo method)
    {
        try
        {
            foreach (CustomAttributeData item in method.GetCustomAttributesData())
            {
                Stringify(builder, indentation, item);
            }
        }
        catch
        {

        }

        builder.Indent(indentation);
        builder.Append($"{method.Attributes & MethodAttributes.MemberAccessMask} ");

        foreach (MethodAttributes attribute in CompatibilityUtils.GetEnumValues<MethodAttributes>()
            .Where(v => (v & MethodAttributes.MemberAccessMask) == 0 && v != 0))
        {
            if (method.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(method.ReturnType.ToString());
        builder.Append(' ');
        builder.Append(method.Name);
        builder.Append('(');
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            ParameterInfo parameter = parameters[i];
            foreach (ParameterAttributes attribute in CompatibilityUtils.GetEnumValues<ParameterAttributes>().Where(v => v is not ParameterAttributes.None))
            {
                if (parameter.Attributes.HasFlag(attribute))
                {
                    builder.Append($"{attribute} ");
                }
            }
            builder.Append(parameter.ParameterType.ToString());
            builder.Append(' ');
            builder.Append(parameter.Name ?? $"p{i}");
        }
        builder.Append(')');
    }

    static void StringifySignature(StringBuilder builder, int indentation, ConstructorInfo method)
    {
        try
        {
            foreach (CustomAttributeData item in method.GetCustomAttributesData())
            {
                Stringify(builder, indentation, item);
            }
        }
        catch
        {

        }

        builder.Indent(indentation);
        builder.Append($"{method.Attributes & MethodAttributes.MemberAccessMask} ");

        foreach (MethodAttributes attribute in CompatibilityUtils.GetEnumValues<MethodAttributes>()
            .Where(v => (v & MethodAttributes.MemberAccessMask) == 0 && v != 0))
        {
            if (method.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(method.Name);
        builder.Append('(');
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            ParameterInfo parameter = parameters[i];
            foreach (ParameterAttributes attribute in CompatibilityUtils.GetEnumValues<ParameterAttributes>().Where(v => v is not ParameterAttributes.None))
            {
                if (parameter.Attributes.HasFlag(attribute))
                {
                    builder.Append($"{attribute} ");
                }
            }
            builder.Append(parameter.ParameterType.ToString());
            builder.Append(' ');
            builder.Append(parameter.Name ?? $"p{i}");
        }
        builder.Append(')');
    }

    static void Stringify(StringBuilder builder, int indentation, MethodInfo method)
    {
        if (method is DynamicMethod dynamicMethod)
        {
            Stringify(builder, indentation, dynamicMethod);
        }
        else
        {
            StringifySignature(builder, indentation, method);
            builder.Append(';');
            builder.AppendLine();
            builder.AppendLine();
        }
    }

    static void Stringify(StringBuilder builder, int indentation, ConstructorInfo constructor)
    {
        if (constructor is ConstructorBuilder constructorBuilder)
        {
            Stringify(builder, indentation, constructorBuilder);
        }
        else
        {
            StringifySignature(builder, indentation, constructor);
            builder.Append(';');
            builder.AppendLine();
            builder.AppendLine();
        }
    }

    static void Stringify(StringBuilder builder, int indentation, ConstructorBuilder constructor)
    {
        StringifySignature(builder, indentation, constructor);
        builder.AppendLine();

        builder.Indent(indentation);
        builder.Append('{');
        builder.AppendLine();

        MethodBody? body = null;
        byte[]? code = DynamicMethodILProvider.GetByteArray(constructor);

        try
        {
            body = constructor.GetMethodBody();
        }
        catch
        {
        }

        if (body is not null)
        {
            foreach (LocalVariableInfo localVariable in body.LocalVariables)
            {
                builder.Indent(indentation + 1);
                builder.Append(localVariable.LocalType);
                builder.Append(' ');
                builder.Append($"l{localVariable.LocalIndex}");
                builder.AppendLine();
            }
        }

        if (code is null)
        {
            builder.Indent(indentation + 1);
            builder.AppendLine("// IL isn't avaliable");
        }
        else
        {
            foreach (ILInstruction instruction in new ILReader(code, new DynamicScopeTokenResolver(constructor)))
            {
                builder.Indent(indentation + 1);
                builder.Append(UnshortenInstruction(instruction));
                builder.AppendLine();
            }
        }

        builder.Indent(indentation);
        builder.Append('}');
        builder.AppendLine();
        builder.AppendLine();
    }

    static void Stringify(StringBuilder builder, int indentation, DynamicMethod method)
    {
        StringifySignature(builder, indentation, method);
        builder.AppendLine();

        builder.Indent(indentation);
        builder.Append('{');
        builder.AppendLine();

        MethodBody? body = null;
        byte[]? code = DynamicMethodILProvider.GetByteArray(method);

        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
        }

        if (body is not null)
        {
            foreach (LocalVariableInfo localVariable in body.LocalVariables)
            {
                builder.Indent(indentation + 1);
                builder.Append(localVariable.LocalType);
                builder.Append(' ');
                builder.Append($"l{localVariable.LocalIndex}");
                builder.AppendLine();
            }
        }

        if (code is null)
        {
            builder.Indent(indentation + 1);
            builder.AppendLine("// IL isn't avaliable");
        }
        else
        {
            foreach (ILInstruction instruction in new ILReader(code, new DynamicScopeTokenResolver(method)))
            {
                builder.Indent(indentation + 1);
                builder.Append(UnshortenInstruction(instruction));
                builder.AppendLine();
            }
        }

        builder.Indent(indentation);
        builder.Append('}');
        builder.AppendLine();
        builder.AppendLine();
    }

    static ILInstruction UnshortenInstruction(ILInstruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldarg_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 0);
        if (instruction.OpCode == OpCodes.Ldarg_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 1);
        if (instruction.OpCode == OpCodes.Ldarg_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 2);
        if (instruction.OpCode == OpCodes.Ldarg_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 3);

        if (instruction.OpCode == OpCodes.Ldloc_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 0);
        if (instruction.OpCode == OpCodes.Ldloc_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 1);
        if (instruction.OpCode == OpCodes.Ldloc_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 2);
        if (instruction.OpCode == OpCodes.Ldloc_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 3);

        if (instruction.OpCode == OpCodes.Stloc_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 0);
        if (instruction.OpCode == OpCodes.Stloc_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 1);
        if (instruction.OpCode == OpCodes.Stloc_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 2);
        if (instruction.OpCode == OpCodes.Stloc_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 3);

        if (instruction.OpCode == OpCodes.Ldc_I4_0) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 0);
        if (instruction.OpCode == OpCodes.Ldc_I4_1) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 1);
        if (instruction.OpCode == OpCodes.Ldc_I4_2) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 2);
        if (instruction.OpCode == OpCodes.Ldc_I4_3) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 3);
        if (instruction.OpCode == OpCodes.Ldc_I4_4) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 4);
        if (instruction.OpCode == OpCodes.Ldc_I4_5) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 5);
        if (instruction.OpCode == OpCodes.Ldc_I4_6) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 6);
        if (instruction.OpCode == OpCodes.Ldc_I4_7) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 7);
        if (instruction.OpCode == OpCodes.Ldc_I4_8) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 8);
        if (instruction.OpCode == OpCodes.Ldc_I4_M1) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, -1);

        if (instruction is ShortInlineVarInstruction s0)
        {
            if (instruction.OpCode == OpCodes.Ldarg_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldloc_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Stloc_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Starg_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Starg, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldarga_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarga, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldloca_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloca, s0.Ordinal);
        }

        if (instruction is ShortInlineIInstruction s1)
        {
            if (instruction.OpCode == OpCodes.Ldc_I4_S) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, s1.Byte);
        }

        return instruction;
    }
}
