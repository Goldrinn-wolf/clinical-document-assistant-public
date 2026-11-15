using System;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;using SuiteCustomization;
namespace ProgressNotesAssistant
{
 public static class CustomTests
 {
  public static void Run(MainWindow main,string dir)
  {
   Directory.CreateDirectory(dir);TestSupport.Results.Clear();MainWindow restored=null;
   try {
    Manager manager=main.CustomManager();manager.Title.Text="腰椎MRI";manager.Body.Text="通用MRI参考文字";manager.Prefix.Text="腰椎MRI：";manager.Save();Item exam=main.CustomItems.Single();
    TestSupport.Check(main.Library.Exams.Any(e=>e.Id==exam.Id)&&main.Library.Matches(main.Library.GetExam(exam.Id),"yzmri"),"自定义辅助检查加入列表且可首字母搜索");
    var checks=main.CreateExamsDialog();TestSupport.Check(checks.Rows[exam.Id].Result.Text==""&&checks.Rows[exam.Id].Selected.IsChecked==false,"新自定义检查不自动勾选或填参考");checks.Rows[exam.Id].Selected.IsChecked=true;checks.Save();main.Generate();TestSupport.Check(main.Preview.Text.Contains("腰椎MRI无明显异常。"),"自定义空白勾选与内置项目相同按正常汇总");
    checks=main.CreateExamsDialog();checks.Rows[exam.Id].LoadReference.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));checks.Save();TestSupport.Check(main.State.Exam(exam.Id).Result=="通用MRI参考文字","手动载入自定义参考才进入本例检查");
    if(main.ExamsOnly) {
     main.Generate();TestSupport.Check(main.CanOutput()&&main.Preview.Text.Contains("腰椎MRI：通用MRI参考文字"),"独立工具生成自定义检查正文");
     main.Save();restored=new MainWindow(main.Profile);TestSupport.Check(restored.CustomItems.Count==1&&restored.State.Exam(exam.Id).Result=="通用MRI参考文字","独立工具重启恢复自定义检查与结果");restored.Window.Close();restored=null;
     main.NewPatient(false);TestSupport.Check(main.CustomItems.Count==1&&!main.State.Exam(exam.Id).Selected&&main.State.Exam(exam.Id).Result=="","独立工具新患者清空结果并保留自定义检查");
     main.DeleteCustomItem(exam.Id);TestSupport.Check(main.Library.Exams.Count==37,"独立工具删除自定义检查保留37个内置项目");TestSupport.Finish(dir);return;
    }
    var handling=main.CreateHandlingDialog();TestSupport.Check(handling.Rows.ContainsKey(exam.Id),"自定义有字检查自动关联人工处理入口");handling.Rows[exam.Id].Treatment.Text="本例人工处理";handling.Save();main.Generate();TestSupport.Check(main.CanOutput()&&main.Preview.Text.Contains("针对患者腰椎MRI异常（通用MRI参考文字），予以本例人工处理处理。"),"自定义检查处理按相同规则生成");
    main.SaveCustomItem(new Item { Kind="treatment",Title="通用处理参考",Text="手工维护的通用处理" });
    handling=main.CreateHandlingDialog();handling.Rows[exam.Id].Treatment.Clear();TestSupport.Capture(handling.Window,Path.Combine(dir,"自定义处理参考.png"));var choices=TestSupport.Elements<ComboBox>(handling.Window).First(c=>c.Items.Cast<object>().Any(o=>o is Item));choices.SelectedIndex=0;TestSupport.Check(handling.Rows[exam.Id].Treatment.Text=="","选择处理参考名称不自动填写方案");TestSupport.Elements<Button>(handling.Window).First(b=>Convert.ToString(b.Content)=="手动载入处理参考").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));handling.Save();main.Generate();TestSupport.Check(main.State.Exam(exam.Id).Treatment=="手工维护的通用处理"&&main.CanOutput(),"通用处理参考经手动载入写入本例方案并可生成");
    main.SaveCustomItem(new Item { Kind="record",Title="我的病程模块",Text="通用病程骨架",Exams=true,Handling=true });Item record=main.CustomItems.Single(i=>i.Kind=="record");main.SelectModule(record.Id);main.Generate();TestSupport.Check(main.Preview.Text.Contains("通用病程骨架")&&main.Preview.Text.Contains("腰椎MRI")&&main.Fields.ContainsKey("body"),"自定义病程模块可编辑正文并复用检查与处理");
    ((TextBox)main.Fields["body"]).Text="本例自定义正文";main.Generate();main.Save();restored=new MainWindow(main.Profile);TestSupport.Check(restored.State.CurrentModule==record.Id&&restored.Library.Modules.Any(m=>m.Id==record.Id)&&restored.CustomItems.Count==3&&restored.Preview.Text.Contains("本例自定义正文"),"重启恢复三类自定义项目及自定义模块草稿");restored.Window.Close();restored=null;
    main.SaveCustomItem(new Item { Id=exam.Id,Kind="exam",Title=exam.Title,Prefix=exam.Prefix,Text="更新的通用参考" });TestSupport.Check(main.State.Exam(exam.Id).Result=="通用MRI参考文字","更新自定义参考不改写患者已有检查结果");
    manager.Refresh();manager.List.SelectedItem=manager.List.Items.Cast<Item>().First(i=>i.Kind=="record");TestSupport.Capture(manager.Window,Path.Combine(dir,"自定义病程项目.png"));manager.Window.Close();TestSupport.Capture(main.Window,Path.Combine(dir,"自定义病程模块.png"));
    main.NewPatient(false);TestSupport.Check(main.CustomItems.Count==3&&main.State.Exam(exam.Id).Result==""&&!main.State.Exam(exam.Id).Selected,"新患者清空个案，保留自定义检查模块和处理参考");
    main.SelectModule(record.Id);main.DeleteCustomItem(record.Id);TestSupport.Check(main.State.CurrentModule=="day2"&&!main.Library.Modules.Any(m=>m.Id==record.Id),"删除当前自定义模块安全回退入院第二天");main.DeleteCustomItem(exam.Id);TestSupport.Check(main.Library.Exams.Count==37,"删除自定义检查后全部37个内置检查保留");
    bool rejected=false;try{main.DeleteCustomItem("echo");}catch{rejected=true;}TestSupport.Check(rejected,"内置检查不允许在自定义管理中删除");TestSupport.Finish(dir);
   }catch(Exception ex){TestSupport.Error(dir,ex);}finally{if(restored!=null)restored.Window.Close();main.Window.Close();}
  }
 }
}
