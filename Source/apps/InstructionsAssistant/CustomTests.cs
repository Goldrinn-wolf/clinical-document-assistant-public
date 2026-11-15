using System; using System.IO; using System.Linq; using System.Windows; using SuiteCustomization;
namespace InstructionsAssistant
{
 public static class CustomTests
 {
  public static void Run(MainWindow main,string dir)
  {
   Directory.CreateDirectory(dir);TestSupport.Results.Clear();MainWindow restored=null;
   try {
    Manager manager=main.CustomManager();manager.Types.SelectedIndex=0;manager.Title.Text="自定义睡眠医嘱";manager.Body.Text="人工通用医嘱正文";manager.Save();
    Entry added=main.Library.Entries.Single(e=>e.Custom);TestSupport.Check(added.Step==1&&added.Text=="人工通用医嘱正文","管理窗口保存自定义基础疾病条目");
    TestSupport.Check(main.Library.Matches(added,"sm")&&main.Library.Matches(added,"shuimian"),"自定义条目名称支持拼音和首字母");
    manager.List.SelectedItem=manager.List.Items.Cast<Item>().Single();manager.Body.Text="修改后的通用医嘱";manager.Save();TestSupport.Check(main.Library.Entries.Single(e=>e.Custom).Id==added.Id&&main.Library.Entries.Single(e=>e.Custom).Text=="修改后的通用医嘱","更新自定义条目保留ID且替换正文");
    main.Choose(main.Library.Entries.Single(e=>e.Custom),true);main.SetPuncture(0);main.Generate(false);TestSupport.Check(main.Preview.Text.Contains("修改后的通用医嘱"),"自定义医嘱在实际编号生成中输出");
    restored=new MainWindow(main.Profile);TestSupport.Check(restored.Library.Entries.Single(e=>e.Custom).Text=="修改后的通用医嘱","重启从原数据目录恢复自定义条目");restored.Window.Close();restored=null;
    manager.Refresh();manager.List.SelectedItem=manager.List.Items.Cast<Item>().First();TestSupport.Capture(manager.Window,Path.Combine(dir,"自定义医嘱项目.png"));manager.Window.Close();
    main.DeleteCustomItem(added.Id);TestSupport.Check(!main.Library.Entries.Any(e=>e.Id==added.Id)&&!main.State.Selected.Contains(added.Id),"删除自定义条目移除当前选择，内置模板保留");
    bool rejected=false;try{main.DeleteCustomItem("word-151");}catch{rejected=true;}TestSupport.Check(rejected,"不能删除内置医嘱模板");
    main.SaveCustomItem(new Item { Kind="5",Title="其他病症自定义",Text="针对其他病症的通用正文" });TestSupport.Check(main.Library.Entries.Any(e=>e.Custom&&e.Step==5),"自定义其他病症项目归入第五步");
    TestSupport.Finish(dir);
   } catch(Exception ex) {TestSupport.Error(dir,ex);} finally {if(restored!=null)restored.Window.Close();main.Window.Close();}
  }
 }
}
