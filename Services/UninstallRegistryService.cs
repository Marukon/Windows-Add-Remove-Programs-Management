using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using 添加删除程序管理.Models;

namespace 添加删除程序管理.Services;

/// <summary>
/// 负责读取、删除、导出与启动“添加 / 删除程序”注册表项。
/// 所有注册表操作均做了异常防护，兼容 Windows 7 (3.5.1) ~ Windows 11。
/// 为兼容 .NET Framework 3.5.1，注册表访问使用 RegistryPInvoke（非 4.0 的 RegistryView）。
/// </summary>
internal static class UninstallRegistryService
{
    internal const string UninstallRelPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly HashSet<string> UpdateReleaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Update", "Hotfix", "Security Update", "SecurityUpdate", "ServicePack", "UpdateRollup", "Update Pack"
    };

    /// <summary>
    /// 读取全部条目（系统 64 位、系统 32 位、当前用户），并计算残留状态。
    /// 调用方应在后台线程执行本方法，避免界面卡顿。
    /// </summary>
    public static List<UninstallEntry> LoadAll()
    {
        var result = new List<UninstallEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Enumerate(RegistryHive.LocalMachine, RegView.Registry64, ArchitectureKind.Native64, result, seen);

        // 32 位视图仅在 64 位系统上才有意义；在 32 位系统上枚举会重复，故跳过。
        if (RegistryPInvoke.Is64BitOs())
            Enumerate(RegistryHive.LocalMachine, RegView.Registry32, ArchitectureKind.Wow32, result, seen);

        Enumerate(RegistryHive.CurrentUser, RegView.Default, ArchitectureKind.User, result, seen);

        foreach (var e in result)
            e.OrphanState = DetectOrphan(e);

        result.Sort((a, b) => string.Compare(a.DisplayNameOrFallback, b.DisplayNameOrFallback, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    private static void Enumerate(RegistryHive hive, RegView view, ArchitectureKind arch,
        List<UninstallEntry> outList, HashSet<string> seen)
    {
        IntPtr baseKey = RegistryPInvoke.OpenKey(hive, view, UninstallRelPath, writable: false);
        if (baseKey == IntPtr.Zero) return;

        try
        {
            var subNames = RegistryPInvoke.EnumSubKeyNames(baseKey);
            foreach (var name in subNames)
            {
                if (string.IsNullOrEmpty(name)) continue;

                // 防御性去重（理论上各视图不重叠，但稳妥起见）。
                string identity = hive + "|" + view + "|" + name;
                if (!seen.Add(identity)) continue;

                IntPtr sub = RegistryPInvoke.OpenKey(hive, view, UninstallRelPath + "\\" + name, writable: false);
                if (sub == IntPtr.Zero) continue;

                try
                {
                    var entry = ReadEntry(sub, name, hive, view, arch);
                    if (entry != null) outList.Add(entry);
                }
                catch
                {
                    // 单个子项损坏不影响整体读取
                }
                finally
                {
                    RegistryPInvoke.CloseKey(sub);
                }
            }
        }
        finally
        {
            RegistryPInvoke.CloseKey(baseKey);
        }
    }

    private static UninstallEntry ReadEntry(IntPtr key, string keyName,
        RegistryHive hive, RegView view, ArchitectureKind arch)
    {
        var e = new UninstallEntry
        {
            Hive = hive,
            View = view,
            KeyName = keyName,
            Architecture = arch,
            DisplayName = GetString(key, "DisplayName"),
            DisplayVersion = GetString(key, "DisplayVersion"),
            Publisher = GetString(key, "Publisher"),
            InstallDate = GetString(key, "InstallDate"),
            InstallLocation = GetString(key, "InstallLocation"),
            UninstallString = GetString(key, "UninstallString"),
            QuietUninstallString = GetString(key, "QuietUninstallString"),
            DisplayIcon = GetString(key, "DisplayIcon"),
            Comments = GetString(key, "Comments"),
            HelpLink = GetString(key, "HelpLink"),
            UrlInfoAbout = GetString(key, "URLInfoAbout"),
            UrlUpdateInfo = GetString(key, "URLUpdateInfo"),
            ReleaseType = GetString(key, "ReleaseType"),
            IsSystemComponent = GetInt(key, "SystemComponent") == 1,
            IsWindowsInstaller = GetInt(key, "WindowsInstaller") == 1,
            NoRemove = GetInt(key, "NoRemove") == 1,
            NoModify = GetInt(key, "NoModify") == 1,
            NoRepair = GetInt(key, "NoRepair") == 1,
        };

        int est = GetInt(key, "EstimatedSize");
        e.EstimatedSizeBytes = est > 0 ? (long)est * 1024 : 0;
        e.IsUpdate = !StrUtil.IsNullOrWhiteSpace(e.ReleaseType) && UpdateReleaseTypes.Contains(e.ReleaseType);
        return e;
    }

    /// <summary>
    /// 尽力判定条目是否为“残留”：
    /// 卸载命令指向的文件、或安装目录均已不存在，则很可能是卸载后残留。
    /// MSI 安装包无法简单校验，标记为“未知”。网络路径（UNC）不检查以避免卡顿。
    /// </summary>
    private static OrphanStatus DetectOrphan(UninstallEntry e)
    {
        if (e.IsWindowsInstaller) return OrphanStatus.Unknown;

        string? target = ResolveExecutable(e.UninstallString);
        bool uninstallOk = false;
        if (target != null && !IsUnc(target))
        {
            try { uninstallOk = File.Exists(target); }
            catch { uninstallOk = false; }
        }

        bool installOk = false;
        if (!StrUtil.IsNullOrWhiteSpace(e.InstallLocation) && !IsUnc(e.InstallLocation))
        {
            try { installOk = Directory.Exists(e.InstallLocation) || File.Exists(e.InstallLocation); }
            catch { installOk = false; }
        }

        if (StrUtil.IsNullOrWhiteSpace(e.UninstallString) && StrUtil.IsNullOrWhiteSpace(e.InstallLocation))
            return OrphanStatus.Unknown;

        return (!uninstallOk && !installOk) ? OrphanStatus.Orphaned : OrphanStatus.Ok;
    }

    private static string? ResolveExecutable(string? commandLine)
    {
        if (StrUtil.IsNullOrWhiteSpace(commandLine)) return null;
        string cmd = commandLine!.Trim();
        if (StrUtil.StartsWithI(cmd, "msiexec")) return null;
        var parts = SplitCommandLine(cmd);
        if (StrUtil.IsNullOrWhiteSpace(parts.Exe)) return null;
        parts.Exe = Environment.ExpandEnvironmentVariables(parts.Exe).Trim('"');
        return parts.Exe;
    }

    /// <summary>将命令行拆分为“可执行文件”与“参数”两部分（用普通类以兼容 .NET Framework 3.5，无 ValueTuple）。</summary>
    internal sealed class CmdParts
    {
        public string Exe = string.Empty;
        public string Args = string.Empty;
    }

    /// <summary>将命令行拆分为“可执行文件”与“参数”两部分。</summary>
    internal static CmdParts SplitCommandLine(string cmd)
    {
        var parts = new CmdParts();
        cmd = (cmd ?? string.Empty).Trim();
        if (cmd.Length == 0) return parts;

        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) { parts.Exe = cmd.Substring(1); return parts; }
            parts.Exe = cmd.Substring(1, end - 1);
            parts.Args = cmd.Substring(end + 1).Trim();
            return parts;
        }

        int space = cmd.IndexOf(' ');
        if (space < 0) { parts.Exe = cmd; return parts; }
        parts.Exe = cmd.Substring(0, space);
        parts.Args = cmd.Substring(space + 1).Trim();
        return parts;
    }

    private static bool IsUnc(string path) => path.Length >= 2 && path[0] == '\\' && path[1] == '\\';

    /// <summary>当前进程是否以管理员身份运行。</summary>
    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 删除条目：先导出 .reg 备份，再删除注册表项。
    /// 删除 HKLM 下的项需要管理员权限，否则会抛出 UnauthorizedAccessException。
    /// </summary>
    public static void DeleteEntry(UninstallEntry entry, string backupDirectory)
    {
        try
        {
            Directory.CreateDirectory(backupDirectory);
            string safe = string.Join("_", entry.KeyName.Split(Path.GetInvalidFileNameChars()));
            string regFile = Path.Combine(backupDirectory, safe + ".reg");
            RegFileExporter.ExportSingle(entry, regFile);
        }
        catch
        {
            // 备份失败不应阻断删除流程
        }

        RegistryPInvoke.DeleteTree(entry.Hive, entry.View, entry.KeyName);
    }

    /// <summary>启动条目的卸载程序。quiet=true 时优先使用静默卸载命令。</summary>
    public static void RunUninstall(UninstallEntry entry, bool quiet)
    {
        string raw = (quiet && !StrUtil.IsNullOrWhiteSpace(entry.QuietUninstallString))
            ? entry.QuietUninstallString
            : entry.UninstallString;

        if (StrUtil.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("该条目没有可用的卸载命令。");

        string cmd = Environment.ExpandEnvironmentVariables(raw.Trim());

        // MSI：将 /I（修复/安装）改为 /X（卸载），确保真正执行卸载。
        if (StrUtil.StartsWithI(cmd, "msiexec"))
        {
            cmd = Regex.Replace(cmd, @"(?i)/I(?=\{)", "/X");
            if (StrUtil.IndexOfI(cmd, "/X") < 0)
                cmd = Regex.Replace(cmd, @"(?i)/I", "/X");
            if (quiet && StrUtil.IndexOfI(cmd, "/qn") < 0 && StrUtil.IndexOfI(cmd, "/quiet") < 0)
                cmd += " /qn";
        }

        var parts = SplitCommandLine(cmd);
        if (StrUtil.IsNullOrWhiteSpace(parts.Exe))
            throw new InvalidOperationException("无法解析卸载命令。");

        var psi = new ProcessStartInfo
        {
            FileName = parts.Exe,
            Arguments = parts.Args,
            UseShellExecute = true,
            ErrorDialog = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("无法启动卸载进程。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("启动卸载失败：" + ex.Message, ex);
        }
    }

    private static string GetString(IntPtr hKey, string name)
    {
        try
        {
            var r = RegistryPInvoke.GetValue(hKey, name);
            return r.Value as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int GetInt(IntPtr hKey, string name)
    {
        try
        {
            var r = RegistryPInvoke.GetValue(hKey, name);
            if (r.Value is int i) return i;
            if (r.Value is string s && int.TryParse(s, out int rr)) return rr;
            return 0;
        }
        catch
        {
            return 0;
        }
    }
}
