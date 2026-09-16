using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using 添加删除程序管理.Models;

namespace 添加删除程序管理.Services;

/// <summary>
/// 将注册表项序列化为 .reg 文本（UTF-16 LE 带 BOM，与 reg.exe 导出格式兼容）。
/// 完全自包含，不依赖任何外部进程。读取使用 RegistryPInvoke 以兼容 .NET Framework 3.5.1。
/// </summary>
internal static class RegFileExporter
{
    private const string Header = "Windows Registry Editor Version 5.00";

    /// <summary>导出单个条目到指定 .reg 文件。</summary>
    public static void ExportSingle(UninstallEntry entry, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine();
        AppendKey(sb, entry);
        File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
    }

    /// <summary>将多个条目合并导出为一个 .reg 文本。</summary>
    public static string BuildRegFile(IEnumerable<UninstallEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine();
        foreach (var e in entries)
            AppendKey(sb, e);
        return sb.ToString();
    }

    private static void AppendKey(StringBuilder sb, UninstallEntry e)
    {
        sb.AppendLine('[' + GetNativePath(e) + ']');

        IntPtr baseKey = RegistryPInvoke.OpenKey(e.Hive, e.View, UninstallRegistryService.UninstallRelPath, writable: false);
        if (baseKey == IntPtr.Zero)
        {
            sb.AppendLine("; (该项无法读取)");
            sb.AppendLine();
            return;
        }
        try
        {
            IntPtr key = RegistryPInvoke.OpenKey(e.Hive, e.View, UninstallRegistryService.UninstallRelPath + "\\" + e.KeyName, writable: false);
            if (key == IntPtr.Zero)
            {
                sb.AppendLine("; (该项已被删除或无法读取)");
                sb.AppendLine();
                return;
            }
            try
            {
                foreach (var valueName in RegistryPInvoke.GetValueNames(key))
                    AppendValue(sb, key, valueName);
            }
            catch (Exception ex)
            {
                sb.AppendLine("; 导出出错: " + ex.Message);
            }
            finally
            {
                RegistryPInvoke.CloseKey(key);
            }
        }
        finally
        {
            RegistryPInvoke.CloseKey(baseKey);
        }

        sb.AppendLine();
    }

    private static void AppendValue(StringBuilder sb, IntPtr hKey, string valueName)
    {
        string nameToken = valueName.Length == 0
            ? "@"
            : "\"" + valueName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        var r = RegistryPInvoke.GetValue(hKey, valueName);
        var val = r.Value;
        var kind = r.Kind;
        if (val == null) return;

        switch (kind)
        {
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                sb.AppendLine(nameToken + "=\"" + ((string)val).Replace("\"", "\\\"") + "\"");
                break;

            case RegistryValueKind.MultiString:
                var bytes = BuildMultiStringBytes((string[])val);
                sb.AppendLine(nameToken + "=hex(7):" + ToHex(bytes));
                break;

            case RegistryValueKind.DWord:
                sb.AppendLine(nameToken + "=dword:" + ((int)val).ToString("x8"));
                break;

            case RegistryValueKind.QWord:
                sb.AppendLine(nameToken + "=hex(b):" + ToHex(BitConverter.GetBytes((long)val)));
                break;

            case RegistryValueKind.Binary:
            case RegistryValueKind.Unknown:
                sb.AppendLine(nameToken + "=hex:" + ToHex((byte[])val));
                break;

            default:
                if (val is byte[] b)
                    sb.AppendLine(nameToken + "=hex:" + ToHex(b));
                else
                    sb.AppendLine("; 跳过未知类型: " + kind);
                break;
        }
    }

    private static byte[] BuildMultiStringBytes(string[] arr)
    {
        using var ms = new MemoryStream();
        foreach (var s in arr)
        {
            foreach (char c in s)
            {
                ms.WriteByte((byte)(c & 0xFF));
                ms.WriteByte((byte)(c >> 8));
            }
            ms.WriteByte(0);
            ms.WriteByte(0);
        }
        ms.WriteByte(0);
        ms.WriteByte(0);
        return ms.ToArray();
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bytes[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>构造与注册表物理路径一致的本地路径（32 位视图需包含 WOW6432Node）。</summary>
    private static string GetNativePath(UninstallEntry e)
    {
        string root = e.Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
        string rel = e.View == RegView.Registry32
            ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        return root + "\\" + rel + "\\" + e.KeyName;
    }
}
