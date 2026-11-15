using System;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using SuiteCustomization;
namespace FirstCourseAssistant
{
 public static class CustomTests
 {
  private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject obj) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++){var child=VisualTreeHelper.GetChild(obj,i);if(child is T)yield return (T)child;foreach(var item in Find<T>(child))yield return item;} }
  public static void Run(MainWindow main,string dir)
  {
   Directory.CreateDirectory(dir);TestSupport.Results.Clear();MainWindow restored=null;
   try {
    Manager manager=main.CustomManager();manager.Types.SelectedIndex=2;manager.Title.Text="自定义腰椎检查";manager.Body.Text="通用手动检查参考";manager.Save();
    Template exam=main.Library.Templates.Single(t=>t.Kind=="exam");TestSupport.Check(main.Library.Matches(exam,"yz")&&main.Library.Matches(exam,"jiancha"),"辅助检查自定义项目支持名称拼音搜索");
    main.GoTo(1);TestSupport.Check(main.State.Get("Tests")=="","保存通用参考不自动填入患者检查");
    var combo=Find<ComboBox>(main.Window).First(c=>c.Items.Cast<object>().Any(o=>o is ComboBoxItem&&((ComboBoxItem)o).Tag==exam));combo.SelectedIndex=0;
    TestSupport.Check(main.State.Get("Tests")=="","选择辅助检查参考名称不会自动载入");
    Find<Button>(main.Window).First(b=>Convert.ToString(b.Content)=="将所选参考加入辅助检查框").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));TestSupport.Check(main.State.Get("Tests").Contains("自定义腰椎检查：通用手动检查参考"),"手动按钮将参考加入实际辅助检查输入框");
    main.SaveCustomItem(new Item { Kind="vte-preset",Title="自定义VTE参考",Text="临床人员编写的VTE通用参考" });main.State.Set("VteMode","custom");main.GoTo(3);
    var vte=main.Library.Templates.Single(t=>t.Kind=="vte-preset");var vc=Find<ComboBox>(main.Window).First(c=>c.Items.Cast<object>().Any(o=>o is ComboBoxItem&&((ComboBoxItem)o).Tag==vte));vc.SelectedIndex=0;Find<Button>(main.Window).First(b=>Convert.ToString(b.Content)=="载入所选VTE参考").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    TestSupport.Check(Generator.Vte(main.Library,main.State)==vte.Text,"自定义VTE参考经手动载入进入原VTE生成位置");
    main.SaveCustomItem(new Item { Kind="ddx",Title="自定义鉴别",Text="人工鉴别通用正文" });main.SaveCustomItem(new Item { Kind="plan",Title="自定义计划",Text="人工计划通用正文" });
    main.SaveCustomItem(new Item { Id=exam.Id,Kind="exam",Title=exam.Title,Text="更新后的参考" });TestSupport.Check(main.Library.Get(exam.Id).Text=="更新后的参考"&&main.State.Get("Tests").Contains("通用手动检查参考"),"更新通用参考不覆写当前患者已粘贴报告");
    main.Save();restored=new MainWindow(main.Profile);TestSupport.Check(restored.Library.Templates.Count(t=>t.Custom)==4,"首程四类自定义项目重启恢复");restored.Window.Close();restored=null;
    manager.Refresh();manager.List.SelectedItem=manager.List.Items.Cast<Item>().First(i=>i.Kind=="exam");TestSupport.Capture(manager.Window,Path.Combine(dir,"自定义首程项目.png"));manager.Window.Close();main.DeleteCustomItem(exam.Id);TestSupport.Check(!main.Library.Templates.Any(t=>t.Id==exam.Id)&&main.State.Get("Tests").Contains("通用手动检查参考"),"删除辅助参考仍保留本例已粘贴结果");
    bool rejected=false;try{main.DeleteCustomItem("plan-common");}catch{rejected=true;}TestSupport.Check(rejected,"不能删除原诊疗计划");TestSupport.Finish(dir);
   }catch(Exception ex){TestSupport.Error(dir,ex);}finally{if(restored!=null)restored.Window.Close();main.Window.Close();}
  }
 }
}
