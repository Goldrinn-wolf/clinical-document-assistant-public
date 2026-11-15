using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Threading;

[assembly: AssemblyTitle("出院带药助手")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
namespace DischargeAssistant
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--self-test")) return Tests.Run(args);
                bool fixTest=args.Contains("--fix-test"); bool testing = args.Contains("--preview-test") || fixTest;
                bool first;
                using (Mutex mutex = new Mutex(true, "Local\\DischargeAssistantPublic_1", out first))
                {
                    if (!first && !testing) { MessageBox.Show("出院带药助手已在运行，请从任务栏打开。", "出院带药助手"); return 0; }
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += (s, e) => { if (testing) { File.WriteAllText(Path.Combine(args[args.Length - 1], "ui-error.txt"), e.Exception.ToString()); e.Handled = true; app.Shutdown(1); return; } MessageBox.Show("操作未完成：" + e.Exception.Message, "出院带药助手"); e.Handled = true; };
                    MainWindow window = new MainWindow(testing, fixTest ? Path.Combine(args[args.Length-1],"profile","draft.xml") : null);
                    if (testing) window.Window.Loaded += (s, e) => window.Window.Dispatcher.BeginInvoke(new Action(() => (fixTest ? (Action)(()=>FixTests.Run(window,args[args.Length-1])) : ()=>Tests.Preview(window,args))()), DispatcherPriority.ApplicationIdle);
                    app.Run(window.Window);
                    if (first) mutex.ReleaseMutex();
                }
                return testing ? Environment.ExitCode : 0;
            }
            catch (Exception e) { if (args.Length > 1) { Directory.CreateDirectory(args[args.Length - 1]); File.WriteAllText(Path.Combine(args[args.Length - 1], "startup-error.txt"), e.ToString()); return 1; } MessageBox.Show("无法启动：" + e.Message + "\n请检查程序文件是否完整。", "出院带药助手"); return 1; }
        }
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        public static Library ReadLibrary()
        {
            using (Stream s = Resource("library.xml")) return Library.Load(s);
        }
    }
    public partial class MainWindow
    {
        public Window Window;
        public Library Library;
        public HashSet<string> Selected = new HashSet<string>();
        public TextBox Search, Preview;
        public bool Stale, Edited;
        public string GeneratedIds = "";
        private ListBox categories;
        private StackPanel drugs;
        private CheckBox currentOnly;
        private TextBlock title, resultInfo, status, previewState;
        private Button selectedButton;
        private Category active;
        private bool selectedMode, reviewMode, restoring, testMode, isolatedProfile;
        private DispatcherTimer searchTimer, saveTimer;
        private string draftPath;
        public MainWindow(bool testing, string isolatedDraftPath = null)
        {
            testMode = testing;
            Library = Program.ReadLibrary();
            using (Stream stream = Program.Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            Search = Find<TextBox>("SearchBox"); Preview = Find<TextBox>("PreviewBox"); categories = Find<ListBox>("CategoryList"); drugs = Find<StackPanel>("DrugList"); currentOnly = Find<CheckBox>("CurrentOnly");
            title = Find<TextBlock>("ListTitle"); resultInfo = Find<TextBlock>("ResultInfo"); status = Find<TextBlock>("StatusText"); previewState = Find<TextBlock>("PreviewState"); selectedButton = Find<Button>("SelectedButton");
            Search.ToolTip = "搜索全库：中文 / 全拼 / 首字母；Ctrl+F。输入多个词可缩小范围。";
            isolatedProfile = isolatedDraftPath != null;
            draftPath = isolatedDraftPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DischargeAssistantPublic", "draft.xml");
            customPath = Path.Combine(Path.GetDirectoryName(draftPath), "custom-library.xml");
            if (!testing || isolatedProfile) RestoreCustom();
            UpdateLibraryInfo();
            if (!testing || isolatedProfile) Restore();
            if (active == null) active = Library.Categories[0];
            RestoreFollowups();
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); Find<ScrollViewer>("DrugScroll").ScrollToTop(); RefreshDrugs(); };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveDraft(); };
            Search.TextChanged += (s, e) => { Find<TextBlock>("SearchHint").Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; searchTimer.Stop(); searchTimer.Start(); };
            Preview.TextChanged += (s, e) => { if (!restoring) { Edited = true; UpdatePreviewState(); ScheduleSave(); } };
            currentOnly.Checked += (s, e) => RefreshDrugs(); currentOnly.Unchecked += (s, e) => RefreshDrugs();
            categories.SelectionChanged += (s, e) => {
                if (restoring || categories.SelectedItem == null) return;
                active = (Category)((ListBoxItem)categories.SelectedItem).Tag;
                selectedMode = reviewMode = false; Find<ScrollViewer>("DrugScroll").ScrollToTop(); RefreshDrugs(); ScheduleSave();
            };
            selectedButton.Click += (s, e) => { selectedMode = !selectedMode; reviewMode = false; RefreshDrugs(); };
            Find<Button>("ReviewButton").Click += (s, e) => { reviewMode = !reviewMode; selectedMode = false; RefreshDrugs(); };
            Find<Button>("TemplateOneButton").Click += delegate { try { SwitchFollowup("模板一"); } catch(Exception ex) { MessageBox.Show(Window,ex.Message); } };
            Find<Button>("TemplateTwoButton").Click += delegate { if(String.IsNullOrWhiteSpace(Followups["模板二"])) FollowupDialog("模板二").ShowDialog(); else SwitchFollowup("模板二"); };
            Find<Button>("TemplatesButton").Click += delegate { FollowupDialog().ShowDialog(); };
            Find<Button>("HelpButton").Click += (s, e) => Help();
            Find<Button>("CustomButton").Click += (s, e) => ShowCustom();
            Find<Button>("GenerateButton").Click += (s, e) => Generate(true);
            Find<Button>("CopyButton").Click += (s, e) => Copy();
            Find<Button>("ExportButton").Click += (s, e) => Export();
            Find<Button>("ClearButton").Click += (s, e) => Clear();
            Find<Button>("SmallerButton").Click += (s, e) => ChangeFont(-1);
            Find<Button>("LargerButton").Click += (s, e) => ChangeFont(1);
            Window.PreviewKeyDown += KeyDown;
            Window.SizeChanged += (s, e) => Responsive();
            Window.Closing += (s, e) => { saveTimer.Stop(); SaveDraft(); };
            RefreshCategories(); RefreshDrugs(); UpdatePreviewState();
        }
        public T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        private void Restore()
        {
            string message = null;
            Draft d = null;
            if (File.Exists(draftPath))
            {
                try { d = Draft.Load(draftPath); }
                catch { if (File.Exists(draftPath + ".bak")) try { d = Draft.Load(draftPath + ".bak"); message = "草稿异常，已恢复上一份备份"; } catch { } }
                if (d == null) { message = "草稿无法读取，原文件已保留；本次使用空白草稿"; }
            }
            if (d != null)
            {
                Selected = new HashSet<string>(d.Selected.Where(id => Library.Drugs.Any(x => x.Id == id)));
                active = Library.Categories.FirstOrDefault(c => c.Id == d.CategoryId);
                restoring = true; Preview.Text = d.Preview; Preview.FontSize = d.FontSize; restoring = false;
                Stale = d.Stale || Selected.Count != d.Selected.Count; Edited = d.Edited; GeneratedIds = d.GeneratedIds;
                if (!String.IsNullOrWhiteSpace(d.Intro)) Library.Intro = d.Intro;
                message = message ?? "已恢复上次草稿 · 选择和手动编辑内容均已保留";
            }
            if (message != null) status.Text = message;
        }
        public void SaveDraft()
        {
            if (testMode && !isolatedProfile) return;
            try { new Draft { Selected = Selected, Preview = Preview.Text, Intro = Library.Intro, CategoryId = active.Id, FontSize = Preview.FontSize, Stale = Stale, Edited = Edited, GeneratedIds = GeneratedIds }.Save(draftPath); }
            catch (Exception e) { status.Text = "草稿保存失败：" + e.Message; }
        }
        private void ScheduleSave() { if (saveTimer == null) return; saveTimer.Stop(); saveTimer.Start(); }
        private void RefreshCategories()
        {
            restoring = true;
            if (categories.Items.Count == 0) foreach (Category c in Library.Categories)
            {
                TextBlock text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                ListBoxItem item = new ListBoxItem { Content = text, Tag = c, ToolTip = c.Title + "（" + c.Drugs.Count + "项）" };
                categories.Items.Add(item);
                if (active == c) item.IsSelected = true;
            }
            foreach (ListBoxItem item in categories.Items)
            {
                Category c = (Category)item.Tag;
                int count = c.Drugs.Count(d => Selected.Contains(d.Id));
                ((TextBlock)item.Content).Text = c.Title.Split('：')[0] + (count > 0 ? "  · " + count : "");
            }
            restoring = false;
            selectedButton.Content = "已选 " + Selected.Count + " 项 · 查看";
        }
        public void RefreshDrugs()
        {
            IEnumerable<Drug> list = Library.Drugs;
            bool searching = !String.IsNullOrWhiteSpace(Search.Text);
            if (selectedMode) list = list.Where(d => Selected.Contains(d.Id));
            else if (reviewMode) list = list.Where(d => d.Warnings.Count > 0);
            else if (!searching || currentOnly.IsChecked == true) list = list.Where(d => d.Category == active);
            if (searching) list = list.Where(d => Library.Matches(d, Search.Text));
            List<Drug> results = list.ToList();
            title.Text = selectedMode ? "已选药物" : reviewMode ? "待核对条目" : searching && currentOnly.IsChecked != true ? "全库搜索结果" : active.Title;
            resultInfo.Text = results.Count + " 项" + (searching ? " · 搜索不会清除已有选择" : " · 勾选后可切换分类继续选择");
            double offset = Find<ScrollViewer>("DrugScroll").VerticalOffset;
            drugs.Children.Clear();
            string lastGroup = null;
            foreach (Drug d in results)
            {
                string group = d.Category.Title + (String.IsNullOrEmpty(d.Section) ? "" : " / " + d.Section);
                if (group != lastGroup && (searching || selectedMode || reviewMode || !String.IsNullOrEmpty(d.Section)))
                    drugs.Children.Add(new TextBlock { Text = group, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#738B94"), FontSize = 12, Margin = new Thickness(2, 10, 2, 8) });
                lastGroup = group;
                CheckBox check = new CheckBox { IsChecked = Selected.Contains(d.Id), Tag = d.Id, Margin = new Thickness(0, 2, 0, 2), VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 0, 0, 0) };
                StackPanel content = new StackPanel();
                content.Children.Add(new TextBlock { Text = d.Name, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = Brush("#234C59"), FontSize = 15 });
                content.Children.Add(new TextBlock { Text = d.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#637D88"), FontSize = 13, Margin = new Thickness(0, 6, 0, 0) });
                foreach (string n in d.Notes) content.Children.Add(new TextBlock { Text = n, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#627F89"), FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
                if (d.Warnings.Count > 0) content.Children.Add(new TextBlock { Text = "待核对 · " + String.Join(" ", d.Warnings), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A37424"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
                check.Content = content;
                Border card = new Border { Child = check, Background = Brush(Selected.Contains(d.Id) ? "#EDF7F5" : "#FAFCFD"), BorderBrush = Brush(Selected.Contains(d.Id) ? "#ADD4CE" : "#E7EEF1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 5, 9) };
                check.Checked += (s, e) => { SetSelected(d.Id, true); card.Background = Brush("#EDF7F5"); card.BorderBrush = Brush("#ADD4CE"); };
                check.Unchecked += (s, e) => { SetSelected(d.Id, false); card.Background = Brush("#FAFCFD"); card.BorderBrush = Brush("#E7EEF1"); if (selectedMode) Window.Dispatcher.BeginInvoke(new Action(RefreshDrugs)); };
                drugs.Children.Add(card);
            }
            if (results.Count == 0) drugs.Children.Add(new TextBlock { Text = selectedMode ? "还没有选中药物。\n点击左侧分类开始选择。" : "没有匹配条目。\n可清除搜索，或取消“仅当前分类”。", Foreground = Brush("#7C929B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 24, 8, 0) });
            if (!searching && !selectedMode && !reviewMode && active.Notes.Count > 0)
                drugs.Children.Add(new Border { Background = Brush("#F0F6F8"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 6, 5, 0), Child = new TextBlock { Text = "本分类共用备注\n" + String.Join("\n", active.Notes), Foreground = Brush("#577783"), FontSize = 12, TextWrapping = TextWrapping.Wrap } });
            Find<ScrollViewer>("DrugScroll").ScrollToVerticalOffset(Math.Min(offset, Find<ScrollViewer>("DrugScroll").ScrollableHeight));
        }
        public void SetSelected(string id, bool value)
        {
            if (value) Selected.Add(id); else Selected.Remove(id);
            Stale = Preview.Text.Length > 0 && SelectionSignature() != GeneratedIds;
            RefreshCategories(); UpdatePreviewState(); ScheduleSave();
        }
        public string SelectionSignature() { return String.Join(",", Selected.OrderBy(id => id)) + "|" + Library.Intro; }
        public void Generate(bool confirm)
        {
            if (Selected.Count == 0) { status.Text = "请先选择至少一个药物模板"; return; }
            if (confirm && Edited && Preview.Text.Length > 0 && MessageBox.Show(Window, "重新生成会替换手动编辑的预览内容。是否继续？", "重新生成", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            restoring = true; Preview.Text = Library.Generate(Selected); restoring = false;
            Stale = Edited = false; GeneratedIds = SelectionSignature(); UpdatePreviewState(); ScheduleSave();
            int count = Library.Drugs.Count(d => Selected.Contains(d.Id) && d.Warnings.Count > 0);
            status.Text = "已生成 " + Selected.Count + " 项 · " + (count > 0 ? count + " 项原文待核对，可在药物卡片查看" : "已按原模板顺序排列");
        }
        private void UpdatePreviewState()
        {
            previewState.Text = Preview.Text.Length == 0 ? "选择药物后点击生成" : Stale ? "选择已变更，请重新生成或保留当前编辑" : Edited ? "已手动编辑 · 自动保存草稿" : "已生成 · 可继续编辑";
            previewState.Foreground = Brush(Stale ? "#A37424" : "#728994");
            Find<Button>("CopyButton").IsEnabled = Find<Button>("ExportButton").IsEnabled = Preview.Text.Length > 0;
        }
        private bool ReadyToOutput()
        {
            if (Preview.Text.Length == 0) { status.Text = "请先生成带药文本"; return false; }
            if (Stale && MessageBox.Show(Window, "选择已变更，当前预览仍是之前的文本。是否输出当前预览？\n选择“否”后可点击一键生成更新。", "确认输出内容", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            return true;
        }
        public void Copy()
        {
            if (!ReadyToOutput()) return;
            try { Clipboard.SetText(Preview.Text, TextDataFormat.UnicodeText); status.Text = "已复制完整纯文本，可粘贴到病历系统"; }
            catch { status.Text = "剪贴板暂时被占用，请稍后重试；也可使用导出 TXT"; }
        }
        public void Export()
        {
            if (!ReadyToOutput()) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "出院带药_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt", DefaultExt = ".txt", AddExtension = true };
            if (dialog.ShowDialog(Window) == true) { File.WriteAllText(dialog.FileName, Preview.Text, new UTF8Encoding(true)); status.Text = "已导出 TXT：" + dialog.FileName; }
        }
        public void Clear()
        {
            if (Selected.Count == 0 && Preview.Text.Length == 0) return;
            if (MessageBox.Show(Window, "清空已选药物和预览内容，开始新草稿？", "清空草稿", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Selected.Clear(); restoring = true; Preview.Clear(); Search.Clear(); restoring = false; Stale = Edited = false; GeneratedIds = "";
            selectedMode = reviewMode = false; RefreshCategories(); RefreshDrugs(); UpdatePreviewState(); ScheduleSave(); status.Text = "草稿已清空";
        }
        private void ChangeFont(int delta) { Preview.FontSize = Math.Max(12, Math.Min(24, Preview.FontSize + delta)); ScheduleSave(); }
        private void KeyDown(object s, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { Search.Focus(); Search.SelectAll(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) { Generate(true); e.Handled = true; }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) { Copy(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Export(); e.Handled = true; }
            else if (e.Key == Key.Escape && Search.IsKeyboardFocused) { Search.Clear(); e.Handled = true; }
        }
        public void Responsive()
        {
            bool small = Window.ActualWidth < 1080;
            Grid body = Find<Grid>("Body");
            Find<Border>("PreviewPanel").Padding = small ? new Thickness(14, 12, 14, 12) : new Thickness(18, 16, 18, 16);
            Find<TextBlock>("PreviewHint").Visibility = small ? Visibility.Collapsed : Visibility.Visible;
            Find<StackPanel>("PreviewActions").Margin = new Thickness(0, small ? 6 : 10, 0, 0);
            Find<Grid>("PreviewHeader").Margin = new Thickness(0, 0, 0, small ? 6 : 10);
            Find<Button>("CopyButton").Padding = Find<Button>("ExportButton").Padding = small ? new Thickness(10, 6, 10, 6) : new Thickness(14, 9, 14, 9);
            Preview.Padding = new Thickness(small ? 8 : 12);
            if (small)
            {
                body.ColumnDefinitions[0].Width = new GridLength(185); body.ColumnDefinitions[3].Width = new GridLength(0); body.ColumnDefinitions[2].Width = new GridLength(0);
                body.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); body.RowDefinitions[1].Height = new GridLength(12); body.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
                Grid.SetRow(Find<Border>("PreviewPanel"), 2); Grid.SetColumn(Find<Border>("PreviewPanel"), 1); Grid.SetColumnSpan(Find<Border>("PreviewPanel"), 3); Grid.SetRowSpan(Find<Border>("CategoryPanel"), 3);
            }
            else
            {
                body.ColumnDefinitions[0].Width = new GridLength(210); body.ColumnDefinitions[3].Width = new GridLength(Math.Min(460, Window.ActualWidth * 0.35)); body.ColumnDefinitions[2].Width = new GridLength(14);
                body.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); body.RowDefinitions[1].Height = body.RowDefinitions[2].Height = new GridLength(0);
                Grid.SetRow(Find<Border>("PreviewPanel"), 0); Grid.SetColumn(Find<Border>("PreviewPanel"), 3); Grid.SetColumnSpan(Find<Border>("PreviewPanel"), 1); Grid.SetRowSpan(Find<Border>("CategoryPanel"), 1);
            }
        }
        private void Help()
        {
            Window w = new Window { Title = "使用说明与复诊说明", Owner = Window, Width = 640, Height = 570, MinWidth = 500, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Window.FontFamily, Background = Brush("#F3F6F8") };
            StackPanel p = new StackPanel { Margin = new Thickness(22) };
            p.Children.Add(new TextBlock { Text = "选择 → 生成 → 核对编辑 → 复制或导出", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#234C59"), TextWrapping = TextWrapping.Wrap });
            p.Children.Add(new TextBlock { Text = "跨分类多选，搜索后选择仍保留。点击左下角已选数量查看和取消。输出按Word分类和类内顺序排列，类别间空一行。待核对条目保留原文。\n\nCtrl+F 搜索 · Ctrl+Enter 生成\nCtrl+Shift+C 复制全文 · Ctrl+S 导出TXT\nTab 切换控件 · 空格勾选 · Esc 清除搜索\n\n草稿在本机自动保存，关闭后可恢复。更改选择后需重新生成；手动编辑不会被自动覆盖。\n\n复诊说明始终在生成文本最前，可在下方修改并保存：", Margin = new Thickness(0, 14, 0, 14), Foreground = Brush("#587680"), TextWrapping = TextWrapping.Wrap });
            TextBox intro = new TextBox { Text = Library.Intro, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 115, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            p.Children.Add(intro);
            Button save = new Button { Content = "保存复诊说明", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
            save.Click += (s, e) => { if (String.IsNullOrWhiteSpace(intro.Text)) { MessageBox.Show(w, "复诊说明不能为空。"); return; } try { SaveFollowup(ActiveFollowup, intro.Text, true); w.Close(); } catch(Exception ex) { MessageBox.Show(w, ex.Message, "未保存"); } };
            p.Children.Add(save);
            p.Children.Add(new TextBlock { Text = "运行环境：Windows 7 SP1 / 8.1 / 10 / 11 + .NET Framework 4.8。Windows 7、8.1、10 尚未实机验证。\n版本 1.1.0 · 草稿路径：" + draftPath, Margin = new Thickness(0, 16, 0, 0), Foreground = Brush("#728994"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            w.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; w.ShowDialog();
        }
        public static Brush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
    }
}
