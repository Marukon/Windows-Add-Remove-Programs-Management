using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace 添加删除程序管理.Services;

/// <summary>
/// 极简 INI 文件读写器。不依赖 Win32 API，兼容 .NET Framework 4.8 与 .NET 10。
/// 键名大小写不敏感；读写均做异常保护，文件损坏或不可写时静默降级。
/// </summary>
public sealed class IniFile
{
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, string>> _data =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public IniFile(string path) => _path = path;

    /// <summary>配置文件默认路径：exe 同目录下的 settings.ini。</summary>
    public static string DefaultPath()
    {
        string exe = Application.ExecutablePath;
        string? dir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(dir))
            dir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(dir!, "settings.ini");
    }

    public void Load()
    {
        lock (_sync)
        {
            _data.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                string? current = null;
                foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        current = line.Substring(1, line.Length - 2);
                        if (!_data.ContainsKey(current))
                            _data[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else if (current != null && line.Contains("="))
                    {
                        int eq = line.IndexOf('=');
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        if (key.Length > 0)
                            _data[current!][key] = val;
                    }
                }
            }
            catch
            {
                // 损坏则当作空配置
                _data.Clear();
            }
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("; 添加/删除程序管理器 配置文件");
                sb.AppendLine("; 位于 exe 同目录，可手动编辑，重启软件后生效。");
                foreach (var sec in _data)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + sec.Key + "]");
                    foreach (var kv in sec.Value)
                        sb.AppendLine(kv.Key + "=" + kv.Value);
                }
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 不可写（如受限目录）时静默忽略
            }
        }
    }

    public bool HasSection(string section)
    {
        lock (_sync) return _data.ContainsKey(section);
    }

    public string? Get(string section, string key)
    {
        lock (_sync)
        {
            return _data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : null;
        }
    }

    public bool GetBool(string section, string key, bool def)
    {
        string? v = Get(section, key);
        if (v == null) return def;
        return v.Equals("1", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public int GetInt(string section, string key, int def)
    {
        string? v = Get(section, key);
        return int.TryParse(v, out int r) ? r : def;
    }

    public void Set(string section, string key, string value)
    {
        lock (_sync)
        {
            if (!_data.ContainsKey(section))
                _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _data[section][key] = value;
        }
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");
    public void SetInt(string section, string key, int value) => Set(section, key, value.ToString());
}

/// <summary>
/// 应用设置：封装筛选勾选、列可见性、窗口位置，统一持久化到 exe 同目录的 settings.ini。
/// </summary>
public sealed class AppSettings
{
    private readonly IniFile _ini;

    // 脏标记：仅当用户真正改动过设置（筛选/列/窗口）时才写文件。
    // 这样“默认设置未改动”时不会在 exe 同目录凭空生成 settings.ini。
    private bool _dirty;

    // 窗口基线：窗体真正显示后记录其位置/尺寸，仅当与基线不同才视为改动。
    private bool _windowBaselineSet;
    private bool _windowBaselineMax;
    private System.Drawing.Rectangle _windowBaseline;

    public AppSettings()
    {
        _ini = new IniFile(IniFile.DefaultPath());
        _ini.Load();
    }

    /// <summary>标记有设置被改动，关闭时应持久化。</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>在窗体首次显示后调用，记录窗口位置/尺寸的基线。</summary>
    public void CaptureWindowBaseline(Form form)
    {
        _windowBaselineMax = form.WindowState == FormWindowState.Maximized;
        _windowBaseline = _windowBaselineMax ? form.RestoreBounds : form.Bounds;
        _windowBaselineSet = true;
    }

    // ===================== 筛选 =====================
    public bool FilterSystem { get => _ini.GetBool("Filters", "System", true); set => _ini.SetBool("Filters", "System", value); }
    public bool FilterUpdates { get => _ini.GetBool("Filters", "Updates", true); set => _ini.SetBool("Filters", "Updates", value); }
    public bool FilterOrphanOnly { get => _ini.GetBool("Filters", "OrphanOnly", false); set => _ini.SetBool("Filters", "OrphanOnly", value); }
    public bool FilterHideNoName { get => _ini.GetBool("Filters", "HideNoName", true); set => _ini.SetBool("Filters", "HideNoName", value); }
    public bool Filter64 { get => _ini.GetBool("Filters", "Arch64", true); set => _ini.SetBool("Filters", "Arch64", value); }
    public bool Filter32 { get => _ini.GetBool("Filters", "Arch32", true); set => _ini.SetBool("Filters", "Arch32", value); }
    public bool FilterUser { get => _ini.GetBool("Filters", "User", false); set => _ini.SetBool("Filters", "User", value); }

    // ===================== 列可见性 =====================
    public bool GetColumnVisible(string name, bool def) => _ini.GetBool("Columns", name, def);
    public void SetColumnVisible(string name, bool visible) => _ini.SetBool("Columns", name, visible);

    // ===================== 窗口位置 =====================
    public void SaveWindow(Form form)
    {
        if (!_windowBaselineSet) return;

        // 若窗体处于最小化状态关闭，Location 会变成 (-32000, -32000)，必须使用 RestoreBounds
        if (form.WindowState == FormWindowState.Minimized)
        {
            var b = form.RestoreBounds;
            if (b.Width > 0 && b.Height > 0 && b.X > -30000 && b.Y > -30000)
            {
                _dirty = true;
                _ini.SetBool("Window", "Maximized", false);
                _ini.SetInt("Window", "X", b.X);
                _ini.SetInt("Window", "Y", b.Y);
                _ini.SetInt("Window", "W", b.Width);
                _ini.SetInt("Window", "H", b.Height);
            }
            return;
        }

        bool curMax = form.WindowState == FormWindowState.Maximized;
        var cur = curMax ? form.RestoreBounds : form.Bounds;
        // 与基线完全一致（含最大化状态）视为未改动，不写文件
        if (curMax == _windowBaselineMax && cur == _windowBaseline)
            return;

        _dirty = true;
        if (curMax)
        {
            _ini.SetBool("Window", "Maximized", true);
            var b = form.RestoreBounds;
            if (b.Width > 0 && b.Height > 0 && b.X > -30000 && b.Y > -30000)
            {
                _ini.SetInt("Window", "X", b.X);
                _ini.SetInt("Window", "Y", b.Y);
                _ini.SetInt("Window", "W", b.Width);
                _ini.SetInt("Window", "H", b.Height);
            }
        }
        else
        {
            _ini.SetBool("Window", "Maximized", false);
            _ini.SetInt("Window", "X", form.Location.X);
            _ini.SetInt("Window", "Y", form.Location.Y);
            _ini.SetInt("Window", "W", form.Width);
            _ini.SetInt("Window", "H", form.Height);
        }
    }

    /// <summary>若存在已保存的窗口几何，则应用到窗体。坐标非法或脱离当前屏幕时自动修正/居中。</summary>
    public void ApplyWindow(Form form)
    {
        int x = _ini.GetInt("Window", "X", int.MinValue);
        int y = _ini.GetInt("Window", "Y", int.MinValue);
        int w = _ini.GetInt("Window", "W", int.MinValue);
        int h = _ini.GetInt("Window", "H", int.MinValue);
        if (w <= 0 || h <= 0 || x <= int.MinValue || y <= int.MinValue) return;
        if (x < -30000 || y < -30000) return; // 非法/最小化到极端位置

        int minW = form.MinimumSize.Width > 0 ? form.MinimumSize.Width : 400;
        int minH = form.MinimumSize.Height > 0 ? form.MinimumSize.Height : 300;
        w = Math.Max(w, minW);
        h = Math.Max(h, minH);

        var targetRect = new Rectangle(x, y, w, h);

        // 检查与当前所有连接显示器的工作区是否有足够的有效可视区域（至少 120x60 可视，避免多屏拔掉后窗口停在虚空）
        bool isVisible = false;
        foreach (var screen in Screen.AllScreens)
        {
            var intersect = Rectangle.Intersect(screen.WorkingArea, targetRect);
            if (intersect.Width >= 120 && intersect.Height >= 60)
            {
                isVisible = true;
                break;
            }
        }

        var primaryWorkArea = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : Screen.AllScreens[0].WorkingArea;

        if (!isVisible)
        {
            // 脱离了所有显示器（如副屏拔掉、分辨率降低）：
            // 若原保存尺寸超出当前主屏，重置为适中的默认尺寸 (1000, 640)
            if (w > primaryWorkArea.Width || h > primaryWorkArea.Height)
            {
                w = Math.Min(1000, primaryWorkArea.Width);
                h = Math.Min(640, primaryWorkArea.Height);
            }
            x = primaryWorkArea.Left + Math.Max(0, (primaryWorkArea.Width - w) / 2);
            y = primaryWorkArea.Top + Math.Max(0, (primaryWorkArea.Height - h) / 2);
        }
        else
        {
            // 在屏幕范围内：找到相交面积最大的显示器，确保标题栏在该显示器工作区内，防止被拖到屏幕外无法点击
            Screen? targetScreen = null;
            int maxArea = 0;
            foreach (var screen in Screen.AllScreens)
            {
                var intersect = Rectangle.Intersect(screen.WorkingArea, targetRect);
                int area = intersect.Width * intersect.Height;
                if (area > maxArea)
                {
                    maxArea = area;
                    targetScreen = screen;
                }
            }
            var wa = targetScreen != null ? targetScreen.WorkingArea : primaryWorkArea;
            w = Math.Min(w, wa.Width);
            h = Math.Min(h, wa.Height);
            if (y < wa.Top) y = wa.Top;
            if (y + 40 > wa.Bottom) y = wa.Bottom - 40;
            if (x + 60 > wa.Right) x = wa.Right - 60;
            if (x + w - 60 < wa.Left) x = wa.Left;
        }

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(x, y);
        form.Size = new Size(w, h);
        if (_ini.GetBool("Window", "Maximized", false))
            form.WindowState = FormWindowState.Maximized;
    }

    /// <summary>仅当存在实际改动时才写入 settings.ini；默认设置未改动则不生成文件。</summary>
    public void Save()
    {
        if (_dirty) _ini.Save();
    }
}
