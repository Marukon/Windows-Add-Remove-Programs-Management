// .NET Framework（3.5 / 4.8）下缺失的 WinForms API 垫片，仅对 Framework 目标编译，避免与 .NET 10 原生实现冲突。
#if NETFRAMEWORK
using System.Runtime.InteropServices;

namespace System.Windows.Forms
{
    /// <summary>
    /// 为 ToolStripItemCollection 补充 params 版本的 AddRange（4.8 仅有数组重载）。
    /// 通过扩展方法提供，与 .NET 10 原生的 params 重载不冲突。
    /// </summary>
    internal static class ToolStripItemCollectionExtensions
    {
        public static void AddRange(this ToolStripItemCollection collection, params ToolStripItem[] items)
        {
            collection.AddRange(items);
        }
    }

    /// <summary>
    /// 4.8 的 TextBox 没有 PlaceholderText 属性，用 EM_SETCUEBANNER 实现占位提示。
    /// </summary>
    internal static class TextBoxCueBanner
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetCueBanner(this TextBox textBox, string text)
        {
            SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
#endif
