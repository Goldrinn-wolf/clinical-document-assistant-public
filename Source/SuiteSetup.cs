using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Reflection;using System.Security.Cryptography;using System.Text;using System.Xml.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using Microsoft.Win32;
namespace ClinicalSuite
{
 public static class SuiteSetup
 {
  private const string Product="临床文书助手四合一（公开版）",Key=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClinicalSuitePublic";
  private static string InstallDir { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","ClinicalSuitePublic");} }
  private static string MenuDir { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Product);} }
  private static List<XElement> Files() {using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.xml"))return XDocument.Load(s).Root.Elements("File").ToList();}
  private static string SafePath(string dir,string name) {string root=Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("安装载荷路径无效。");return path;}
  private static string Hash(byte[] b) {using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
  private static void Extract(string dir)
  {
   var expected=Files();using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
   {
    if(zip.Entries.Count!=expected.Count)throw new InvalidDataException("安装载荷文件数量不符。");foreach(XElement file in expected) {string name=(string)file.Attribute("path");var entry=zip.Entries.FirstOrDefault(e=>e.FullName.Replace('\\','/')==name.Replace('\\','/'));if(entry==null)throw new InvalidDataException("安装载荷缺失："+name);byte[] bytes;using(Stream s=entry.Open())using(MemoryStream m=new MemoryStream()) {s.CopyTo(m);bytes=m.ToArray();}if(Hash(bytes)!=(string)file.Attribute("sha256"))throw new InvalidDataException("载荷校验失败："+name);string path=SafePath(dir,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);if(Hash(File.ReadAllBytes(path))!=(string)file.Attribute("sha256"))throw new IOException("文件写入校验失败："+name); }
   }
  }
  private static void Shortcut(string file,string target)
  {
   Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});Type lt=link.GetType();lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(target)});lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
  }
  private static bool Running() {return new[]{"ClinicalSuite","DischargeAssistant","InstructionsAssistant","FirstCourseAssistant","ProgressNotesAssistant","ExaminationAssistant"}.Any(n=>Process.GetProcessesByName(n).Length>0);}
  [STAThread]public static int Main(string[] args)
  {
   try {
    if(args.Length==2&&args[0]=="--test-install") {Extract(args[1]);Shortcut(Path.Combine(args[1],"测试入口.lnk"),Path.Combine(args[1],"ClinicalSuite.exe"));File.WriteAllText(Path.Combine(args[1],"installer-tests.txt"),"PASS 全部安装载荷SHA256解包校验\r\nPASS 四合一入口快捷方式创建\r\n未修改真实桌面、开始菜单和注册表。",new UTF8Encoding(true));return 0;}
    if(Path.GetFileName(Assembly.GetExecutingAssembly().Location).Equals("Uninstall.exe",StringComparison.OrdinalIgnoreCase))return Uninstall();
    using(RegistryKey key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) {if(key==null||Convert.ToInt32(key.GetValue("Release",0))<528040) {MessageBox.Show("请先安装.NET Framework 4.8。下载说明见压缩包。",Product);return 1;} }
    var app=new Application();var w=new Window {Title=Product+" · 安装",Width=590,Height=420,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=(Brush)new BrushConverter().ConvertFromString("#F3F6F8"),FontFamily=new FontFamily("Microsoft YaHei"),FontSize=14};var p=new StackPanel {Margin=new Thickness(28)};p.Children.Add(new TextBlock {Text=Product,FontSize=26,FontWeight=FontWeights.Bold});p.Children.Add(new TextBlock {Text="出院带药 · 出院医嘱 · 入院首程 · 日常病程\n\n一次安装，四个入口；各工具继续使用原本机数据。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,18)});p.Children.Add(new TextBlock {Text=InstallDir,TextWrapping=TextWrapping.Wrap,FontSize=12});var desktop=new CheckBox {Content="创建四合一首页桌面快捷方式",IsChecked=true,Margin=new Thickness(0,18,0,18)};p.Children.Add(desktop);var install=new Button {Content="安装",Padding=new Thickness(15,10,15,10)};p.Children.Add(install);
    install.Click+=delegate {try {if(Running()) {MessageBox.Show(w,"请关闭四个助手和首页后再安装或更新。",Product);return;} Extract(InstallDir);File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(InstallDir,"Uninstall.exe"),true);Directory.CreateDirectory(MenuDir);Shortcut(Path.Combine(MenuDir,Product+".lnk"),Path.Combine(InstallDir,"ClinicalSuite.exe"));Shortcut(Path.Combine(MenuDir,"卸载.lnk"),Path.Combine(InstallDir,"Uninstall.exe"));if(desktop.IsChecked==true)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk"),Path.Combine(InstallDir,"ClinicalSuite.exe"));using(RegistryKey key=Registry.CurrentUser.CreateSubKey(Key)) {key.SetValue("DisplayName",Product);key.SetValue("DisplayVersion","1.4.1");key.SetValue("InstallLocation",InstallDir);key.SetValue("UninstallString","\""+Path.Combine(InstallDir,"Uninstall.exe")+"\"");key.SetValue("DisplayIcon",Path.Combine(InstallDir,"ClinicalSuite.exe"));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}MessageBox.Show(w,"安装完成，请从桌面打开四合一首页。",Product);w.Close();}catch(Exception ex){MessageBox.Show(w,"安装未完成："+ex.Message,Product);} };w.Content=p;app.Run(w);return 0;
   }catch(Exception ex){if(args.Length==2&&args[0]=="--test-install"){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"installer-error.txt"),ex.ToString(),new UTF8Encoding(true));}else if(args.Length==0)MessageBox.Show(ex.Message,Product);return 1;}
  }
  private static int Uninstall()
  {
   if(!Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).Equals(InstallDir,StringComparison.OrdinalIgnoreCase))return 1;if(Running()) {MessageBox.Show("请关闭助手和首页后再卸载。",Product);return 1;}if(MessageBox.Show("卸载四合一软件？本机草稿、自定义库及原独立版保留。",Product,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return 0;
   foreach(XElement file in Files()){string path=SafePath(InstallDir,(string)file.Attribute("path"));if(File.Exists(path))File.Delete(path);}foreach(string file in new[]{Path.Combine(MenuDir,Product+".lnk"),Path.Combine(MenuDir,"卸载.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk")})if(File.Exists(file))File.Delete(file);if(Directory.Exists(MenuDir)&&Directory.GetFileSystemEntries(MenuDir).Length==0)Directory.Delete(MenuDir);Registry.CurrentUser.DeleteSubKey(Key,false);MessageBox.Show("已卸载。草稿与自定义库已保留；卸载程序关闭后可手动移除安装目录。",Product);return 0;
  }
 }
}
