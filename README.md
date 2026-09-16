# 添加 / 删除程序管理器 (Windows Add/Remove Programs Management)

一款轻量、纯净且功能强大的 Windows 已安装软件与卸载注册表管理器。**原生跨越 Windows 7 至 Windows 11 全代操作系统**，提供 **.NET 10（Win10/11）** 与 **.NET Framework 3.5.1（Win7）** 双版本架构，支持全面扫描 64 位、32 位和当前用户注册表，具备残留项智能识别、误删自动安全备份、原厂卸载调用、快速定位安装目录与 `.reg` 导出等功能。

对于已经卸载的软件却还在添加删除程序列表里面的，使用此软件可很方便的处理掉。

---

## 💻 双版本体系与系统兼容性

本项目采用**同一套核心代码**实现双目标框架编译，针对不同代际 Windows 系统量身定制最佳体验：

| 维度 | Windows 10 / 11 现代版 | Windows 7 经典兼容版 |
| :--- | :--- | :--- |
| **目标系统** | Windows 10 / Windows 11（主流现代系统） | Windows 7 / 8 / 8.1（兼顾老旧与工控系统） |
| **目标框架** | **.NET 10** (`net10.0-windows`) | **.NET Framework 3.5.1** (`net35`) |
| **编译架构** | **64 位 (x64)** | **32 位 (x86)**，兼容 32 位与 64 位 Win7 |
| **运行库依赖** | 依赖 .NET 10 桌面运行时（或使用 Self-Contained 独立打包） | **零依赖！** Windows 7 SP1 原生内置 .NET 3.5.1，开箱即用免装运行库 |
| **高 DPI 支持** | Per-Monitor V2 动态感知（多屏/缩放无缝切换、高清锐利） | Win7 系统级 DPI 感知 (`SetProcessDPIAware`)，防止界面模糊 |
| **发布形态** | 单文件绿色 exe（体积仅约 224 KB） | 原生轻量 exe（体积仅约 54 KB） |
| **编译环境要求** | **现代 .NET 10 SDK 即可直接一键编译两者！**（无需安装旧版 .NET 3.5 SDK） |

> 💡 **关于 .NET 3.5 的编译说明**：
> 本项目通过引入微软官方的 `Microsoft.NETFramework.ReferenceAssemblies.net35` 引用包，**无需在现代开发机上安装任何老旧的 .NET 3.5 SDK 或 VS 2008 组件**。只需安装现代 .NET 10 SDK，即可同时编译出原生的 Windows 7 可执行文件！

---

## 🌟 核心特性

- **全架构注册表深度覆盖**
  - 原生 64 位注册表：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
  - 32 位兼容层注册表 (WOW64)：`HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
  - 当前用户独立注册表：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- **残留项智能判定 (Orphan Detection)**
  - 根据注册表记录的 `InstallLocation`、`UninstallString` 等信息解析主执行程序与安装目录。
  - 自动检测物理文件是否存在，标示已被卸载但注册表未清理干净的“残留项”。
- **误删无忧：全自动 .reg 安全备份**
  - 执行“删除选中项”前，程序会自动将选中条目导出为 `.reg` 备份文件存入本地应用数据目录：
    `%LocalAppData%\UninstallManager\Backups`
  - 一旦误删，随时双击备份文件即可秒级还原注册表。
- **便捷丰富的操作**
  - **卸载此项**：调用软件官方卸载程序（MSI 安装包自动以 `/X` 方式调用）。
  - **删除此项**：清理无效或残留的注册表项。
  - **打开安装目录**：在 Windows 资源管理器中自动定位并高亮选中该程序的主可执行文件或所在文件夹。
  - **导出为 .reg**：批量或单项导出为标准 Windows 注册表脚本。
  - **复制注册表路径**：一键复制项的完整注册表物理路径。
  - **双击查看详情**：查看包含 DisplayVersion、Publisher、InstallDate、Size、架构类型等完整元数据。
- **多维度即时过滤**
  - 支持按软件名称、发布者实时关键字模糊搜索。
  - 快捷筛选开关：系统组件、更新/补丁、仅残留、隐藏无名称、64 位、32 位、当前用户。
- **纯净绿色 & 免配置**
  - 默认居中启动（`1000 × 640`），不依赖外部 `settings.ini` 配置文件，不残留垃圾文件。
  - 声明 `requireAdministrator` 清单，双击即以管理员身份运行，确保 HKLM 注册表读写权限完整。

---

## 🛠️ 技术实现

- **底层互操作 (P/Invoke)**：
  - 使用 Win32 `Advapi32.dll` API（`RegOpenKeyEx`、`RegEnumKeyEx`、`RegQueryValueEx` 等）精确控制 `KEY_WOW64_64KEY` 与 `KEY_WOW64_32KEY` 视图访问，彻底避免 64 位与 32 位注册表重定向冲突。
- **高响应异步架构**：
  - 注册表扫描与残留判定等耗时逻辑全部运行于后台线程池（ThreadPool），界面操作丝滑不卡顿。
- **代码垫片 (Polyfill)**：
  - 项目内建 `WinForms48Polyfill.cs`、`NullableAttributes.cs`、`IsExternalInit.cs` 等垫片，使高版本 C# 语法与 WinForms API 能无缝降级编译至 .NET Framework 3.5.1。

---

## 🚀 编译与发布指南

### 开发环境要求
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（或更高版本）
- 操作系统：Windows 10 / 11

### 1. 克隆代码库
```bash
git clone https://github.com/Marukon/Windows-Add-Remove-Programs-Management.git
cd Windows-Add-Remove-Programs-Management
```

### 2. 一键编译双版本（Debug / Release）
```bash
dotnet build -c Release
```
此命令将**同时生成**：
- Windows 10/11 版：`bin\Release\net10.0-windows\win-x64\添加删除程序管理.dll`
- Windows 7 版：`bin\Release\net35\添加删除程序管理.exe`（原生 32 位，免装运行库）

### 3. 发布 Windows 10 / 11 依赖框架单文件版（推荐）
```bash
dotnet publish -f net10.0-windows -c Release
```
发布产物为单个纯净 exe 文件（仅约 224 KB）：
`bin\Release\net10.0-windows\win-x64\publish\添加删除程序管理.exe`

### 4. 发布 Windows 10 / 11 独立免安装版（自带 .NET 运行时）
如需在未安装 .NET 10 运行时的 Windows 10/11 机器上运行：
```bash
dotnet publish -f net10.0-windows -c Release -p:SelfContained=true
```

---

## 📂 项目结构

```text
├── Forms/
│   └── MainForm.cs               # 主界面设计与事件处理逻辑
├── Models/
│   └── UninstallEntry.cs         # 注册表条目数据模型与枚举
├── Services/
│   ├── RegistryPInvoke.cs        # Win32 Advapi32 注册表底层 API 封装
│   ├── UninstallRegistryService.cs # 注册表枚举扫描、残留判定与卸载调用
│   ├── RegFileExporter.cs        # 注册表导出为 .reg 备份文件
│   └── StrUtil.cs                # 跨版本兼容的高性能字符串匹配工具
├── Program.cs                    # 应用程序入口点与不同系统的 DPI 初始化
├── WinForms48Polyfill.cs         # .NET Framework 3.5 API 垫片兼容层
├── NullableAttributes.cs         # C# 空安全属性兼容
├── IsExternalInit.cs             # C# init 属性兼容
├── GlobalUsings.cs               # 全局引用定义
├── app.manifest                  # 提权声明 (requireAdministrator) 与 Win7~Win11 兼容声明
├── app.config                    # Windows 7 (.NET 3.5) 运行时配置
├── app.ico                       # 程序内嵌高清图标
├── 添加删除程序管理.csproj       # 双目标框架项目工程文件 (net10.0-windows + net35)
└── 添加删除程序管理.slnx         # 解决方案文件
```

---

## 📄 开源许可

本项目基于 [MIT License](LICENSE) 开源，欢迎提交 Issue 与 Pull Request！
