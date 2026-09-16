// 让 `init` 访问器在 .NET Framework 4.8 下也能编译。
// .NET 10 已内置该类型，故此 polyfill 仅在 4.8 目标下生效，避免重复定义。
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
