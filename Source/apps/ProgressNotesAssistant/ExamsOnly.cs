using System;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Threading;using SuiteCustomization;
namespace ExaminationAssistant
{
 public static class EntryPoint
 {
  [STAThread]public static int Main(string[] args){ProgressNotesAssistant.Program.ExamsOnly=true;return ProgressNotesAssistant.Program.Main(args);}
 }
}
namespace ProgressNotesAssistant
{
 public partial class MainWindow
 {
  public bool ExamsOnly { get {return Program.ExamsOnly;} }
  public string BuildText(){return ExamsOnly?Generator.Auxiliary(Library,State):Generator.Record(Library,State,State.CurrentModule);}
  private void ConfigureExamsOnly()
  {
   if(!ExamsOnly)return;Window.Title="辅助检查助手 v1.4.1-public";Window.Width=1100;
   foreach(var text in SuiteCustomization.TestSupport.Elements<TextBlock>(Window).Where(t=>t.Text=="日常病程助手"||t.Text=="病程文本").ToList())text.Text=text.Text=="病程文本"?"辅助检查文本":"辅助检查助手";
   ((UIElement)Window.FindName("ModulesPanel")).Visibility=Visibility.Collapsed;((UIElement)Window.FindName("HandlingButton")).Visibility=Visibility.Collapsed;((UIElement)Window.FindName("HelpButton")).Visibility=Visibility.Collapsed;
   ((TextBlock)Window.FindName("ModuleTitle")).Text="辅助检查 · 选择与粘贴";((TextBlock)Window.FindName("ModuleHint")).Text="只生成检查文本，当前患者资料独立保存";((TextBlock)Window.FindName("CaseSummary")).Text="当前患者 · 已选检查 "+Library.Exams.Count(e=>State.Exam(e.Id).Selected)+" 项 · 独立检查草稿";
   ((Button)Window.FindName("GenerateButton")).Content="生成辅助检查  Ctrl+Enter";((TextBlock)Window.FindName("PreviewHint")).Text="有文字逐项保留完整表头；勾选空白项目在最后汇总为无明显异常。";
   form.Children.Clear();Fields.Clear();form.Children.Add(UI.Text("1. 打开辅助检查，先按四类选择。基础血常规、特殊抗体首次进入默认全选，可取消未做项目。\n\n2. 有文字的项目以完整检查名称开头；勾选空白的项目统一汇总为无明显异常。\n\n3. 保存检查后生成，核对后复制或导出 TXT。",16));form.Children.Add(UI.Button("整批粘贴 · 离线智能整理",delegate { OpenSmartLab(Window); }));form.Children.Add(UI.Button("打开辅助检查 · 勾选 / 粘贴",OpenExams));form.Children.Add(UI.Button("管理自定义检查",delegate{CustomManager().Window.ShowDialog();}));form.Children.Add(UI.Text("参考不会自动填入；请手动载入后按本例修改。\n\n“新患者”清空本例检查和预览，保留通用自定义检查。\n\n本工具与病程助手的患者草稿分别保存。",13,"#728994"));
  }
 }
 public static class FixTests
 {
  public static void Run(MainWindow main,string dir)
  {
   Directory.CreateDirectory(dir);TestSupport.Results.Clear();MainWindow restored=null;
   try{
    main.NewPatient(false);var dialog=main.CreateExamsDialog();dialog.Rows["lab-main"].Selected.IsChecked=true;dialog.Rows["lab-main"].Result.Text="血糖及心肌酶异常（测试）";dialog.Rows["urine"].Selected.IsChecked=true;dialog.Save();main.Generate();string expected=main.Library.GetExam("lab-main").Title+"：血糖及心肌酶异常（测试）";
    TestSupport.Check(main.Preview.Text.Contains(expected),"组合检验异常生成时保留完整表头与冒号");TestSupport.Check(!main.Preview.Text.Contains("检验结果：血糖"),"不再输出缺失项目名称的笼统表头");TestSupport.Check(main.Preview.Text.Contains("尿常规无明显异常。"),"空白勾选仍汇总正常");
    main.SaveCustomItem(new Item{Kind="exam",Title="自定义检查名称",Prefix="自定义缩写：",Text="参考"});var custom=main.CustomItems.Single();Core.UpdateResult(main.State,custom.Id,true,"测试异常");main.Generate();TestSupport.Check(main.Preview.Text.Contains("自定义检查名称：测试异常"),"自定义缩写前缀缺名称时回退完整项目表头");
    if(main.ExamsOnly){TestSupport.Check(main.CanOutput()&&!main.Preview.Text.Contains("晨查房")&&!main.Preview.Text.Contains("针对患者"),"独立工具只生成辅助检查，不要求处理方案");string file=Path.Combine(dir,"辅助检查导出.txt");TestSupport.Check(main.ExportTo(file)&&File.ReadAllText(file)==main.Preview.Text,"独立辅助检查TXT与预览完整一致");TestSupport.Check(!((UIElement)main.Window.FindName("ModulesPanel")).IsVisible&&!((UIElement)main.Window.FindName("HandlingButton")).IsVisible,"独立界面不显示病程模块与处理入口");}
    else {TestSupport.Check(main.CreateHandlingDialog().Rows.ContainsKey("lab-main"),"病程内仍保留辅助检查及对应处理联动");}
    if(main.ExamsOnly){IDataObject original;bool available=Tests.SnapshotClipboard(out original),changed=false;try{if(available){changed=true;TestSupport.Check(main.CopyToClipboard()&&Tests.ClipboardText()==main.Preview.Text,"独立检查实际系统剪贴板复制与生成内容一致");Core.UpdateResult(main.State,"lab-main",true,"更新但尚未生成");TestSupport.Check(!main.CopyToClipboard(),"独立检查输入变化后阻止复制旧预览");Core.UpdateResult(main.State,"lab-main",true,"血糖及心肌酶异常（测试）");main.Generate();}else TestSupport.Results.Add("SKIP 无法备份剪贴板，独立复制未验证");}catch(System.Runtime.InteropServices.ExternalException){TestSupport.Results.Add("SKIP 剪贴板占用，独立复制未验证");}finally{if(changed)try{if(original==null)Clipboard.Clear();else Clipboard.SetDataObject(original,true);}catch(System.Runtime.InteropServices.ExternalException){TestSupport.Results.Add("SKIP 无法恢复测试前剪贴板");}}}
    main.Save();restored=new MainWindow(main.Profile);TestSupport.Check(restored.State.Exam("lab-main").Result=="血糖及心肌酶异常（测试）"&&restored.CustomItems.Count==1,"重启恢复本例检查与通用自定义库");restored.Window.Close();restored=null;
    TestSupport.Capture(main.Window,Path.Combine(dir,main.ExamsOnly?"独立辅助检查助手.png":"病程辅助检查修复.png"));if(main.ExamsOnly){main.Window.Width=860;main.Window.Height=700;TestSupport.Capture(main.Window,Path.Combine(dir,"独立辅助检查小屏.png"));TestSupport.Check(main.Preview.ActualHeight>=60&&((Button)main.Window.FindName("GenerateButton")).IsVisible,"独立工具小屏窗口保留可读预览与生成按钮");}dialog=main.CreateExamsDialog();TestSupport.Capture(dialog.Window,Path.Combine(dir,"完整表头检查输入.png"));dialog.Cancel();
    main.NewPatient(false);TestSupport.Check(main.State.Exam("lab-main").Result==""&&!main.State.Exam("lab-main").Selected&&main.CustomItems.Count==1,"新患者清空检查不删除自定义库");TestSupport.Finish(dir);
   }catch(Exception ex){TestSupport.Error(dir,ex);}finally{if(restored!=null)restored.Window.Close();main.Window.Close();}
  }
 }
}
