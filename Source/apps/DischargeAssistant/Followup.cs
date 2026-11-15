using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Xml;using System.Xml.Linq;using System.Windows;using System.Windows.Controls;
namespace DischargeAssistant
{
 public partial class MainWindow
 {
  public Dictionary<string,string> Followups=new Dictionary<string,string>();public string ActiveFollowup="模板一";private bool followupWritable=true;
  private string FollowupPath {get{return Path.Combine(Path.GetDirectoryName(draftPath),"followup-templates.xml");}}
  private void RestoreFollowups()
  {
   using(var stream=Program.Resource("followup-defaults.xml")) {var defaults=XDocument.Load(stream);Followups=defaults.Root.Elements("Template").ToDictionary(e=>(string)e.Attribute("name"),e=>e.Value);}
   string secondDefault=Followups["模板二"];Followups["模板一"]=Library.Intro;
   if((!testMode||isolatedProfile)&&File.Exists(FollowupPath)) {try{ReadFollowups(FollowupPath);}catch{try{ReadFollowups(FollowupPath+".bak");status.Text="复诊模板已恢复备份，请核对。";}catch{followupWritable=false;status.Text="复诊模板文件无法读取，原文件保留；请先恢复后再修改。";}}}
   if(String.IsNullOrWhiteSpace(Followups["模板二"]))Followups["模板二"]=secondDefault;
   if(!String.IsNullOrWhiteSpace(Followups[ActiveFollowup]))Library.Intro=Followups[ActiveFollowup];UpdateFollowupButtons();
  }
  private void ReadFollowups(string path)
  {
   XDocument doc;using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc=XDocument.Load(reader);
   if(doc.Root==null||doc.Root.Name!="Followups")throw new InvalidDataException("模板格式无效。");var next=new Dictionary<string,string>();foreach(var e in doc.Root.Elements("Template")){string name=(string)e.Attribute("name");if(!new[]{"模板一","模板二"}.Contains(name)||next.ContainsKey(name))throw new InvalidDataException("模板名称无效或重复。");next.Add(name,e.Value);}string active=(string)doc.Root.Attribute("active");if(next.Count!=2||!next.ContainsKey(active)||String.IsNullOrWhiteSpace(next[active]))throw new InvalidDataException("当前模板无效。");Followups=next;ActiveFollowup=active;
  }
  private void PersistFollowups(Dictionary<string,string> values,string active)
  {
   if(!followupWritable)throw new IOException("复诊模板文件无法读取，暂不能覆盖。");Directory.CreateDirectory(Path.GetDirectoryName(FollowupPath));string temp=FollowupPath+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var writer=XmlWriter.Create(temp,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,NewLineHandling=NewLineHandling.Entitize}))new XDocument(new XElement("Followups",new XAttribute("active",active),values.Select(p=>new XElement("Template",new XAttribute("name",p.Key),p.Value)))).Save(writer);if(File.Exists(FollowupPath))File.Replace(temp,FollowupPath,FollowupPath+".bak",true);else File.Move(temp,FollowupPath);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public void SaveFollowup(string name,string text,bool activate)
  {
   if(!Followups.ContainsKey(name)||String.IsNullOrWhiteSpace(text))throw new ArgumentException("请粘贴完整模板正文后保存。");var next=new Dictionary<string,string>(Followups);next[name]=text.Trim();string active=activate?name:ActiveFollowup;PersistFollowups(next,active);Followups=next;ActiveFollowup=active;Library.Intro=Followups[active];Stale=Preview.Text.Length>0;UpdateFollowupButtons();UpdatePreviewState();SaveDraft();
  }
  public void SwitchFollowup(string name)
  {
   if(!Followups.ContainsKey(name)||String.IsNullOrWhiteSpace(Followups[name]))throw new InvalidOperationException("该模板尚未填写，请先粘贴正文并保存。");PersistFollowups(Followups,name);ActiveFollowup=name;Library.Intro=Followups[name];Stale=Preview.Text.Length>0;UpdateFollowupButtons();UpdatePreviewState();SaveDraft();status.Text="已切换为"+name+"模板，重新生成后即可复制。";
  }
  private void UpdateFollowupButtons(){Find<Button>("TemplateOneButton").Content="某某主任 · 模板一"+(ActiveFollowup=="模板一"?" ✓":"");Find<Button>("TemplateTwoButton").Content="某某主任 · 模板二"+(ActiveFollowup=="模板二"?" ✓":"");}
  public Window FollowupDialog(string selected=null)
  {
   var w=new Window{Owner=Window,Title="某某主任 · 编辑默认模板",Width=760,Height=610,MinWidth=560,MinHeight=470,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=Window.FontFamily,Resources=Window.Resources,Background=Brush("#F3F6F8")};var grid=new Grid{Margin=new Thickness(22)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var hint=new TextBlock{Text="两份模板分别保存，切换后作为带药正文开头。可粘贴整段通用文字；不填写未知门诊时间。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};grid.Children.Add(hint);var names=new ComboBox{ItemsSource=new[]{"模板一","模板二"},SelectedItem=selected??ActiveFollowup,MinHeight=38,Margin=new Thickness(0,0,0,14)};Grid.SetRow(names,1);grid.Children.Add(names);var body=new TextBox{Text=Followups[(string)names.SelectedItem],AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};Grid.SetRow(body,2);grid.Children.Add(body);
   var edits=new Dictionary<string,string>(Followups);string current=(string)names.SelectedItem;names.SelectionChanged+=delegate{edits[current]=body.Text;current=(string)names.SelectedItem;body.Text=edits[current];};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};
   var cancel=new Button{Content="取消"};cancel.Click+=delegate{w.Close();};actions.Children.Add(cancel);var save=new Button{Content="保存两份模板并使用当前模板"};save.Click+=delegate{try{edits[current]=body.Text;if(String.IsNullOrWhiteSpace(edits[current]))throw new ArgumentException("当前模板正文不能为空。");PersistFollowups(edits,current);Followups=edits;ActiveFollowup=current;Library.Intro=edits[current];Stale=Preview.Text.Length>0;UpdateFollowupButtons();UpdatePreviewState();SaveDraft();w.Close();}catch(Exception ex){MessageBox.Show(w,ex.Message,"未保存");}};actions.Children.Add(save);Grid.SetRow(actions,3);grid.Children.Add(actions);w.Content=grid;return w;
  }
 }
}
