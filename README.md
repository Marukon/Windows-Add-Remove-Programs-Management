# 添加 / 删除程序管理器 (Windows Add/Remove Programs Management)

一款轻量、纯净且功能强大的 Windows 已安装软件与卸载注册表管理器，使用 C# / WinForms 基于现代 .NET 10 开发，支持全面扫描 64 位、32 位和当前用户的软件列表，提供残留项智能识别、安全删除备份、原厂卸载调用、快速定位安装目录与 `.reg` 导出等功能。

---

## 🌟 核心特性

- **全架构注册表覆盖**
  - 原生 64 位注册表：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
  - 32 位兼容层注册表 (WOW64)：`HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
  - 当前用户独立注册表：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- **智能残留检测 (Orphan Detection)**
  - 根据注册表中的 `InstallLocation`、`UninstallString` 等信息提取主执行程序及安装路径。
  - 自动检测物理文件是否存在，标示已被卸载但注册表未清理干净的“残留项”。
- **误删无忧：自动安全备份**
  - 在执行“删除选中项”前，程序会自动将选中的注册表项导出为 `.reg` 备份文件存入本地应用数据目录：
    `%LocalAppData%\UninstallManager\Backups`
  - 一旦误删，只需双击对应 `.reg` 文件即可即时恢复。
- **丰富的右键与菜单操作**
  - **卸载此项**：调用软件原生卸载程序（MSI 安装包自动以 `/X` 方式调用）。
  - **删除此项**：清理无效或残留的注册表项。
  - **打开安装目录**：在 Windows 资源管理器中自动定位并高亮选中该程序的主可执行文件或所在目录。
  - **导出为 .reg**：将选中的单项或多项注册表信息导出为标准 Windows 注册表脚本。
  - **复制注册表路径**：一键复制项的完整注册表物理路径。
  - **双击查看详情**：查看包含 DisplayVersion、Publisher、InstallDate、Size、架构类型等完整元数据。
- **多维度即时过滤**
  - 支持按软件名称、发布者实时关键字搜索过滤。
  - 快捷开关：系统组件、更新/补丁、仅残留、隐藏无名称、64 位、32 位、当前用户。
- **纯净绿色 & 开箱即用**
  - 无任何配置文件绑定（默认居中启动），不残留垃圾配置。
  - 发布为**单文件**（Single-File）可执行程序，体积小巧（约 224 KB）。
  - 声明 `requireAdministrator` 清单，双击即以管理员身份运行，确保读写 HKLM 注册表权限完整。

---

## 🛠️ 技术栈与架构

- **语言 / 框架**：C# 14 / .NET 10 (WinForms)
- **目标平台**：Windows (x64)
- **底层互操作**：
  - 使用 Win32 `Advapi32.dll` API（`RegOpenKeyEx`、`RegEnumKeyEx`、`RegQueryValueEx` 等）精确控制 `KEY_WOW64_64KEY` 与 `KEY_WOW64_32KEY` 视图访问，彻底避免 64 位与 32 位注册表重定向冲突。
  - 多线程架构：耗时注册表读取与枚举均在后台线程池（ThreadPool）执行，UI 线程保持流畅不卡顿。

---

## 🚀 编译与发布

### 环境要求
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（或更高版本）
- Windows 10 / 11 操作系统

### 1. 克隆仓库
```bash
git clone https://github.com/Marukon/Windows-Add-Remove-Programs-Management.git
cd Windows-Add-Remove-Programs-Management
```

### 2. 编译调试版
```bash
dotnet build -c Debug
```

### 3. 发布依赖框架单文件版（推荐）
```bash
dotnet publish -c Release
```
发布产物为单个 exe 文件：
`bin\Release\net10.0-windows\win-x64\publish\添加删除程序管理.exe`
> 目标机器需安装 .NET 10 桌面运行时（WindowsDesktop Runtime 10.0+）。

### 4. 发布独立运行版（无需目标机安装 .NET 运行时）
如需打包所有运行时组件为独立免安装 exe：
```bash
dotnet publish -c Release -p:SelfContained=true
```

---

## 📂 项目结构

```text
├── Forms/
│   └── MainForm.cs           # 主界面与事件处理逻辑
├── Models/
│   └── UninstallEntry.cs     # 卸载条目数据模型与枚举定义
├── Services/
│   ├── RegistryPInvoke.cs    # Win32 Advapi32 注册表 API P/Invoke 封装
│   ├── UninstallRegistryService.cs # 注册表全量扫描、残留判定与卸载调用
│   ├── RegFileExporter.cs    # 注册表项导出为 .reg 备份文件
│   └── StrUtil.cs            # 高性能字符串与模糊匹配工具
├── Program.cs                # 应用程序入口点与高 DPI 初始化
├── app.manifest              # 提权声明 (requireAdministrator) 与系统兼容性声明
├── app.ico                   # 应用程序图标
├── 添加删除程序管理.csproj   # 项目工程配置文件
└── 添加删除程序管理.slnx     # 解决方案文件
```

---

## 📄 开源许可

本项目遵循 [MIT License](LICENSE) 许可协议开源。欢迎提交 Issue 或 Pull Request！
