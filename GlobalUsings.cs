// 手写 global using，供 .NET Framework 3.5 目标使用（ImplicitUsings 在该目标下已关闭）。
// 仅包含 3.5 实际存在的命名空间，刻意排除 System.Threading.Tasks（4.0 才有）。
// .NET 10 目标下 ImplicitUsings 仍开启，此文件与其重复声明不会产生冲突（编译器自动去重）。
global using System;
global using System.Collections.Generic;
global using System.Drawing;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Threading;
global using System.Windows.Forms;
global using Microsoft.Win32;
global using 添加删除程序管理;
global using 添加删除程序管理.Models;
global using 添加删除程序管理.Services;
