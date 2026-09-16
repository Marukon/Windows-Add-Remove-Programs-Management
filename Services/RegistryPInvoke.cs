using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using 添加删除程序管理.Models;

namespace 添加删除程序管理.Services;

/// <summary>
/// 对 advapi32.dll 的注册表 P/Invoke 封装。
/// 用于替代 .NET Framework 4.0+ 才提供的 RegistryView / RegistryKey.OpenBaseKey，
/// 以便同一份代码能在 .NET Framework 3.5.1（Windows 7）下编译运行。
/// 所有方法均做了返回码检查，调用方应自行 try/finally 关闭句柄。
/// </summary>
internal static class RegistryPInvoke
{
    // 预定义根键句柄。
    // 注意：必须用 IntPtr(int) 构造，不要写 unchecked((IntPtr)0x80000002)。
    // 在 32 位(x86)下，后者的转换路径会走 IntPtr(long) 构造，
    // 而 long 值 0x80000002 超过 Int32.MaxValue，IntPtr(long) 在 32 位会抛 OverflowException；
    // 且 unchecked 无法抑制该构造器的范围检查。改为先转 int 再构造即可（句柄低位一致）。
    private static readonly IntPtr HKLM = new IntPtr(unchecked((int)0x80000002));
    private static readonly IntPtr HKCU = new IntPtr(unchecked((int)0x80000001));

    // 访问权限
    private const int KEY_READ = 0x20019;
    private const int KEY_ALL_ACCESS = 0xF003F;
    private const int KEY_WOW64_64KEY = 0x0100;
    private const int KEY_WOW64_32KEY = 0x0200;

    private const int ERROR_NO_MORE_ITEMS = 259;

    internal static IntPtr HiveRoot(RegistryHive hive) =>
        hive == RegistryHive.LocalMachine ? HKLM : HKCU;

    internal static int Wow64Flag(RegView view) => view switch
    {
        RegView.Registry64 => KEY_WOW64_64KEY,
        RegView.Registry32 => KEY_WOW64_32KEY,
        _ => 0
    };

    /// <summary>打开注册表项；失败返回 IntPtr.Zero。</summary>
    internal static IntPtr OpenKey(RegistryHive hive, RegView view, string subKey, bool writable)
    {
        uint sam = (uint)((writable ? KEY_ALL_ACCESS : KEY_READ) | Wow64Flag(view));
        IntPtr h;
        int r = RegOpenKeyEx(HiveRoot(hive), subKey, 0, sam, out h);
        return r == 0 ? h : IntPtr.Zero;
    }

    internal static void CloseKey(IntPtr h)
    {
        if (h != IntPtr.Zero) RegCloseKey(h);
    }

    /// <summary>枚举某键下的所有子项名称。</summary>
    internal static List<string> EnumSubKeyNames(IntPtr hKey)
    {
        var names = new List<string>();
        if (hKey == IntPtr.Zero) return names;
        var sb = new StringBuilder(256);
        int index = 0;
        while (true)
        {
            int len = sb.Capacity;
            int r = RegEnumKeyEx(hKey, index, sb, ref len, IntPtr.Zero, null, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) break;
            names.Add(sb.ToString(0, len));
            index++;
            if (index > 100000) break; // 防御性上限，避免异常死循环
        }
        return names;
    }

    /// <summary>枚举某键下的所有值名称。</summary>
    internal static List<string> GetValueNames(IntPtr hKey)
    {
        var names = new List<string>();
        if (hKey == IntPtr.Zero) return names;
        var sb = new StringBuilder(16383);
        int index = 0;
        while (true)
        {
            int len = sb.Capacity;
            int r = RegEnumValue(hKey, index, sb, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) break;
            names.Add(sb.ToString(0, len));
            index++;
            if (index > 100000) break;
        }
        return names;
    }

    /// <summary>读取某个值的结果（值 + 类型）。使用结构体以兼容 .NET Framework 3.5（无 ValueTuple）。</summary>
    internal struct RegValueResult
    {
        public object? Value;
        public RegistryValueKind Kind;
    }

    /// <summary>读取某个值，返回 (值, 类型)。失败返回 (null, None)。</summary>
    internal static RegValueResult GetValue(IntPtr hKey, string name)
    {
        int type = 0;
        int size = 0;
        if (RegQueryValueEx(hKey, name, IntPtr.Zero, out type, null, ref size) != 0)
            return new RegValueResult { Value = null, Kind = RegistryValueKind.Unknown };
        if (size < 0) size = 0;

        var bytes = new byte[size == 0 ? 1 : size];
        if (RegQueryValueEx(hKey, name, IntPtr.Zero, out type, bytes, ref size) != 0)
            return new RegValueResult { Value = null, Kind = RegistryValueKind.Unknown };

        int usable = size > bytes.Length ? bytes.Length : size;
        switch (type)
        {
            case 1: // REG_SZ
            case 2: // REG_EXPAND_SZ
            {
                string s = Encoding.Unicode.GetString(bytes, 0, usable);
                int nul = s.IndexOf('\0');
                if (nul >= 0) s = s.Substring(0, nul);
                return new RegValueResult { Value = s, Kind = (RegistryValueKind)type };
            }
            case 4: // REG_DWORD
                return new RegValueResult { Value = BitConverter.ToInt32(bytes, 0), Kind = RegistryValueKind.DWord };
            case 11: // REG_QWORD
                return new RegValueResult { Value = BitConverter.ToInt64(bytes, 0), Kind = RegistryValueKind.QWord };
            case 7: // REG_MULTI_SZ
            {
                string raw = Encoding.Unicode.GetString(bytes, 0, usable);
                string[] parts = raw.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                return new RegValueResult { Value = parts, Kind = RegistryValueKind.MultiString };
            }
            default: // REG_BINARY (3) 及其它
                return new RegValueResult { Value = bytes, Kind = (RegistryValueKind)type };
        }
    }

    /// <summary>
    /// 删除 Uninstall 下的某个子项（含其全部子项）。
    /// 需要注册表项的“可写”访问权限（删除 HKLM 项需管理员）。
    /// </summary>
    internal static void DeleteTree(RegistryHive hive, RegView view, string subKey)
    {
        uint sam = (uint)(KEY_ALL_ACCESS | Wow64Flag(view));
        IntPtr h;
        if (RegOpenKeyEx(HiveRoot(hive), UninstallRegistryService.UninstallRelPath, 0, sam, out h) != 0)
            throw new UnauthorizedAccessException("无法打开注册表项进行写入（可能需要以管理员身份运行）。");
        try
        {
            int r = RegDeleteTree(h, subKey);
            if (r != 0)
                throw new InvalidOperationException("删除注册表项失败（错误码 " + r + "）。");
        }
        finally
        {
            RegCloseKey(h);
        }
    }

    /// <summary>判断当前操作系统是否为 64 位（兼容 3.5，无 Environment.Is64BitOperatingSystem）。</summary>
    internal static bool Is64BitOs()
    {
        if (IntPtr.Size == 8) return true; // 64 位进程必然运行在 64 位系统
        bool wow;
        return IsWow64Process(GetCurrentProcess(), out wow) && wow;
    }

    // ===================== P/Invoke =====================

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW", SetLastError = true)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, uint samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumKeyExW", SetLastError = true)]
    private static extern int RegEnumKeyEx(IntPtr hKey, int dwIndex, StringBuilder lpName, ref int lpcbName,
        IntPtr lpReserved, StringBuilder? lpClass, IntPtr lpcbClass, IntPtr lpftLastWriteTime);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW", SetLastError = true)]
    private static extern int RegEnumValue(IntPtr hKey, int dwIndex, StringBuilder lpValueName, ref int lpcbValueName,
        IntPtr lpReserved, IntPtr lpType, IntPtr lpData, IntPtr lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW", SetLastError = true)]
    private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, out int lpType,
        [Out] byte[]? lpData, ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteTreeW", SetLastError = true)]
    private static extern int RegDeleteTree(IntPtr hKey, string lpSubKey);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
