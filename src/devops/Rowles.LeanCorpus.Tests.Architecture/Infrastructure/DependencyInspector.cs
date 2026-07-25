using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

internal static class DependencyInspector
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodes = BuildOpCodes();

    internal static IReadOnlyList<string> FindViolations(
        Assembly assembly,
        Func<Type, bool> sourcePredicate,
        Func<Type, bool> dependencyPredicate)
    {
        var failures = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes().Where(sourcePredicate))
        {
            if (GetDependencies(type).Any(dependencyPredicate))
                failures.Add(GetOwningType(type).FullName ?? GetOwningType(type).Name);
        }

        return failures.ToArray();
    }

    internal static bool IsExactType(Type candidate, Type expected) =>
        Normalise(candidate) == Normalise(expected);

    private static IEnumerable<Type> GetDependencies(Type type)
    {
        foreach (var dependency in Expand(type.BaseType))
            yield return dependency;

        foreach (var @interface in type.GetInterfaces())
        foreach (var dependency in Expand(@interface))
            yield return dependency;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        foreach (var dependency in Expand(field.FieldType))
            yield return dependency;

        foreach (var property in type.GetProperties(flags))
        foreach (var dependency in Expand(property.PropertyType))
            yield return dependency;

        foreach (var @event in type.GetEvents(flags))
        foreach (var dependency in Expand(@event.EventHandlerType))
            yield return dependency;

        foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
            if (method is MethodInfo methodInfo)
            foreach (var dependency in Expand(methodInfo.ReturnType))
                yield return dependency;

            foreach (var parameter in method.GetParameters())
            foreach (var dependency in Expand(parameter.ParameterType))
                yield return dependency;

            foreach (var dependency in ReadMethodBodyDependencies(method))
                yield return dependency;
        }
    }

    private static IEnumerable<Type> ReadMethodBodyDependencies(MethodBase method)
    {
        MethodBody? body;
        try
        {
            body = method.GetMethodBody();
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        byte[]? il = body?.GetILAsByteArray();
        if (il is null)
            yield break;

        int position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xfe)
                value = (short)(0xfe00 | il[position++]);

            if (!OpCodes.TryGetValue(value, out var opCode))
                yield break;

            if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineType or OperandType.InlineTok)
            {
                int token = BitConverter.ToInt32(il, position);
                MemberInfo? member = ResolveMember(method, token);
                foreach (var dependency in ExpandMember(member))
                    yield return dependency;
            }

            position += GetOperandSize(opCode.OperandType, il, position);
        }
    }

    private static MemberInfo? ResolveMember(MethodBase method, int token)
    {
        try
        {
            Type[]? typeArguments = method.DeclaringType?.GetGenericArguments();
            Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            return method.Module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<Type> ExpandMember(MemberInfo? member)
    {
        if (member?.DeclaringType is not null)
        foreach (var dependency in Expand(member.DeclaringType))
            yield return dependency;

        if (member is Type type)
        foreach (var dependency in Expand(type))
            yield return dependency;

        if (member is FieldInfo field)
        foreach (var dependency in Expand(field.FieldType))
            yield return dependency;

        if (member is MethodInfo method)
        foreach (var dependency in Expand(method.ReturnType))
            yield return dependency;
    }

    private static IEnumerable<Type> Expand(Type? type)
    {
        if (type is null)
            yield break;

        Type normalised = Normalise(type);
        yield return normalised;

        if (normalised.IsGenericType)
        foreach (var argument in normalised.GetGenericArguments())
        foreach (var dependency in Expand(argument))
            yield return dependency;
    }

    private static Type Normalise(Type type)
    {
        while (type.HasElementType)
            type = type.GetElementType()!;

        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    private static Type GetOwningType(Type type)
    {
        while (type.DeclaringType is not null &&
               (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || type.Name.StartsWith('<')))
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int position) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
            OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
            OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, position) * 4,
        _ => throw new InvalidOperationException($"Unsupported IL operand type {operandType}."),
    };

    private static IReadOnlyDictionary<short, OpCode> BuildOpCodes() => typeof(System.Reflection.Emit.OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.FieldType == typeof(OpCode))
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static opCode => opCode.Value);
}
