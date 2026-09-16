using System.Runtime.InteropServices;

namespace 添加删除程序管理;

internal static class Program
{
    /// <summary>
    ///  应用程序入口点。
    /// </summary>
    [STAThread]
    static void Main()
    {
#if NET10_0_OR_GREATER
        // .NET 10/WinForms：用 ApplicationConfiguration 处理高 DPI（会读取清单/运行时配置）。
        ApplicationConfiguration.Initialize();
#else
        // .NET Framework 3.5：无 ApplicationConfiguration 类，也无 app.config 的 PerMonitorV2（需 4.7+）。
        // 用 SetProcessDPIAware 提供系统级 DPI 感知（Win7 仅支持系统级），避免界面在高 DPI 屏上模糊。
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#endif
        Application.Run(new MainForm());
    }

#if !NET10_0_OR_GREATER
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
#endif
}
