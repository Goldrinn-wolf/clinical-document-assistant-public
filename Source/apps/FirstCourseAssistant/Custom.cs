using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Windows; using System.Windows.Controls; using SuiteCustomization;
namespace FirstCourseAssistant
{
 public partial class MainWindow
 {
  public Manager CustomManager()
  {
   Manager.Romanize=PinyinLookup.Convert;
   return new Manager(Window,new[]{new Kind("ddx","鉴别诊断整组"),new Kind("plan","诊疗计划模板"),new Kind("exam","辅助检查参考"),new Kind("vte-preset","VTE通用参考")},()=>Library.Templates.Where(t=>t.Custom).Select(t=>new Item { Id=t.Id,Kind=t.Kind,Title=t.Title,Text=t.Text }).ToList(),SaveCustomItem,DeleteCustomItem,false,false);
  }
  public void SaveCustomItem(Item item)
  {
   if(!customWritable)throw new InvalidOperationException("自定义库无法读取，请先恢复库文件。"); if(!new[]{"ddx","plan","exam","vte-preset"}.Contains(item.Kind))throw new ArgumentException("自定义项目类型无效。");
   List<Template> old=Library.Templates; Library.Templates=old.Where(t=>t.Id!=item.Id).ToList();
   try { Template added=Library.Add(item.Kind,item.Title,item.Text,""); if(item.Id.Length>0) { if(!old.Any(t=>t.Id==item.Id&&t.Custom))throw new ArgumentException("只能修改自定义模板。"); added.Id=item.Id; added.Order=old.First(t=>t.Id==item.Id).Order; } Library.SaveCustom(Path.Combine(Profile,"custom-templates.xml")); } catch { Library.Templates=old;throw; }
   State.Signature=""; Render(); ScheduleSave();
  }
  public void DeleteCustomItem(string id)
  {
   if(!customWritable||!Library.Templates.Any(t=>t.Id==id&&t.Custom))throw new ArgumentException("只能删除可读取的自定义模板。"); List<Template> old=Library.Templates; Library.Templates=old.Where(t=>t.Id!=id).ToList(); try { Library.SaveCustom(Path.Combine(Profile,"custom-templates.xml")); } catch { Library.Templates=old;throw; }
   State.Selected.Remove(id); State.Overrides.Remove(id); State.Signature=""; Render(); ScheduleSave();
  }
  private void CustomPresets(Panel panel,string kind,string target,bool append)
  {
   List<Template> items=Library.Templates.Where(t=>t.Kind==kind).OrderBy(t=>t.Order).ToList(); if(items.Count==0)return;
   Label(panel,kind=="exam"?"自定义辅助检查参考 · 手动加入本例":"自定义VTE参考 · 手动载入后核对");
   TextBox query=new TextBox { Height=36,Margin=new Thickness(0,0,0,5),ToolTip="中文 / 拼音 / 首字母搜索" }; panel.Children.Add(query);
   ComboBox choices=new ComboBox { DisplayMemberPath="",MinHeight=36,Margin=new Thickness(0,0,0,6) }; foreach(Template t in items)choices.Items.Add(new ComboBoxItem { Content=t.Title,Tag=t }); panel.Children.Add(choices);
   query.TextChanged+=delegate { choices.Items.Clear(); foreach(Template t in items.Where(t=>Library.Matches(t,query.Text))) choices.Items.Add(new ComboBoxItem { Content=t.Title,Tag=t }); };
   Button load=new Button { Content=append?"将所选参考加入辅助检查框":"载入所选VTE参考",Margin=new Thickness(0,0,0,12) }; panel.Children.Add(load);
   load.Click+=delegate { if(choices.SelectedItem==null)return; Template t=(Template)((ComboBoxItem)choices.SelectedItem).Tag; if(!append&&State.Get(target).Length>0&&MessageBox.Show(Window,"替换当前VTE正文？","载入参考",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return; if(append) { string prefix=State.Get(target).TrimEnd(); ((TextBox)Fields[target]).Text=(prefix.Length>0?prefix+"\r\n":"")+t.Title+"："+t.Text; } else { ((TextBox)Fields[target]).Text=t.Text; } };
  }
 }
}
