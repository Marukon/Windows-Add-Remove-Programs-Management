using System.Globalization;

namespace 添加删除程序管理;

/// <summary>
/// 跨 .NET 版本兼容的字符串辅助方法。
/// .NET Framework 3.5 缺少 IsNullOrWhiteSpace、IndexOf(string, StringComparison)、
/// StartsWith(string, StringComparison) 等重载，这里统一用 CultureInfo.CompareInfo 实现，
/// 在 .NET 10 与 .NET Framework 3.5 下都能编译且行为一致。
/// </summary>
internal static class StrUtil
{
    public static bool IsNullOrWhiteSpace(string? s)
    {
        if (s == null) return true;
        for (int i = 0; i < s.Length; i++)
            if (!char.IsWhiteSpace(s[i]))
                return false;
        return true;
    }

    public static bool ContainsI(string? s, string value) => IndexOfI(s, value) >= 0;

    public static int IndexOfI(string? s, string value)
    {
        if (s == null) return -1;
        return CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, value, CompareOptions.IgnoreCase);
    }

    public static bool StartsWithI(string? s, string value)
    {
        if (s == null) return false;
        return CultureInfo.InvariantCulture.CompareInfo.IsPrefix(s, value, CompareOptions.IgnoreCase);
    }
}
