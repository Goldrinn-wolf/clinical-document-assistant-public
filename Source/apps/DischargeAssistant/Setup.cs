using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DischargeSetup
{
    public static class Setup
    {
        private const string Product = "出院带药助手";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DischargeAssistant";
        private static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DischargeAssistant"); } }
        private static string StartMenu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product); } }
        private static readonly string[] Files = { "DischargeAssistant.exe", "DischargeAssistant.exe.config", "使用说明.txt", "药物库待核对.md", "测试与兼容性.md" };
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--test-install") { string dir = Path.GetFullPath(args[1]); InstallPayload(dir); Shortcut(Path.Combine(dir, "测试快捷方式.lnk"), Path.Combine(dir, "DischargeAssistant.exe")); File.WriteAllText(Path.Combine(dir, "安装载荷验证.txt"), "PASS 安装载荷解包及SHA256比对完成。\r\nPASS 在测试目录成功创建快捷方式。未修改用户桌面、开始菜单或注册表。", new UTF8Encoding(true)); return 0; }
                if (String.Equals(Path.GetFileName(Assembly.GetExecutingAssembly().Location), "Uninstall.exe", StringComparison.OrdinalIgnoreCase)) return Uninstall();
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    int release = key == null ? 0 : Convert.ToInt32(key.GetValue("Release", 0));
                    if (release < 528040) { MessageBox.Show("请先安装 Microsoft .NET Framework 4.8，然后重新运行安装程序。\n下载地址见压缩包内的使用说明。", Product); return 1; }
                }
                Application app = new Application();
                Window w = new Window { Title = Product + " 安装", Width = 560, Height = 380, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = ColorBrush("#F3F6F8"), FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 14 };
                StackPanel panel = new StackPanel { Margin = new Thickness(30) };
                panel.Children.Add(new TextBlock { Text = Product, FontSize = 27, FontWeight = FontWeights.Bold, Foreground = ColorBrush("#153D49") });
                panel.Children.Add(new TextBlock { Text = "离线药物模板 · 多选生成 · 一键复制\n\n版本 1.1.0  /  x86  /  .NET Framework 4.8\n安装到当前用户，无需管理员权限。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 14), Foreground = ColorBrush("#587680") });
                panel.Children.Add(new TextBlock { Text = InstallDir, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
                CheckBox desktop = new CheckBox { Content = "创建桌面快捷方式", IsChecked = true, Margin = new Thickness(0, 18, 0, 18) }; panel.Children.Add(desktop);
                Button install = new Button { Content = "安装", Background = ColorBrush("#10837D"), Foreground = Brushes.White, Padding = new Thickness(15, 10, 15, 10) }; panel.Children.Add(install);
                install.Click += (s, e) => {
                    try
                    {
                        if (Process.GetProcessesByName("DischargeAssistant").Length > 0) { MessageBox.Show(w, "请先关闭正在运行的出院带药助手，再安装或更新。", Product); return; }
                        InstallPayload(InstallDir);
                        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(InstallDir, "Uninstall.exe"), true);
                        Directory.CreateDirectory(StartMenu);
                        Shortcut(Path.Combine(StartMenu, Product + ".lnk"), Path.Combine(InstallDir, "DischargeAssistant.exe"));
                        Shortcut(Path.Combine(StartMenu, "卸载.lnk"), Path.Combine(InstallDir, "Uninstall.exe"));
                        if (desktop.IsChecked == true) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product + ".lnk"), Path.Combine(InstallDir, "DischargeAssistant.exe"));
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                        {
                            key.SetValue("DisplayName", Product); key.SetValue("DisplayVersion", "1.1.0"); key.SetValue("Publisher", "DischargeAssistant"); key.SetValue("InstallLocation", InstallDir);
                            key.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "Uninstall.exe") + "\""); key.SetValue("DisplayIcon", Path.Combine(InstallDir, "DischargeAssistant.exe")); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                        }
                        install.IsEnabled = false; MessageBox.Show(w, "安装完成，可从桌面或开始菜单打开。", Product); w.Close();
                    }
                    catch (Exception ex) { MessageBox.Show(w, "安装未完成：" + ex.Message, Product); }
                };
                w.Content = panel; app.Run(w); return 0;
            }
            catch (Exception ex) { if (args.Length > 0) return 1; MessageBox.Show("操作未完成：" + ex.Message, Product); return 1; }
        }
        private static void InstallPayload(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string name in Files)
            {
                byte[] bytes;
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                using (MemoryStream m = new MemoryStream()) { resource.CopyTo(m); bytes = m.ToArray(); }
                string destination = Path.Combine(dir, name);
                File.WriteAllBytes(destination, bytes);
                using (SHA256 sha = SHA256.Create())
                    if (Convert.ToBase64String(sha.ComputeHash(bytes)) != Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(destination)))) throw new IOException("安装文件校验失败：" + name);
            }
        }
        private static void Shortcut(string path, string target)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(type);
            object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type linkType = link.GetType(); linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        private static int Uninstall()
        {
            string current = Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            if (!String.Equals(current, Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("请从安装目录运行卸载程序。", Product); return 1; }
            if (MessageBox.Show("卸载出院带药助手？\n本机草稿将保留。", Product, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 0;
            if (Process.GetProcessesByName("DischargeAssistant").Length > 0) { MessageBox.Show("请先关闭出院带药助手，再卸载。", Product); return 1; }
            foreach (string name in Files) { string file = Path.Combine(current, name); if (File.Exists(file)) File.Delete(file); }
            foreach (string link in new[] { Path.Combine(StartMenu, Product + ".lnk"), Path.Combine(StartMenu, "卸载.lnk"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product + ".lnk") }) if (File.Exists(link)) File.Delete(link);
            if (Directory.Exists(StartMenu) && Directory.GetFileSystemEntries(StartMenu).Length == 0) Directory.Delete(StartMenu);
            Registry.CurrentUser.DeleteSubKey(UninstallKey, false);
            // A running executable cannot remove itself safely; leave this single file for manual removal.
            MessageBox.Show("已卸载软件，草稿已保留。\n卸载程序自身位于：" + current + "\n如需完全移除，可关闭后删除此目录。", Product); return 0;
        }
        private static Brush ColorBrush(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
    }
}
