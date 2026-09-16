using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using 添加删除程序管理.Models;
using 添加删除程序管理.Services;

namespace 添加删除程序管理;

/// <summary>
/// 主窗口：加载并管理“添加 / 删除程序”列表。
/// 所有耗时操作在后台线程进行，UI 更新回到主线程，避免界面卡顿与跨线程异常。
/// </summary>
public partial class MainForm : Form
{
    private readonly List<UninstallEntry> _all = new();
    private UninstallEntry? _contextEntry;

    private MenuStrip _menu = null!;
    private FlowLayoutPanel _flow = null!;
    private TextBox _txtSearch = null!;
    private CheckBox _chkSystem = null!;
    private CheckBox _chkUpdates = null!;
    private CheckBox _chkOrphanOnly = null!;
    private CheckBox _chkHideNoName = null!;
    private CheckBox _chk64 = null!;
    private CheckBox _chk32 = null!;
    private CheckBox _chkUser = null!;
    private DataGridView _dgv = null!;
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _lblCount = null!;
    private ToolStripStatusLabel _lblAdmin = null!;
    private ToolStripStatusLabel _lblMsg = null!;
    private ContextMenuStrip _ctx = null!;

    public MainForm()
    {
        InitializeComponent();
        // 标题栏使用 exe 内嵌的应用程序图标（与资源管理器显示一致）
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Load += (_, _) => RefreshList();
    }

    private void InitializeComponent()
    {
        Text = "添加 / 删除程序管理器";
        MinimumSize = new Size(820, 520);
        Size = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;

        // ===== 菜单（工具栏功能已合并进菜单）=====
        _menu = new MenuStrip();
        var miFile = new ToolStripMenuItem("文件(&F)");
        var miRefresh = new ToolStripMenuItem("刷新列表", null, (_, _) => RefreshList());
        var miExport = new ToolStripMenuItem("导出选中为 .reg", null, (_, _) => ExportSelected());
        var miExit = new ToolStripMenuItem("退出", null, (_, _) => Close());
        miFile.DropDownItems.AddRange(miRefresh, miExport, miExit);

        var miAction = new ToolStripMenuItem("操作(&A)");
        var miDelete = new ToolStripMenuItem("删除选中项", null, (_, _) => DeleteSelected());
        var miUninstall = new ToolStripMenuItem("卸载选中项", null, (_, _) => UninstallSelected());
        var miSelAll = new ToolStripMenuItem("全选", null, (_, _) => SetSelection(true));
        var miSelNone = new ToolStripMenuItem("反选/全不选", null, (_, _) => SetSelection(false));
        miAction.DropDownItems.AddRange(miDelete, miUninstall, new ToolStripSeparator(), miSelAll, miSelNone);

        var miHelp = new ToolStripMenuItem("帮助(&H)");
        var miAbout = new ToolStripMenuItem("关于", null, (_, _) => ShowAbout());
        miHelp.DropDownItems.Add(miAbout);

        var miView = new ToolStripMenuItem("视图(&V)");
        _menu.Items.AddRange(miFile, miAction, miView, miHelp);

        // ===== 顶部筛选面板 =====
        // 注意：直接用 FlowLayoutPanel 顶停靠 + AutoSize。不要用“AutoSize 的 Panel + Dock.Fill 子控件”组合，
        // 否则 AutoSize 父容器不会随 Fill 子控件撑开，面板会塌缩，导致复选框被裁切、无法点击。
        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6)
        };

        _txtSearch = new TextBox { Width = 220 };
#if NET10_0_OR_GREATER
        _txtSearch.PlaceholderText = "按名称或发布者筛选…";
#else
        _txtSearch.SetCueBanner("按名称或发布者筛选…");
#endif
        _txtSearch.TextChanged += (_, _) => ApplyFilterAndRender();

        _chkSystem = new CheckBox { Text = "系统组件", Checked = true, AutoSize = true };
        _chkUpdates = new CheckBox { Text = "更新/补丁", Checked = true, AutoSize = true };
        _chkOrphanOnly = new CheckBox { Text = "仅残留", Checked = false, AutoSize = true };
        _chkHideNoName = new CheckBox { Text = "隐藏无名称", Checked = true, AutoSize = true };
        _chk64 = new CheckBox { Text = "64 位", Checked = true, AutoSize = true };
        _chk32 = new CheckBox { Text = "32 位", Checked = true, AutoSize = true };
        _chkUser = new CheckBox { Text = "当前用户", Checked = false, AutoSize = true };
        foreach (var c in new[] { _chkSystem, _chkUpdates, _chkOrphanOnly, _chkHideNoName, _chk64, _chk32, _chkUser })
            c.CheckedChanged += (_, _) => ApplyFilterAndRender();

        _flow.Controls.Add(new Label { Text = "搜索：", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
        _flow.Controls.Add(_txtSearch);
        _flow.Controls.Add(_chkSystem);
        _flow.Controls.Add(_chkUpdates);
        _flow.Controls.Add(_chkOrphanOnly);
        _flow.Controls.Add(_chkHideNoName);
        _flow.Controls.Add(_chk64);
        _flow.Controls.Add(_chk32);
        _flow.Controls.Add(_chkUser);

        // ===== 数据网格 =====
        _dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            ReadOnly = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = System.Drawing.SystemColors.Window,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };
        _dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colSel", HeaderText = "选择", Width = 45 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "名称", Width = 240 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPublisher", HeaderText = "发布者", Width = 160 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colVersion", HeaderText = "版本", Width = 90 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDate", HeaderText = "安装日期", Width = 90 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colSize", HeaderText = "大小", Width = 80 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colType", HeaderText = "类型", Width = 110 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOrphan", HeaderText = "残留?", Width = 80 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colUninstall", HeaderText = "卸载命令", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _dgv.Columns["colSize"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        // 仅“选择”列允许用户勾选；其余数据列设为只读，避免误改单元格内容。
        foreach (DataGridViewColumn col in _dgv.Columns)
            if (col.Name != "colSel") col.ReadOnly = true;

        // 视图菜单：按列切换显示/隐藏，并应用/保存已配置的项。
        {
            foreach (DataGridViewColumn col in _dgv.Columns)
            {
                col.Visible = true;
                var mi = new ToolStripMenuItem(col.HeaderText)
                {
                    CheckOnClick = true,
                    Checked = true,
                    Name = "col_" + col.Name
                };
                mi.CheckedChanged += (_, _) => col.Visible = mi.Checked;
                miView.DropDownItems.Add(mi);
            }
        }

        _dgv.CellMouseDown += Dgv_CellMouseDown;
        // “选择”列已设为可编辑（ReadOnly=false），用户点击复选框方框即可勾选/取消；
        // 双击“选择”列时不弹详情（该列用于勾选）。
        _dgv.CellDoubleClick += (_, e) =>
        {
            // 双击“选择”列不弹详情（该列用于勾选）。
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0
                && _dgv.Columns[e.ColumnIndex].Name != "colSel"
                && _dgv.Rows[e.RowIndex].Tag is UninstallEntry entry)
                ShowDetails(entry);
        };

        // ===== 右键菜单 =====
        _ctx = new ContextMenuStrip();
        _ctx.Items.Add("删除此项", null, (_, _) => DeleteSelected(_contextEntry));
        _ctx.Items.Add("卸载此项", null, (_, _) => UninstallSelected(_contextEntry));
        _ctx.Items.Add("导出此项为 .reg", null, (_, _) => ExportSelected(_contextEntry));
        _ctx.Items.Add(new ToolStripSeparator());
        _ctx.Items.Add("打开安装目录", null, (_, _) => OpenInstallDir(_contextEntry));
        _ctx.Items.Add("复制注册表路径", null, (_, _) => CopyPath(_contextEntry));

        // ===== 状态栏 =====
        _status = new StatusStrip();
        _lblCount = new ToolStripStatusLabel("共 0 项");
        _lblAdmin = new ToolStripStatusLabel();
        _lblMsg = new ToolStripStatusLabel { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        _status.Items.AddRange(_lblCount, _lblAdmin, _lblMsg);
        _lblAdmin.Text = "管理员：" + (UninstallRegistryService.IsRunningAsAdmin() ? "是" : "否");

        // ===== 组装 =====
        Controls.Add(_dgv);
        Controls.Add(_flow);
        Controls.Add(_status);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
    }

    // ===================== 数据加载与渲染 =====================

    /// <summary>
    /// 重新加载注册表列表。耗时读取放在线程池线程，完成后回到 UI 线程渲染，
    /// 避免界面卡顿与跨线程访问异常（.NET Framework 3.5 无 async/await）。
    /// </summary>
    private void RefreshList()
    {
        SetMsg("正在加载注册表列表…");
        Cursor = Cursors.WaitCursor;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            List<UninstallEntry>? list = null;
            Exception? err = null;
            try { list = UninstallRegistryService.LoadAll(); }
            catch (Exception ex) { err = ex; }

            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (err != null)
                    {
                        SetMsg("加载失败：" + err.Message);
                        MessageBox.Show(this, "读取注册表时出错：\n" + err.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        _all.Clear();
                        _all.AddRange(list!);
                        ApplyFilterAndRender();
                        _lblCount.Text = "共 " + _all.Count + " 项";
                        SetMsg("就绪。");
                    }
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }));
        });
    }

    /// <summary>重新按筛选条件渲染。渲染前保留已勾选的项。</summary>
    private void ApplyFilterAndRender()
    {
        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow r in _dgv.Rows)
        {
            if (r.Cells["colSel"].Value is bool b && b && r.Tag is UninstallEntry ee)
                selectedKeys.Add(ee.KeyName);
        }

        string filter = _txtSearch.Text.Trim();
        _dgv.Rows.Clear();

        foreach (var e in _all)
        {
            if (!PassesFilter(e, filter)) continue;

            int i = _dgv.Rows.Add();
            var row = _dgv.Rows[i];
            row.Cells["colSel"].Value = selectedKeys.Contains(e.KeyName);
            row.Cells["colName"].Value = e.DisplayNameOrFallback;
            row.Cells["colPublisher"].Value = e.Publisher;
            row.Cells["colVersion"].Value = e.DisplayVersion;
            row.Cells["colDate"].Value = e.InstallDate;
            row.Cells["colSize"].Value = e.SizeText;
            row.Cells["colType"].Value = e.ArchitectureText;
            row.Cells["colOrphan"].Value = e.OrphanText;
            row.Cells["colUninstall"].Value = e.UninstallString;
            row.Tag = e;
        }
    }

    private bool PassesFilter(UninstallEntry e, string filter)
    {
        if (!_chkSystem.Checked && e.IsSystemComponent) return false;
        if (!_chkUpdates.Checked && e.IsUpdate) return false;
        if (_chkOrphanOnly.Checked && e.OrphanState != OrphanStatus.Orphaned) return false;
        // “隐藏无名称”（默认勾选）：排除无 DisplayName 的注册表项，让列表更干净。
        if (_chkHideNoName.Checked && StrUtil.IsNullOrWhiteSpace(e.DisplayName)) return false;
        if (!_chk64.Checked && e.Architecture == ArchitectureKind.Native64) return false;
        if (!_chk32.Checked && e.Architecture == ArchitectureKind.Wow32) return false;
        if (!_chkUser.Checked && e.Architecture == ArchitectureKind.User) return false;

        if (!string.IsNullOrEmpty(filter))
        {
            bool hit = StrUtil.IndexOfI(e.DisplayNameOrFallback, filter) >= 0
                       || StrUtil.IndexOfI(e.Publisher, filter) >= 0;
            if (!hit) return false;
        }
        return true;
    }

    private List<UninstallEntry> GetSelectedEntries(UninstallEntry? single = null)
    {
        if (single != null) return new List<UninstallEntry> { single };

        var list = new List<UninstallEntry>();
        foreach (DataGridViewRow r in _dgv.Rows)
        {
            if (r.Cells["colSel"].Value is bool b && b && r.Tag is UninstallEntry e)
                list.Add(e);
        }
        return list;
    }

    private void SetSelection(bool value)
    {
        foreach (DataGridViewRow r in _dgv.Rows)
            r.Cells["colSel"].Value = value;
    }

    // ===================== 删除 =====================

    private void DeleteSelected(UninstallEntry? single = null)
    {
        var targets = GetSelectedEntries(single);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要删除的条目（或右键选择“删除此项”）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool hasHklm = targets.Any(e => e.Hive == RegistryHive.LocalMachine);
        if (hasHklm && !UninstallRegistryService.IsRunningAsAdmin())
        {
            var r = MessageBox.Show(this,
                "删除系统级（HKLM）残留项需要管理员权限，当前未以管理员身份运行，操作可能失败。\n是否仍要继续？",
                "权限提示", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        var detail = string.Join("\n", targets.Select(e => "• " + e.DisplayNameOrFallback + "\n    " + e.FriendlyPath).ToArray());
        var confirm = MessageBox.Show(this,
            "确定要删除以下 " + targets.Count + " 个注册表项吗？\n删除前已自动导出 .reg 备份（可在“关于”中查看备份位置）。此操作不可恢复！\n\n" + detail,
            "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        string backupDir = BackupDirectory();
        Cursor = Cursors.WaitCursor;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            int ok = 0, fail = 0;
            var errors = new StringBuilder();
            foreach (var e in targets)
            {
                try
                {
                    UninstallRegistryService.DeleteEntry(e, backupDir);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.AppendLine(e.DisplayNameOrFallback + "：" + ex.Message);
                }
            }

            List<UninstallEntry>? list = null;
            Exception? reloadErr = null;
            try { list = UninstallRegistryService.LoadAll(); }
            catch (Exception ex) { reloadErr = ex; }

            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (list != null)
                    {
                        _all.Clear();
                        _all.AddRange(list);
                        ApplyFilterAndRender();
                        _lblCount.Text = "共 " + _all.Count + " 项";
                    }
                    else if (reloadErr != null)
                    {
                        errors.AppendLine("刷新失败：" + reloadErr.Message);
                    }

                    string msg = "成功删除 " + ok + " 项，失败 " + fail + " 项。";
                    if (fail > 0) msg += "\n\n" + errors;
                    MessageBox.Show(this, msg, "删除结果", MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }));
        });
    }

    // ===================== 卸载 =====================

    private void UninstallSelected(UninstallEntry? single = null)
    {
        var targets = GetSelectedEntries(single);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要卸载的条目（或右键选择“卸载此项”）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var noCmd = targets.Where(e => StrUtil.IsNullOrWhiteSpace(e.UninstallString)).ToList();
        var withCmd = targets.Where(e => !StrUtil.IsNullOrWhiteSpace(e.UninstallString)).ToList();

        if (withCmd.Count > 0)
        {
            var detail = string.Join("\n", withCmd.Select(e => "• " + e.DisplayNameOrFallback).ToArray());
            var r = MessageBox.Show(this,
                $"即将为以下 {withCmd.Count} 个程序启动卸载程序，请按提示完成卸载：\n\n{detail}\n\n（MSI 安装包会被自动以 /X 方式卸载）",
                "启动卸载", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            foreach (var e in withCmd)
            {
                try { UninstallRegistryService.RunUninstall(e, quiet: false); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"无法卸载“{e.DisplayNameOrFallback}”：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        if (noCmd.Count > 0)
        {
            var detail = string.Join("\n", noCmd.Select(e => "• " + e.DisplayNameOrFallback + "  (" + e.FriendlyPath + ")").ToArray());
            MessageBox.Show(this,
                $"以下 {noCmd.Count} 个条目没有可用的卸载命令，无法自动卸载；如确认是残留项，请使用“删除”功能移除：\n\n{detail}",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ===================== 导出 =====================

    private void ExportSelected(UninstallEntry? single = null)
    {
        var targets = GetSelectedEntries(single);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要导出的条目（或右键选择“导出此项”）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "注册表文件 (*.reg)|*.reg|所有文件 (*.*)|*.*",
            FileName = targets.Count == 1 ? Sanitize(targets[0].KeyName) + ".reg" : "卸载列表备份.reg",
            Title = "导出选中条目为注册表文件"
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string content = RegFileExporter.BuildRegFile(targets);
            File.WriteAllText(sfd.FileName, content, System.Text.Encoding.Unicode);
            MessageBox.Show(this, $"已导出 {targets.Count} 个条目到：\n{sfd.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ===================== 右键 / 详情 / 复制 =====================

    private void Dgv_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        if (e.RowIndex < 0) return;
        _dgv.ClearSelection();
        _dgv.Rows[e.RowIndex].Selected = true;
        _contextEntry = _dgv.Rows[e.RowIndex].Tag as UninstallEntry;
        _ctx.Show(_dgv, _dgv.PointToClient(Cursor.Position));
    }

    private void CopyPath(UninstallEntry? entry)
    {
        if (entry == null) return;
        try
        {
            Clipboard.SetText(entry.FriendlyPath);
            SetMsg("已复制注册表路径：" + entry.FriendlyPath);
        }
        catch (Exception ex)
        {
            SetMsg("复制失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 打开条目的安装目录：优先用 InstallLocation；为空时从 DisplayIcon 或
    /// UninstallString 里解析出可执行文件所在目录。若解析到的是文件则在资源管理器中定位它。
    /// </summary>
    private void OpenInstallDir(UninstallEntry? entry)
    {
        if (entry == null) return;

        string? dir = null;    // 存在的目录
        string? file = null;   // 存在的文件（用于在资源管理器中选中）

        // 1) InstallLocation
        string loc = entry.InstallLocation.Trim().Trim('"');
        if (loc.Length > 0)
        {
            try { if (Directory.Exists(loc)) dir = loc; } catch { /* 忽略非法路径 */ }
        }

        // 2) DisplayIcon（形如 "C:\App\app.exe,0"，去掉逗号后的图标索引）
        if (dir == null && file == null)
        {
            string icon = entry.DisplayIcon.Trim().Trim('"');
            if (icon.Length > 0)
            {
                int comma = icon.LastIndexOf(',');
                if (comma > 1) icon = icon.Substring(0, comma).Trim().Trim('"');
                try { if (File.Exists(icon)) file = icon; } catch { }
            }
        }

        // 3) UninstallString 里的可执行文件路径
        if (dir == null && file == null)
        {
            string exe = ExtractExecutablePath(entry.UninstallString);
            if (exe.Length > 0)
            {
                try
                {
                    if (File.Exists(exe)) file = exe;
                    else
                    {
                        string? d = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) dir = d;
                    }
                }
                catch { }
            }
        }

        try
        {
            if (file != null)
            {
                // 在资源管理器中打开所在目录并选中该文件
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + file + "\"",
                    UseShellExecute = true
                });
                SetMsg("已定位文件：" + file);
            }
            else if (dir != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
                SetMsg("已打开安装目录：" + dir);
            }
            else
            {
                MessageBox.Show(this,
                    "未能找到该条目的安装目录。\n可能是残留项、MSI 安装或注册表未记录安装位置。",
                    "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开安装目录失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 从卸载命令字符串中解析可执行文件路径。
    /// 支持带引号的路径（"C:\a b\x.exe" /S）与不带引号的路径（C:\a\x.exe /S）。
    /// </summary>
    private static string ExtractExecutablePath(string uninstallString)
    {
        string s = uninstallString.Trim();
        if (s.Length == 0) return string.Empty;

        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s.Substring(1, end - 1) : string.Empty;
        }

        // 无引号：找到 ".exe" 结尾处（大小写不敏感），之后视为参数
        int idx = StrUtil.IndexOfI(s, ".exe");
        if (idx >= 0) return s.Substring(0, idx + 4);

        // 退而求其次：取第一个空格前的部分
        int sp = s.IndexOf(' ');
        return sp > 0 ? s.Substring(0, sp) : s;
    }

    private void ShowDetails(UninstallEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("名称：      " + e.DisplayNameOrFallback);
        sb.AppendLine("发布者：    " + (e.Publisher.Length == 0 ? "（无）" : e.Publisher));
        sb.AppendLine("版本：      " + (e.DisplayVersion.Length == 0 ? "（无）" : e.DisplayVersion));
        sb.AppendLine("安装日期：  " + (e.InstallDate.Length == 0 ? "（无）" : e.InstallDate));
        sb.AppendLine("大小：      " + (e.SizeText.Length == 0 ? "（无）" : e.SizeText));
        sb.AppendLine("类型：      " + e.ArchitectureText);
        sb.AppendLine("残留判定：  " + e.OrphanText);
        sb.AppendLine("系统组件：  " + (e.IsSystemComponent ? "是" : "否"));
        sb.AppendLine("MSI 安装：  " + (e.IsWindowsInstaller ? "是" : "否"));
        sb.AppendLine("更新/补丁： " + (e.IsUpdate ? "是" : "否"));
        sb.AppendLine("注册表路径：" + e.FriendlyPath);
        sb.AppendLine();
        sb.AppendLine("安装位置：  " + (e.InstallLocation.Length == 0 ? "（无）" : e.InstallLocation));
        sb.AppendLine("卸载命令：  " + (e.UninstallString.Length == 0 ? "（无）" : e.UninstallString));
        sb.AppendLine("静默卸载：  " + (e.QuietUninstallString.Length == 0 ? "（无）" : e.QuietUninstallString));

        MessageBox.Show(this, sb.ToString(), "条目详情：" + e.DisplayNameOrFallback, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        var sb = new StringBuilder();
        sb.AppendLine("添加 / 删除程序管理器");
        sb.AppendLine(".NET 10 / .NET Framework 3.5.1 (WinForms) · 兼容 Windows 7 ~ Windows 11");
        sb.AppendLine();
        sb.AppendLine("作者：布布");
        sb.AppendLine();
        sb.AppendLine("功能：");
        sb.AppendLine(" • 加载全部“添加/删除程序”列表（系统 64 位、32 位、当前用户）");
        sb.AppendLine(" • 检测并清理卸载后残留的注册表项");
        sb.AppendLine(" • 删除前自动导出 .reg 备份，可随时还原");
        sb.AppendLine(" • 启动原程序卸载、导出 .reg、复制路径");
        sb.AppendLine();
        sb.AppendLine("备份目录（删除时自动生成）：");
        sb.AppendLine(" " + BackupDirectory());
        sb.AppendLine();
        sb.AppendLine("版本说明：");
        sb.AppendLine(" • Windows 10/11 版：基于 .NET 10，编译为 64 位；");
        sb.AppendLine(" • Windows 7 版：基于 .NET Framework 3.5.1，编译为 32 位；");
        sb.AppendLine("两者共用同一份代码，仅目标框架与架构不同。");
        MessageBox.Show(this, sb.ToString(), "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ===================== 辅助 =====================

    private void SetMsg(string text) => _lblMsg.Text = text;

    private static string BackupDirectory()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(Path.Combine(baseDir, "UninstallManager"), "Backups");
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
