using System; using System.IO; using System.Linq; using System.Diagnostics; using System.Threading; using System.Reflection; using System.Runtime.InteropServices; using System.Windows; using System.Windows.Controls; using System.Windows.Markup; using System.Windows.Media; using System.Windows.Media.Imaging; using System.Windows.Threading;
namespace ClinicalSuite
{
 public class Launcher
 {
  public Window Window; public Button[] Buttons=new Button[4]; public string TestDirectory=""; public Process LastLaunched; public OfflineLab.MainWindow SmartLab;
  public static readonly string[] Names={"出院带药助手","出院医嘱助手","入院首程助手","日常病程助手"};
  public static readonly string[] Apps={"DischargeAssistant","InstructionsAssistant","FirstCourseAssistant","ProgressNotesAssistant"};
  public static string Root { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
  [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
  [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle,int command);
  public Launcher()
  {
   using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.xaml"))Window=(Window)XamlReader.Load(stream);
   ((Button)Window.FindName("ExaminationButton")).Click+=delegate {try { string path=Path.Combine(Root,"apps","ExaminationAssistant","ExaminationAssistant.exe");if(!File.Exists(path))throw new FileNotFoundException("辅助检查工具文件缺失，请完整解压。",path);var existing=Process.GetProcessesByName("ExaminationAssistant").FirstOrDefault(p=>p.MainWindowHandle!=IntPtr.Zero);if(existing!=null) {ShowWindow(existing.MainWindowHandle,9);SetForegroundWindow(existing.MainWindowHandle);}else Process.Start(new ProcessStartInfo{FileName=path,WorkingDirectory=Path.GetDirectoryName(path),UseShellExecute=false}); }catch(Exception ex){MessageBox.Show(Window,ex.Message,"辅助检查助手");}};
   ((Button)Window.FindName("SmartLabButton")).Click+=delegate { OpenSmartLab(); };
   for(int i=0;i<4;i++) { int index=i; Buttons[i]=(Button)Window.FindName("Entry"+i); Buttons[i].Click+=delegate { try { string arguments=""; if(TestDirectory.Length>0) { string path=Path.Combine(TestDirectory,Apps[index]);Directory.CreateDirectory(path);arguments="--self-test \""+path+"\""; } LastLaunched=Launch(index,arguments); ((TextBlock)Window.FindName("Status")).Text="已打开"+Names[index]+"，首页可继续打开其他工具。"; } catch(Exception ex) { if(TestDirectory.Length>0)throw;MessageBox.Show(Window,"打开失败："+ex.Message,Names[index]); } }; }
  }
  public void OpenSmartLab()
  {
   if(SmartLab!=null && SmartLab.Window.IsLoaded) {SmartLab.Window.Activate();return;}
   SmartLab=new OfflineLab.MainWindow();SmartLab.Window.Owner=Window;SmartLab.Window.WindowStartupLocation=WindowStartupLocation.CenterOwner;SmartLab.Window.Title="检验智能整理 · 四合一 v1.4.1-public";
   SmartLab.Window.Closed+=delegate {SmartLab=null;};SmartLab.Window.Show();
  }
  private void TestSmartLab(string dir)
  {
   var button=(Button)Window.FindName("SmartLabButton");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(SmartLab==null || !SmartLab.Window.IsVisible || SmartLab.Window.Owner!=Window)throw new Exception("首页智能整理窗口没有打开");
   var first=SmartLab;SmartLab.Input.Text=OfflineLab.MainWindow.Sample;SmartLab.Arrange();
   string expected="1、血细胞五项：粒细胞↑，白细胞↓；2、肝肾功血糖血脂心肌酶+HYC：丙氨酸氨基转移酶↑；3、肾功能、甲功、凝血无明显异常。";
   if(!SmartLab.CanOutput() || SmartLab.Output.Text!=expected || !SmartLab.ExportTo(Path.Combine(dir,"首页智能整理导出.txt")))throw new Exception("首页内嵌整理结果或导出失败");
   Capture(SmartLab.Window,Path.Combine(dir,"首页内嵌智能整理.png"));button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(SmartLab!=first || SmartLab.Input.Text!=OfflineLab.MainWindow.Sample)throw new Exception("重复点击丢失智能整理输入");SmartLab.Window.Close();
   Window.Width=760;Window.Height=700;Window.UpdateLayout();Window.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);
   if(!button.IsVisible || button.ActualWidth<150 || Buttons.Any(b=>!b.IsVisible || b.ActualHeight<100))throw new Exception("小屏首页入口布局不完整");
   Capture(Window,Path.Combine(dir,"四合一首页_760宽.png"));Window.Width=980;Window.Height=750;Window.UpdateLayout();
  }
  public Process Launch(int index,string arguments)
  {
   if(index<0||index>=Apps.Length)throw new ArgumentOutOfRangeException("index"); string path=Path.Combine(Root,"apps",Apps[index],Apps[index]+".exe"); if(!File.Exists(path))throw new FileNotFoundException("软件文件缺失，请完整解压或重新安装。",path);
   if(arguments.Length==0) foreach(Process existing in Process.GetProcessesByName(Apps[index])) { try { if(existing.MainWindowHandle!=IntPtr.Zero) { ShowWindow(existing.MainWindowHandle,9); SetForegroundWindow(existing.MainWindowHandle); return existing; } } catch { } }
   return Process.Start(new ProcessStartInfo { FileName=path,Arguments=arguments,WorkingDirectory=Path.GetDirectoryName(path),UseShellExecute=false });
  }
  [STAThread] public static int Main(string[] args)
  {
   bool test=args.Length>0&&args[0]=="--preview-test"; string dir=args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"ClinicalSuiteTests"); bool first;
   using(Mutex mutex=new Mutex(true,test?"Local\\ClinicalSuiteTest_"+Guid.NewGuid().ToString("N"):"Local\\ClinicalSuitePublic_1",out first))
   {
    if(!first) { var old=Process.GetProcessesByName("ClinicalSuite").FirstOrDefault(p=>p.Id!=Process.GetCurrentProcess().Id&&p.MainWindowHandle!=IntPtr.Zero); if(old!=null) { ShowWindow(old.MainWindowHandle,9);SetForegroundWindow(old.MainWindowHandle); } return 0; }
    Application app=new Application(); Launcher launcher=new Launcher(); if(test)launcher.TestDirectory=dir;
    if(test)launcher.Window.Loaded+=delegate { launcher.Window.Dispatcher.BeginInvoke(new Action(delegate { try { Directory.CreateDirectory(dir); foreach(var entry in Apps)if(!File.Exists(Path.Combine(Root,"apps",entry,entry+".exe")))throw new Exception("入口文件缺失："+entry); foreach(Button button in launcher.Buttons)if(!button.IsVisible||button.ActualWidth<200||button.ActualHeight<100)throw new Exception("四个入口布局未完整显示"); Capture(launcher.Window,Path.Combine(dir,"四合一首页.png"));
   for(int i=0;i<4;i++) { launcher.Buttons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var proc=launcher.LastLaunched; if(proc==null||!proc.WaitForExit(15000)||proc.ExitCode!=0)throw new Exception("入口实际调用失败："+Apps[i]); } launcher.TestSmartLab(dir); File.WriteAllText(Path.Combine(dir,"launcher-ui-tests.txt"),"PASS 四个清晰入口及对应EXE路径完整\r\nPASS 四个入口在首页可见且尺寸满足点击\r\nPASS 首页实际WPF渲染截图\r\nPASS 四个入口按钮实际启动对应EXE并运行各自核心回归，退出码均0\r\nPASS 首页智能整理按钮实际打开同进程窗口并复用已打开窗口\r\nPASS 内嵌整理结果和TXT导出正确\r\nPASS 760宽首页四个入口和智能整理按钮均可见\r\n"); } catch(Exception ex) { File.WriteAllText(Path.Combine(dir,"launcher-error.txt"),ex.ToString());Environment.ExitCode=1; } finally { launcher.Window.Close(); } }),DispatcherPriority.ApplicationIdle); };
    app.Run(launcher.Window);return Environment.ExitCode;
   }
  }
  private static void Capture(Window window,string file)
  {
   window.UpdateLayout(); var content=(FrameworkElement)window.Content; DrawingVisual visual=new DrawingVisual(); using(DrawingContext dc=visual.RenderOpen()) { var rect=new Rect(0,0,content.ActualWidth,content.ActualHeight);dc.DrawRectangle(window.Background,null,rect);dc.DrawRectangle(new VisualBrush(content),null,rect); } var bmp=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(Stream stream=File.Create(file))png.Save(stream);
  }
 }
}
