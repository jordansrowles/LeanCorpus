// .NET 10 compiler support for the preview union and closed-type syntax.
// Keep these definitions internal and include them only on net10.0.
// Contract: dotnet/runtime System.Runtime.CompilerServices IUnion,
// UnionAttribute and IsClosedTypeAttribute.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class UnionAttribute : Attribute;

internal interface IUnion
{
    object? Value { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class IsClosedTypeAttribute : Attribute
{
    public Type[] DerivedTypes { get; set; } = [];
}
