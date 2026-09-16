// 让可空引用类型（Nullable Reference Types）属性在 .NET Framework（3.5/4.8）下也能编译。
// .NET 10 已内置这些类型，故此文件仅在 Framework 目标下生效，避免重复定义。
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property |
                    AttributeTargets.ReturnValue | AttributeTargets.Class | AttributeTargets.Struct |
                    AttributeTargets.Interface, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte b) { NullableFlags = new[] { b }; }
        public NullableAttribute(byte[] b) { NullableFlags = b; }
        public byte[] NullableFlags { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
                    AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte b) { Flag = b; }
        public byte Flag { get; }
    }
}
#endif
