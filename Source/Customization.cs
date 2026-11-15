using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SuiteCustomization
{
    public class Item
    {
        public string Id="", Kind="", Title="", Text="", Prefix=""; public bool Exams, Handling;
        public override string ToString() { return Title; }
    }
    public class Kind
    {
        public string Id, Title; public Kind(string id,string title) { Id=id; Title=title; }
        public override string ToString() { return Title; }
    }
    public class Manager
    {
        public Window Window; public ListBox List; public ComboBox Types; public TextBox Title, Body, Prefix, Search; public CheckBox Exams, Handling; public Button SaveButton, DeleteButton;
        private Func<List<Item>> read; private Action<Item> save; private Action<string> delete; private string selected=""; private bool changing, allowPrefix, allowBlankExam; private TextBlock prefixLabel, bodyLabel;
        public Manager(Window owner, Kind[] kinds, Func<List<Item>> get, Action<Item> put, Action<string> remove, bool examinationPrefix=true, bool blankExam=true)
        {
            read=get; save=put; delete=remove; allowPrefix=examinationPrefix; allowBlankExam=blankExam;
            Window=new Window { Owner=owner,Title="自定义项目 · 添加 / 修改 / 删除",Width=960,Height=780,MinWidth=720,MinHeight=640,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=owner.FontFamily,FontSize=14,Resources=owner.Resources,Background=(Brush)new BrushConverter().ConvertFromString("#F3F6F8") };
            Grid root=new Grid { Margin=new Thickness(22) }; root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
            TextBlock intro=new TextBlock { Text="自定义库跨患者保留。这里只保存通用项目与参考文字，当前患者的报告和处理仍在本例填写。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18),Foreground=Brushes.DarkSlateGray }; root.Children.Add(intro);
            Grid content=new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(245) }); content.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(content,1); root.Children.Add(content);
            DockPanel side=new DockPanel { Margin=new Thickness(0,0,18,0) }; Search=Box(40); Search.ToolTip="名称 / 正文 / 拼音 / 首字母"; DockPanel.SetDock(Search,Dock.Top); side.Children.Add(Search);
            Button add=Button("＋ 新建项目",New); DockPanel.SetDock(add,Dock.Bottom); side.Children.Add(add); List=new ListBox { DisplayMemberPath="",Margin=new Thickness(0,10,0,10) }; side.Children.Add(List); content.Children.Add(side);
            StackPanel form=new StackPanel(); Grid.SetColumn(form,1); ScrollViewer scroll=new ScrollViewer { Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; Grid.SetColumn(scroll,1); content.Children.Add(scroll);
            Label(form,"项目类型"); Types=new ComboBox { ItemsSource=kinds,SelectedIndex=0,Margin=new Thickness(0,0,0,12),MinHeight=38 }; form.Children.Add(Types);
            Label(form,"名称 *"); Title=Box(42); form.Children.Add(Title); prefixLabel=Label(form,"检查前缀（留空为名称加冒号）"); Prefix=Box(42); form.Children.Add(Prefix);
            bodyLabel=Label(form,"通用模板 / 手动载入参考"); Body=Box(245); Body.AcceptsReturn=true; Body.TextWrapping=TextWrapping.Wrap; Body.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; form.Children.Add(Body);
            Exams=new CheckBox { Content="自定义病程加入辅助检查入口",Margin=new Thickness(0,12,0,5) }; Handling=new CheckBox { Content="自定义病程加入检查对应的处理入口",Margin=new Thickness(0,0,0,15) }; form.Children.Add(Exams); form.Children.Add(Handling);
            StackPanel actions=new StackPanel { Orientation=Orientation.Horizontal }; SaveButton=Button("保存项目",Save); DeleteButton=Button("删除此自定义项目",Delete); actions.Children.Add(SaveButton); actions.Children.Add(DeleteButton); form.Children.Add(actions);
            form.Children.Add(new TextBlock { Text="内置模板不在此列表中，继续保持原文。保存的参考须手动载入。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,15,0,0) });
            Types.SelectionChanged+=delegate { if (!changing) UpdateType(); }; Search.TextChanged+=delegate { Refresh(); };
            List.SelectionChanged+=delegate { if (changing || !(List.SelectedItem is Item)) return; Item item=(Item)List.SelectedItem; selected=item.Id; changing=true; Types.SelectedItem=kinds.First(k=>k.Id==item.Kind); Types.IsEnabled=false; Title.Text=item.Title; Body.Text=item.Text; Prefix.Text=item.Prefix; Exams.IsChecked=item.Exams; Handling.IsChecked=item.Handling; changing=false; UpdateType(); DeleteButton.IsEnabled=true; };
            Window.Content=root; Refresh(); New();
        }
        private static TextBox Box(double height) { return new TextBox { Height=height,Padding=new Thickness(9),Margin=new Thickness(0,0,0,12),VerticalContentAlignment=VerticalAlignment.Top }; }
        private static TextBlock Label(Panel p,string text) { TextBlock label=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) };p.Children.Add(label);return label; }
        private static Button Button(string label,Action action) { Button b=new Button { Content=label,Padding=new Thickness(13,9,13,9),Margin=new Thickness(0,8,8,0) }; b.Click+=delegate { action(); }; return b; }
        private void UpdateType() { Kind kind=Types.SelectedItem as Kind; bool exam=kind!=null&&kind.Id=="exam",record=kind!=null&&kind.Id=="record"; Prefix.Visibility=prefixLabel.Visibility=exam&&allowPrefix?Visibility.Visible:Visibility.Collapsed; bodyLabel.Text=exam&&allowBlankExam?"手动参考正文（可留空，仅创建检查项目）":"通用模板 / 手动参考正文 *"; Exams.Visibility=Handling.Visibility=record?Visibility.Visible:Visibility.Collapsed; }
        public void New() { selected=""; changing=true; List.SelectedItem=null; Types.IsEnabled=true; Title.Text=Body.Text=Prefix.Text=""; Exams.IsChecked=Handling.IsChecked=false; changing=false; DeleteButton.IsEnabled=false; UpdateType(); }
        public void Refresh() { changing=true; List.ItemsSource=read().Where(i=>Match(i.Title+" "+i.Text,Search.Text)).ToList(); changing=false; }
        public static Func<string,string> Romanize;
        public static bool Match(string text,string query) { string py=Romanize==null?"":Romanize(text); string initials=String.Concat(py.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Select(s=>s[0])); return (query??"").Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).All(q=>(text??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||py.Replace(" ","").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||initials.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0); }
        public void Save() { try { Kind kind=Types.SelectedItem as Kind; if(kind==null||String.IsNullOrWhiteSpace(Title.Text)||((kind.Id!="exam"||!allowBlankExam)&&String.IsNullOrWhiteSpace(Body.Text))) throw new ArgumentException("请填写名称和此类型需要的模板正文。"); Item item=new Item { Id=selected,Kind=kind.Id,Title=Title.Text.Trim(),Text=Body.Text.Trim(),Prefix=Prefix.Text.Trim(),Exams=Exams.IsChecked==true,Handling=Handling.IsChecked==true }; save(item); Refresh(); New(); } catch(Exception ex) { MessageBox.Show(Window,ex.Message,"项目未保存"); } }
        public void Delete() { if(selected.Length==0)return; if(MessageBox.Show(Window,"删除此自定义项目？通用库将移除此项；已粘贴到本例的正文仍保留。","删除项目",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return; try { delete(selected); Refresh(); New(); } catch(Exception ex) { MessageBox.Show(Window,ex.Message,"项目未删除"); } }
    }
}
