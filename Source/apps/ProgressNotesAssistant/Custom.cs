using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Windows; using System.Windows.Controls; using System.Xml.Linq; using SuiteCustomization;
namespace ProgressNotesAssistant
{
 public partial class MainWindow
 {
  public List<Item> CustomItems=new List<Item>(); private bool customWritable=true;
  public void RestoreCustom()
  {
   string path=Path.Combine(Profile,"custom-items.xml"); if(File.Exists(path))try { ReadCustom(path); } catch { try { ReadCustom(path+".bak"); } catch { customWritable=false; } } ApplyCustom();
  }
  private void ReadCustom(string path)
  {
   XDocument doc; using(Stream stream=File.OpenRead(path))doc=Storage.Read(stream); if(doc.Root==null||doc.Root.Name!="CustomItems")throw new InvalidDataException("自定义库格式无效。");
   List<Item> items=doc.Root.Elements("Item").Select(e=>new Item { Id=(string)e.Attribute("id")??"",Kind=(string)e.Attribute("kind")??"",Title=(string)e.Attribute("title")??"",Prefix=(string)e.Attribute("prefix")??"",Text=(string)e.Element("Text")??"",Exams=(bool?)e.Attribute("exams")??false,Handling=(bool?)e.Attribute("handling")??false }).ToList();
   if(items.Any(i=>!i.Id.StartsWith("custom-",StringComparison.Ordinal)||!new[]{"exam","record","treatment"}.Contains(i.Kind)||String.IsNullOrWhiteSpace(i.Title)||(i.Kind!="exam"&&String.IsNullOrWhiteSpace(i.Text)))||items.Select(i=>i.Id).Distinct().Count()!=items.Count)throw new InvalidDataException("自定义条目无效。"); CustomItems=items;
  }
  private void SaveItems(List<Item> items)
  {
   if(!customWritable)throw new InvalidOperationException("自定义库无法读取，原文件保留。请恢复后再保存。");
   Storage.Save(new XDocument(new XElement("CustomItems",items.Select(i=>new XElement("Item",new XAttribute("id",i.Id),new XAttribute("kind",i.Kind),new XAttribute("title",i.Title),new XAttribute("prefix",i.Prefix),new XAttribute("exams",i.Exams),new XAttribute("handling",i.Handling),new XElement("Text",i.Text))))),Path.Combine(Profile,"custom-items.xml"));
  }
  public void SaveCustomItem(Item item)
  {
   if(!new[]{"exam","record","treatment"}.Contains(item.Kind)||String.IsNullOrWhiteSpace(item.Title)||(item.Kind!="exam"&&String.IsNullOrWhiteSpace(item.Text)))throw new ArgumentException("请填写有效项目名称与模板。");
   if(item.Id.Length>0&&!CustomItems.Any(i=>i.Id==item.Id))throw new ArgumentException("只能修改自定义项目。");
   if(CustomItems.Any(i=>i.Id!=item.Id&&i.Kind==item.Kind&&i.Title.Trim()==item.Title.Trim()))throw new ArgumentException("该类型已有同名自定义项目。");
   Item copy=new Item { Id=item.Id.Length==0?"custom-"+Guid.NewGuid().ToString("N"):item.Id,Kind=item.Kind,Title=item.Title.Trim(),Text=Generator.Normalize(item.Text.Trim()),Prefix=item.Prefix.Trim(),Exams=item.Exams,Handling=item.Handling };
   List<Item> next=CustomItems.Where(i=>i.Id!=copy.Id).ToList(); next.Add(copy); SaveItems(next); CustomItems=next; ApplyCustom(); RebuildModules(); SelectModule(State.CurrentModule); Changed();
  }
  public void DeleteCustomItem(string id)
  {
   if(!CustomItems.Any(i=>i.Id==id))throw new ArgumentException("只能删除自定义项目。"); List<Item> next=CustomItems.Where(i=>i.Id!=id).ToList(); SaveItems(next); CustomItems=next; ApplyCustom(); RebuildModules(); SelectModule(State.CurrentModule); Changed();
  }
  private void ApplyCustom()
  {
   Library.Exams.RemoveAll(e=>e.Id.StartsWith("custom-",StringComparison.Ordinal)); Library.Modules.RemoveAll(m=>m.Id.StartsWith("custom-",StringComparison.Ordinal));
   foreach(Item item in CustomItems)
   {
    if(item.Kind=="exam") { ExamDefinition exam=new ExamDefinition { Id=item.Id,Title=item.Title,Prefix=item.Prefix.Length>0?item.Prefix:item.Title+"：" }; if(item.Text.Length>0)exam.References.Add(new ReferenceText { Id=item.Id+"-reference",Title="自定义手动参考",Text=item.Text }); Library.Exams.Add(exam); }
    if(item.Kind=="record") { ModuleDefinition record=new ModuleDefinition { Id=item.Id,Title=item.Title,Body="{{body}}"+(item.Exams?"\r\n辅助检查：{{EXAMS}}":"")+(item.Handling?"\r\n处理：{{HANDLING}}":"") }; record.Fields.Add(new FieldDefinition { Key="body",Label="本例记录正文",Default=item.Text,Edit="large",Size=240,Required=true }); Library.Modules.Add(record); }
   }
  }
  private void RebuildModules()
  {
   changing=true; modules.Items.Clear(); for(int i=0;i<moduleIds.Length;i++)modules.Items.Add(new ListBoxItem { Content=UI.Text(titles[i],14),Tag=moduleIds[i] });
   foreach(ModuleDefinition module in Library.Modules.Where(m=>m.Id.StartsWith("custom-",StringComparison.Ordinal)))modules.Items.Add(new ListBoxItem { Content=UI.Text(module.Title+" · 自定义",14),Tag=module.Id }); changing=false;
  }
  public Manager CustomManager()
  {
   Manager.Romanize=PinyinLookup.Convert;
   return new Manager(Window,(ExamsOnly ? new[]{new Kind("exam","辅助检查项目与参考")} : new[]{new Kind("exam","辅助检查项目与参考"),new Kind("record","病程记录模块"),new Kind("treatment","通用处理参考")}),()=>CustomItems.Where(i=>!ExamsOnly||i.Kind=="exam").ToList(),SaveCustomItem,DeleteCustomItem);
  }
 }
}
