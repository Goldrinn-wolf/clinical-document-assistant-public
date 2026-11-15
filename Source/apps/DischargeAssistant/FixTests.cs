using System;using System.IO;using System.Linq;using System.Text;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using System.Windows.Media.Imaging;using System.Windows.Threading;
namespace DischargeAssistant
{
 public static class FixTests
 {
  public static void Run(MainWindow main,string dir)
  {
   Directory.CreateDirectory(dir);var results=new System.Collections.Generic.List<string>();MainWindow restored=null;Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL "+name);results.Add("PASS "+name);};
   try{
    string secondDefaultText="复诊可预约某某医院某门诊，由某某主任接诊，具体出诊时间请以医院公布的信息为准。";
    check(main.Followups["模板二"]==secondDefaultText,"首次启动预置手机版模板二模板原文");((Button)main.Window.FindName("TemplateTwoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(main.Library.Intro==secondDefaultText,"未手动粘贴即可一键切换模板二默认正文");var initialEditor=main.FollowupDialog();Capture(initialEditor,Path.Combine(dir,"手机版默认模板已补入.png"));initialEditor.Close();main.SwitchFollowup("模板一");
    string migrationDir=Path.Combine(dir,"empty-legacy");Directory.CreateDirectory(migrationDir);new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Followups",new System.Xml.Linq.XAttribute("active","模板一"),new System.Xml.Linq.XElement("Template",new System.Xml.Linq.XAttribute("name","模板一"),"原已修改模板一模板"),new System.Xml.Linq.XElement("Template",new System.Xml.Linq.XAttribute("name","模板二"),""))).Save(Path.Combine(migrationDir,"followup-templates.xml"));restored=new MainWindow(true,Path.Combine(migrationDir,"draft.xml"));check(restored.Followups["模板二"]==secondDefaultText&&restored.Followups["模板一"]=="原已修改模板一模板","升级补齐旧版空白模板二位置且保留已改模板一模板");restored.Window.Close();restored=null;
    main.SaveFollowup("模板一","模板一模板自定义复诊正文（测试）",true);main.SaveFollowup("模板二","模板二模板自定义复诊正文（测试）",false);check(main.ActiveFollowup=="模板一"&&main.Library.Intro.Contains("模板一"),"分别保存两份默认模板，不误切换当前模板");
    ((Button)main.Window.FindName("TemplateTwoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(main.ActiveFollowup=="模板二"&&main.Library.Intro=="模板二模板自定义复诊正文（测试）","模板二按钮一键切换已保存完整模板");
    main.Selected.Add(main.Library.Drugs.First().Id);main.Generate(false);check(main.Preview.Text.StartsWith("模板二模板自定义复诊正文（测试）"),"已选默认模板在实际带药正文开头生成");
    ((Button)main.Window.FindName("TemplateOneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(main.Stale&&main.Selected.Count==1,"切换模板保留已选药物并标记预览需重生成");main.Generate(false);check(main.Preview.Text.StartsWith("模板一模板自定义复诊正文（测试）"),"模板一按钮一键切换并生成另一份模板");
    restored=new MainWindow(true,Path.Combine(dir,"profile","draft.xml"));check(restored.Followups["模板一"]=="模板一模板自定义复诊正文（测试）"&&restored.Followups["模板二"]=="模板二模板自定义复诊正文（测试）"&&restored.ActiveFollowup=="模板一","重启恢复两份独立模板及当前选择");restored.Window.Close();restored=null;
    Capture(main.Window,Path.Combine(dir,"带药双模板按钮.png"));var editor=main.FollowupDialog();Capture(editor,Path.Combine(dir,"双模板粘贴编辑.png"));var content=(Grid)editor.Content;var body=content.Children.OfType<TextBox>().Single();var names=content.Children.OfType<ComboBox>().Single();body.Text="模板一新粘贴模板（测试）";names.SelectedItem="模板二";check(body.Text=="模板二模板自定义复诊正文（测试）","编辑窗口切换名称载入对应正文，未保存改写仍保留于本次编辑");body.Text="模板二新粘贴模板（测试）";content.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(main.Followups["模板一"]=="模板一新粘贴模板（测试）"&&main.Followups["模板二"]=="模板二新粘贴模板（测试）"&&main.ActiveFollowup=="模板二","实际保存按钮同时保存两份粘贴正文并应用当前模板");
    editor=main.FollowupDialog();((Grid)editor.Content).Children.OfType<TextBox>().Single().Text="取消编辑不保存";editor.Close();check(main.Followups["模板二"]=="模板二新粘贴模板（测试）","关闭编辑窗口不污染已保存模板");results.Add("TOTAL "+results.Count+" fix checks passed");File.WriteAllLines(Path.Combine(dir,"fix-tests.txt"),results,new UTF8Encoding(true));
   }catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines(Path.Combine(dir,"fix-tests.txt"),results,new UTF8Encoding(true));Environment.ExitCode=1;}finally{if(restored!=null)restored.Window.Close();main.Window.Close();}
  }
  private static void Capture(Window w,string path){if(!w.IsVisible)w.Show();w.UpdateLayout();w.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);var c=(FrameworkElement)w.Content;var rect=new Rect(0,0,c.ActualWidth,c.ActualHeight);var v=new DrawingVisual();using(var dc=v.RenderOpen()){dc.DrawRectangle(w.Background??Brushes.White,null,rect);dc.DrawRectangle(new VisualBrush(c),null,rect);}var bmp=new RenderTargetBitmap((int)Math.Ceiling(rect.Width),(int)Math.Ceiling(rect.Height),96,96,PixelFormats.Pbgra32);bmp.Render(v);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(path))png.Save(stream);}
 }
}
