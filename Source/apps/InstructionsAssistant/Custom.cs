using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Windows; using SuiteCustomization;
namespace InstructionsAssistant
{
 public partial class MainWindow
 {
  public Manager CustomManager()
  {
   Manager.Romanize=PinyinLookup.Convert;
   return new Manager(Window,Library.StepNames.Select((s,i)=>new Kind((i+1).ToString(),s)).ToArray(),()=>Library.Entries.Where(e=>e.Custom).Select(e=>new Item { Id=e.Id,Kind=e.Step.ToString(),Title=e.Title,Text=e.Text }).ToList(),SaveCustomItem,DeleteCustomItem);
  }
  public void SaveCustomItem(Item item)
  {
   if(!customWritable)throw new InvalidOperationException("自定义库无法读取，请先恢复库文件。");
   int step; if(!Int32.TryParse(item.Kind,out step)||step<1||step>5||String.IsNullOrWhiteSpace(item.Title)||String.IsNullOrWhiteSpace(item.Text))throw new ArgumentException("请填写有效步骤、名称和正文。");
   List<Entry> old=Library.Entries; Library.Entries=old.Where(e=>e.Id!=item.Id).ToList();
   try { Entry added=Library.Add(step,item.Title,item.Text,""); if(item.Id.Length>0) { if(!old.Any(e=>e.Id==item.Id&&e.Custom))throw new ArgumentException("只能修改自定义条目。"); added.Id=item.Id; added.Order=old.First(e=>e.Id==item.Id).Order; } Library.SaveCustom(Path.Combine(Profile,"custom-instructions.xml")); } catch { Library.Entries=old; throw; }
   UpdateLibraryInfo(); RefreshEntries(); RefreshSelected(); State.GeneratedSignature=""; RefreshPreviewState(); ScheduleSave();
  }
  public void DeleteCustomItem(string id)
  {
   if(!customWritable||!Library.Entries.Any(e=>e.Id==id&&e.Custom))throw new ArgumentException("只能删除可读取的自定义条目。"); List<Entry> old=Library.Entries; Library.Entries=old.Where(e=>e.Id!=id).ToList(); try { Library.SaveCustom(Path.Combine(Profile,"custom-instructions.xml")); } catch { Library.Entries=old;throw; }
   State.Selected.Remove(id); State.GeneratedSignature=""; UpdateLibraryInfo(); RefreshEntries(); RefreshSelected(); RefreshPreviewState(); ScheduleSave();
  }
 }
}
