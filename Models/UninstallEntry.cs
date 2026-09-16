using Microsoft.Win32;
using 添加删除程序管理;

namespace 添加删除程序管理.Models;

/// <summary>
/// 条目所在的注册表架构（决定读取视图与所需权限）。
/// 替代 .NET Framework 4.0+ 的 Microsoft.Win32.RegistryView，以便兼容 3.5.1。
/// </summary>
public enum RegView
{
    /// <summary>默认视图（当前用户 HKCU，不区分位数）。</summary>
    Default,
    /// <summary>64 位视图（KEY_WOW64_64KEY）。</summary>
    Registry64,
    /// <summary>32 位（WOW6432Node）视图（KEY_WOW64_32KEY）。</summary>
    Registry32
}

/// <summary>
/// 条目所在的注册表架构（决定读取视图与所需权限）。
/// </summary>
public enum ArchitectureKind
{
    /// <summary>64 位系统下的原生（64 位）程序列表：HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall</summary>
    Native64,
    /// <summary>64 位系统下的 32 位（WOW6432Node）程序列表</summary>
    Wow32,
    /// <summary>当前用户（HKCU）下的程序列表</summary>
    User
}

/// <summary>
/// 残留（孤儿）判定状态。
/// </summary>
public enum OrphanStatus
{
    /// <summary>无法判定（例如 MSI 安装包，或不包含可校验路径）</summary>
    Unknown,
    /// <summary>卸载目标 / 安装目录仍然存在，视为正常</summary>
    Ok,
    /// <summary>卸载目标与安装目录均已不存在，很可能是卸载后的残留项</summary>
    Orphaned
}

/// <summary>
/// 表示“添加 / 删除程序”列表中的一个条目。
/// 所有属性均在加载时从注册表读取，不做任何写回。
/// </summary>
public sealed class UninstallEntry
{
    // 以下三项用于之后重新打开并操作注册表项（删除 / 导出）。
    public RegistryHive Hive { get; init; }
    public RegView View { get; init; }
    public string KeyName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
    public string DisplayVersion { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string InstallDate { get; init; } = string.Empty;
    public long EstimatedSizeBytes { get; set; }
    public string InstallLocation { get; init; } = string.Empty;
    public string UninstallString { get; init; } = string.Empty;
    public string QuietUninstallString { get; init; } = string.Empty;
    public string DisplayIcon { get; init; } = string.Empty;
    public string Comments { get; init; } = string.Empty;
    public string HelpLink { get; init; } = string.Empty;
    public string UrlInfoAbout { get; init; } = string.Empty;
    public string UrlUpdateInfo { get; init; } = string.Empty;
    public string ReleaseType { get; init; } = string.Empty;

    public bool IsSystemComponent { get; init; }
    public bool IsWindowsInstaller { get; init; }
    public bool NoRemove { get; init; }
    public bool NoModify { get; init; }
    public bool NoRepair { get; init; }
    public bool IsUpdate { get; set; }

    public ArchitectureKind Architecture { get; init; }

    /// <summary>残留判定结果（加载时计算）。</summary>
    public OrphanStatus OrphanState { get; set; } = OrphanStatus.Unknown;

    /// <summary>用于显示的名称；无 DisplayName 时回退为注册表项名。</summary>
    public string DisplayNameOrFallback => StrUtil.IsNullOrWhiteSpace(DisplayName)
        ? $"（无名称：{KeyName}）"
        : DisplayName;

    public string ArchitectureText => Architecture switch
    {
        ArchitectureKind.Native64 => "64 位（系统）",
        ArchitectureKind.Wow32 => "32 位（系统）",
        _ => "当前用户"
    };

    public string OrphanText => OrphanState switch
    {
        OrphanStatus.Ok => "正常",
        OrphanStatus.Orphaned => "可能残留",
        _ => "未知"
    };

    /// <summary>便于阅读的注册表路径（注意 32 位视图会显示 WOW6432Node）。</summary>
    public string FriendlyPath
    {
        get
        {
            string root = Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
            string rel = View == RegView.Registry32
                ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            return $@"{root}\{rel}\{KeyName}";
        }
    }

    /// <summary>估算大小的人类可读文本（EstimatedSize 单位为 KB）。</summary>
    public string SizeText
    {
        get
        {
            if (EstimatedSizeBytes <= 0) return string.Empty;
            double v = EstimatedSizeBytes;
            if (v >= 1L << 30) return $"{v / (1L << 30):F2} GB";
            if (v >= 1 << 20) return $"{v / (1 << 20):F1} MB";
            return $"{v / 1024:F0} KB";
        }
    }
}
