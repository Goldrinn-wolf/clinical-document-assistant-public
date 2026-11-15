using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("出院医嘱助手")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
namespace InstructionsAssistant
{
    public static class Program
    {
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        public static Library ReadLibrary() { using (Stream s = Resource("instructions.xml")) return Library.Load(s); }
        [STAThread]
        public static int Main(string[] args)
        {
            bool testing = args.Contains("--self-test") || args.Contains("--preview-test") || args.Contains("--custom-test");
            try
            {
                if (args.Contains("--self-test")) return Tests.Core(args[args.Length - 1]);
                bool first;
                using (Mutex mutex = new Mutex(true, "Local\\InstructionsAssistantPublic_1", out first))
                {
                    if (!first && !testing) { MessageBox.Show("出院医嘱助手已在运行，请从任务栏打开。", "出院医嘱助手"); return 0; }
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += (s, e) => {
                        e.Handled = true;
                        if (testing) { File.WriteAllText(Path.Combine(args[args.Length - 1], "ui-error.txt"), e.Exception.ToString()); app.Shutdown(1); }
                        else MessageBox.Show("操作未完成：" + e.Exception.Message, "出院医嘱助手");
                    };
                    MainWindow window = new MainWindow(testing ? Path.Combine(args[args.Length - 1], "main-profile-" + Guid.NewGuid().ToString("N")) : null);
                    if (testing) window.Window.Loaded += (s, e) => window.Window.Dispatcher.BeginInvoke(new Action(() => (args.Contains("--custom-test") ? (Action)(() => CustomTests.Run(window, args[args.Length - 1])) : () => Tests.UI(window, args[args.Length - 1]))()), DispatcherPriority.ApplicationIdle);
                    int code = app.Run(window.Window);
                    if (first) mutex.ReleaseMutex();
                    return code != 0 ? code : Environment.ExitCode;
                }
            }
            catch (Exception e)
            {
                if (testing) { Directory.CreateDirectory(args[args.Length - 1]); File.WriteAllText(Path.Combine(args[args.Length - 1], "startup-error.txt"), e.ToString()); }
                else MessageBox.Show("无法启动：" + e.Message, "出院医嘱助手");
                return 1;
            }
        }
    }
    public partial class MainWindow
    {
        public Window Window;
        public Library Library;
        public State State;
        public TextBox Search, Preview;
        public string Profile;
        private ListBox steps;
        private StackPanel entries, selected;
        private TextBlock status, previewState;
        private CheckBox searchAll;
        private bool updating, customWritable = true;
        private DispatcherTimer saveTimer, searchTimer;
        public MainWindow(string profile = null)
        {
            Profile = profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstructionsAssistantPublic");
            Library = Program.ReadLibrary(); State = new State();
            using (Stream s = Program.Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(s);
            Search = Find<TextBox>("SearchBox"); Preview = Find<TextBox>("PreviewBox"); steps = Find<ListBox>("StepList"); entries = Find<StackPanel>("EntryList"); selected = Find<StackPanel>("SelectedList"); status = Find<TextBlock>("StatusText"); previewState = Find<TextBlock>("PreviewState"); searchAll = Find<CheckBox>("SearchAll");
            Restore();
            updating = true; Preview.Text = State.Preview; Preview.FontSize = State.FontSize; updating = false;
            UpdateLibraryInfo();
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); Find<ScrollViewer>("EntryScroll").ScrollToTop(); RefreshEntries(); };
            Search.TextChanged += (s, e) => { Find<TextBlock>("SearchHint").Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; searchTimer.Stop(); searchTimer.Start(); };
            searchAll.Checked += (s, e) => RefreshEntries(); searchAll.Unchecked += (s, e) => RefreshEntries();
            Preview.TextChanged += (s, e) => { if (!updating) { State.Edited = true; RefreshPreviewState(); ScheduleSave(); } };
            steps.SelectionChanged += (s, e) => { if (!updating && steps.SelectedItem != null) GoTo((int)((ListBoxItem)steps.SelectedItem).Tag); };
            Find<RadioButton>("PunctureYes").Checked += (s, e) => { if (!updating) SetPuncture(1); };
            Find<RadioButton>("PunctureNo").Checked += (s, e) => { if (!updating) SetPuncture(0); };
            Find<Button>("PreviousButton").Click += (s, e) => GoTo(Math.Max(1, State.Step - 1));
            Find<Button>("NextButton").Click += (s, e) => Next();
            Find<Button>("GenerateButton").Click += (s, e) => Generate(true);
            Find<Button>("CopyButton").Click += (s, e) => Copy();
            Find<Button>("ExportButton").Click += (s, e) => Export();
            Find<Button>("ClearButton").Click += (s, e) => Clear();
            Find<Button>("CustomButton").Click += (s, e) => CustomManager().Window.ShowDialog();
            Find<Button>("HelpButton").Click += (s, e) => Help();
            Find<Button>("SmallerButton").Click += (s, e) => Font(-1);
            Find<Button>("LargerButton").Click += (s, e) => Font(1);
            Window.PreviewKeyDown += KeyDown;
            Window.SizeChanged += (s, e) => Responsive();
            Window.Closing += (s, e) => { saveTimer.Stop(); Save(); };
            Refresh();
        }
        public T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        public static Brush Brush(string s) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s)); }
        private void Restore()
        {
            string customFile = Path.Combine(Profile, "custom-instructions.xml");
            if (File.Exists(customFile)) try { Library.MergeCustom(customFile); } catch {
                try { Library.MergeCustom(customFile + ".bak"); status.Text = "自定义条目异常，已恢复上一份备份"; }
                catch { customWritable = false; status.Text = "自定义条目无法读取，原文件已保留；请备份并修复后再添加"; }
            }
            string draft = Path.Combine(Profile, "draft.xml");
            if (File.Exists(draft)) try { State = State.Load(draft); status.Text = "已恢复上次医嘱草稿"; } catch {
                try { State = State.Load(draft + ".bak"); status.Text = "草稿异常，已恢复上一份备份"; }
                catch { status.Text = "草稿无法读取，原文件已保留"; }
            }
            int removed = State.Selected.RemoveWhere(id => !Library.Entries.Any(e => e.Id == id));
            if (removed > 0) status.Text = "已恢复草稿，部分条目不可用，请重新核对选择";
        }
        public void Save()
        {
            try { State.Preview = Preview.Text; State.FontSize = Preview.FontSize; State.Save(Path.Combine(Profile, "draft.xml")); }
            catch (Exception e) { status.Text = "草稿保存失败：" + e.Message; }
        }
        private void ScheduleSave() { saveTimer.Stop(); saveTimer.Start(); }
        private void UpdateLibraryInfo() { Find<TextBlock>("LibraryInfo").Text = "5 个步骤 · " + Library.Entries.Count + " 个条目 · 离线医嘱模板"; }
        public void Refresh()
        {
            updating = true;
            if (steps.Items.Count == 0) for (int i = 1; i <= 5; i++) steps.Items.Add(new ListBoxItem { Tag = i, Content = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 } });
            foreach (ListBoxItem item in steps.Items)
            {
                int step = (int)item.Tag; int count = Library.SelectedInStep(State, step).Count;
                string label = step == 4 ? State.Puncture < 0 ? "未确认" : State.Puncture == 0 ? "已跳过" : "已做" : count + " 项";
                ((TextBlock)item.Content).Text = step + ". " + Library.StepNames[step - 1] + "\n" + label;
                item.IsSelected = step == State.Step;
            }
            Find<RadioButton>("PunctureYes").IsChecked = State.Puncture == 1; Find<RadioButton>("PunctureNo").IsChecked = State.Puncture == 0;
            updating = false;
            RefreshEntries(); RefreshSelected(); RefreshPreviewState();
        }
        public void GoTo(int step)
        {
            State.Step = Math.Max(1, Math.Min(5, step)); Search.Clear(); Find<ScrollViewer>("EntryScroll").ScrollToTop(); Refresh(); ScheduleSave();
        }
        public void Next()
        {
            string error = null;
            if (State.Step == 1 && Library.SelectedInStep(State, 1).Count == 0) error = "请先选择至少一种基础疾病。";
            if ((State.Step == 2 || State.Step == 3) && Library.SelectedInStep(State, State.Step).Count != 1) error = "请选择本步骤的一条医嘱。";
            if (State.Step == 4 && State.Puncture < 0) error = "请明确选择“已做”或“未做”。";
            if (State.Step == 4 && State.Puncture == 1 && Library.SelectedInStep(State, 4).Count != 1) error = "已做造影，请选择一条适用的穿刺医嘱。";
            if (error != null) { status.Text = error; return; }
            if (State.Step == 5) Generate(true); else GoTo(State.Step + 1);
        }
        public void SetPuncture(int value) { State.Puncture = value; Refresh(); ScheduleSave(); }
        public void Choose(Entry entry, bool value)
        {
            if (value)
            {
                if (entry.Step >= 2 && entry.Step <= 4) State.Selected.RemoveWhere(id => Library.Entries.Any(e => e.Id == id && e.Step == entry.Step));
                State.Selected.Add(entry.Id);
                if (entry.Step == 4) State.Puncture = 1;
            }
            else State.Selected.Remove(entry.Id);
            Refresh(); ScheduleSave();
        }
        public void RefreshEntries()
        {
            bool global = Search.Text.Trim().Length > 0 && searchAll.IsChecked == true;
            Find<TextBlock>("StepTitle").Text = global ? "全步骤搜索结果" : "步骤 " + State.Step + " · " + Library.StepNames[State.Step - 1];
            string[] descriptions = { "可多选，合并为编号1；按模板顺序用分号连接。", "选择一条服药说明，默认为“按服药单服药”。", "选择一条复诊安排，默认使用你指定的原文。", "确认是否做过股动脉造影；未做时不生成该编号。", "可多选，也可直接生成；无选择时省略此项。" };
            Find<TextBlock>("StepDescription").Text = global ? "选择结果进入对应步骤，不清除之前的选择。" : descriptions[State.Step - 1];
            Find<TextBlock>("StepDescription").Visibility = Window.ActualWidth > 0 && Window.ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
            Find<StackPanel>("PuncturePanel").Visibility = State.Step == 4 && !global ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("PreviousButton").IsEnabled = State.Step > 1;
            Find<Button>("NextButton").Content = State.Step == 5 ? "完成并生成" : "下一步";
            entries.Children.Clear();
            List<Entry> results = Library.Entries.Where(e => (global || e.Step == State.Step) && Library.Matches(e, Search.Text)).OrderBy(e => e.Step).ThenBy(e => e.Order).ToList();
            foreach (Entry e in results)
            {
                DockPanel cardContent = new DockPanel();
                Button customize = new Button { Content = "另存", Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "以本条内容为基础，自定义并保存新条目" };
                customize.Click += (s, args) => CreateCustomWindow(e).ShowDialog();
                DockPanel.SetDock(customize, Dock.Right); cardContent.Children.Add(customize);
                StackPanel content = new StackPanel();
                content.Children.Add(new TextBlock { Text = e.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#234C59"), TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = e.Text, FontSize = 13, Foreground = Brush("#637D88"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                content.Children.Add(new TextBlock { Text = (global ? "步骤" + e.Step + " · " : "") + e.Origin, FontSize = 11, Foreground = Brush("#10837D"), Margin = new Thickness(0, 6, 0, 0) });
                if (e.Warning.Length > 0) content.Children.Add(new TextBlock { Text = "核对：" + e.Warning, FontSize = 12, Foreground = Brush("#A37424"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
                if (e.Source.Length > 0) content.Children.Add(new TextBlock { Text = "来源：" + e.Source, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brush("#728994"), Margin = new Thickness(0, 5, 0, 0) });
                CheckBox check = new CheckBox { Content = content, IsChecked = State.Selected.Contains(e.Id) && (e.Step != 4 || State.Puncture == 1), IsEnabled = e.Step != 4 || global || State.Puncture != 0, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(7, 0, 0, 0), Tag = e.Id };
                check.Checked += (s, args) => Window.Dispatcher.BeginInvoke(new Action(() => Choose(e, true)));
                check.Unchecked += (s, args) => Window.Dispatcher.BeginInvoke(new Action(() => Choose(e, false)));
                cardContent.Children.Add(check);
                entries.Children.Add(new Border { Child = cardContent, Background = Brush(check.IsChecked == true ? "#EDF7F5" : "#FAFCFD"), BorderBrush = Brush(check.IsChecked == true ? "#ADD4CE" : "#E7EEF1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 5, 9) });
            }
            if (results.Count == 0) entries.Children.Add(new TextBlock { Text = "没有匹配的条目，可清除搜索或自定义添加。", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#728994"), Margin = new Thickness(8, 20, 8, 0) });
        }
        public void RefreshSelected()
        {
            selected.Children.Clear(); int total = 0;
            for (int step = 1; step <= 5; step++)
            {
                if (step == 4 && State.Puncture != 1) { selected.Children.Add(new TextBlock { Text = "步骤4 · " + (State.Puncture < 0 ? "造影尚未确认" : "未做造影，已跳过"), Foreground = Brush("#728994"), FontSize = 12, Margin = new Thickness(0, 3, 0, 3) }); continue; }
                foreach (Entry e in Library.SelectedInStep(State, step))
                {
                    total++;
                    DockPanel row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    Button remove = new Button { Content = "×", Padding = new Thickness(6, 0, 6, 0), ToolTip = "取消选择", Margin = new Thickness(5, 0, 0, 0) };
                    remove.Click += (s, args) => Choose(e, false); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                    TextBlock text = new TextBlock { Text = "步骤" + step + " · " + e.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#476771"), Cursor = Cursors.Hand, ToolTip = e.Text };
                    text.MouseLeftButtonUp += (s, args) => GoTo(e.Step); row.Children.Add(text); selected.Children.Add(row);
                }
            }
            Find<TextBlock>("SelectedCount").Text = total + " 项";
        }
        public bool Stale { get { return Preview.Text.Length > 0 && State.GeneratedSignature != Library.Signature(State); } }
        private void RefreshPreviewState()
        {
            previewState.Text = Preview.Text.Length == 0 ? "完成选择后生成，可编辑" : Stale ? "选择已变更，请重新生成或保留当前编辑" : State.Edited ? "已手动编辑 · 本机自动保存" : "已生成 · 可编辑后复制";
            previewState.Foreground = Brush(Stale ? "#A37424" : "#728994");
            Find<Button>("CopyButton").IsEnabled = Find<Button>("ExportButton").IsEnabled = Preview.Text.Length > 0;
        }
        public void Generate(bool confirm)
        {
            string error = Library.Validate(State); if (error != null) { status.Text = error; return; }
            if (confirm && State.Edited && Preview.Text.Length > 0 && MessageBox.Show(Window, "重新生成会替换手动编辑的预览，是否继续？", "重新生成", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            updating = true; Preview.Text = Library.Generate(State); updating = false;
            State.Edited = false; State.GeneratedSignature = Library.Signature(State); RefreshPreviewState(); ScheduleSave();
            status.Text = Preview.Text.Contains("[待填写") ? "已生成，请先填写预览中的待填写字段再复制或导出" : "已生成 · 编号连续，步骤内按模板顺序用分号连接";
        }
        public bool Ready(bool confirm)
        {
            if (Preview.Text.Length == 0) { status.Text = "请先生成医嘱文本。"; return false; }
            if (Preview.Text.Contains("[待填写")) { status.Text = "请先填写预览中的待填写字段。"; Preview.Focus(); return false; }
            if (confirm && Stale && MessageBox.Show(Window, "选择已变更，预览仍是之前的文本。是否输出当前预览？", "确认输出", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            return true;
        }
        public void Copy()
        {
            if (!Ready(true)) return;
            for (int i = 0; i < 5; i++) try { Clipboard.SetText(Preview.Text, TextDataFormat.UnicodeText); status.Text = "已复制完整纯文本"; return; } catch { Thread.Sleep(40); }
            status.Text = "剪贴板被占用，请重试或导出TXT。";
        }
        private void Export()
        {
            if (!Ready(true)) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = "出院医嘱_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt" };
            if (dialog.ShowDialog(Window) == true) { File.WriteAllText(dialog.FileName, Preview.Text, new UTF8Encoding(true)); status.Text = "已导出TXT：" + dialog.FileName; }
        }
        private void Clear()
        {
            if (MessageBox.Show(Window, "开始新草稿并清空当前选择、预览？自定义条目将保留。", "新建草稿", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            State = new State(); updating = true; Preview.Clear(); updating = false; Search.Clear(); Refresh(); ScheduleSave(); status.Text = "已开始新草稿，自定义条目已保留";
        }
        private void Font(int delta) { Preview.FontSize = Math.Max(12, Math.Min(24, Preview.FontSize + delta)); ScheduleSave(); }
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
            Grid body = Find<Grid>("Body"); bool small = Window.ActualWidth < 1080;
            Border panel = Find<Border>("PreviewPanel"); Border stepPanel = Find<Border>("StepsPanel");
            body.ColumnDefinitions[0].Width = new GridLength(small ? 155 : 185);
            body.ColumnDefinitions[3].Width = new GridLength(small ? 0 : Math.Min(470, Window.ActualWidth * 0.35));
            body.ColumnDefinitions[2].Width = new GridLength(small ? 0 : 14);
            bool narrow = Window.ActualWidth < 900;
            body.RowDefinitions[0].Height = new GridLength(small && !narrow ? 0.8 : 1, GridUnitType.Star);
            body.RowDefinitions[1].Height = new GridLength(small ? 10 : 0);
            body.RowDefinitions[2].Height = small ? new GridLength(narrow ? 1 : 1.2, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(panel, small ? 2 : 0); Grid.SetColumn(panel, small ? 1 : 3); Grid.SetColumnSpan(panel, small ? 3 : 1); Grid.SetRowSpan(stepPanel, small ? 3 : 1);
            Find<ScrollViewer>("SelectedScroll").MaxHeight = narrow ? 36 : small ? 60 : 125;
            Find<TextBlock>("StepDescription").Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("StepTitle").FontSize = narrow ? 17 : 19;
            previewState.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            Preview.Padding = new Thickness(narrow ? 5 : 10);
            Find<Button>("PreviousButton").Padding = Find<Button>("NextButton").Padding = new Thickness(12, narrow ? 6 : 9, 12, narrow ? 6 : 9);
            panel.Padding = new Thickness(small ? 12 : 16);
            Find<Button>("CopyButton").Padding = Find<Button>("ExportButton").Padding = new Thickness(12, small ? 5 : 9, 12, small ? 5 : 9);
        }
        public Entry AddCustom(int step, string title, string body, string pinyin)
        {
            if (!customWritable) throw new InvalidOperationException("自定义条目文件无法读取，暂不能覆盖保存。");
            Entry e = Library.Add(step, title, body, pinyin);
            try { Library.SaveCustom(Path.Combine(Profile, "custom-instructions.xml")); } catch { Library.Entries.Remove(e); throw; }
            GoTo(step); UpdateLibraryInfo(); status.Text = "已保存自定义条目，可勾选使用：" + e.Title; return e;
        }
        public Window CreateCustomWindow(Entry original)
        {
            Window w = new Window { Title = "自定义医嘱条目", Owner = Window, Width = 620, Height = 650, MinWidth = 480, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Window.FontFamily, FontSize = 14, Background = Brush("#F3F6F8") };
            StackPanel p = new StackPanel { Margin = new Thickness(24) };
            p.Children.Add(new TextBlock { Text = original == null ? "添加可重复使用的医嘱" : "以原条目为基础另存医嘱", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#254C59") });
            p.Children.Add(new TextBlock { Text = "选择所属步骤", Margin = new Thickness(0, 16, 0, 5) });
            ComboBox step = new ComboBox { ItemsSource = Library.StepNames, SelectedIndex = original == null ? State.Step - 1 : original.Step - 1, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) }; p.Children.Add(step);
            TextBox title = Field(p, "条目名称 *", false); title.Text = original == null ? "" : original.Title + "（自定义）";
            TextBox body = Field(p, "完整医嘱正文 *（不填写外层1、2等编号）", true); body.Text = original == null ? "" : original.Text;
            TextBox py = Field(p, "拼音（选填，用空格分隔每字，可修正多音字）", false);
            p.Children.Add(new TextBlock { Text = "新条目保留在本机，重启及新建草稿后仍可使用。\n步骤1、5的疾病名称应写在正文中；拼音留空时自动生成。", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#728994"), Margin = new Thickness(0, 0, 0, 14) });
            Button save = new Button { Content = "保存条目", Background = Brush("#10837D"), Foreground = Brushes.White };
            save.Click += (s, e) => { try { AddCustom(step.SelectedIndex + 1, title.Text, body.Text, py.Text); w.Close(); } catch (Exception ex) { MessageBox.Show(w, ex.Message, "未保存"); } }; p.Children.Add(save);
            w.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; return w;
        }
        private static TextBox Field(StackPanel p, string label, bool multiline)
        {
            p.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            TextBox box = new TextBox { Height = multiline ? 180 : 40, TextWrapping = TextWrapping.Wrap, AcceptsReturn = multiline, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 12) }; p.Children.Add(box); return box;
        }
        private void Help()
        {
            MessageBox.Show(Window, "步骤1：基础疾病可多选，合并为编号1。\n步骤2：默认“按服药单服药”。\n步骤3：默认神经内科复诊原文。\n步骤4：确认是否股动脉造影；未做时跳过。\n步骤5：其他病症可多选，无选择时省略。\n\n编号连续，多条按模板顺序用分号连接。原文条目需结合患者情况核对；网络补充条目已在卡片标注来源。血栓条目中的VTE评分需在预览填写后才能输出。\n\n每步骤都可自定义添加；卡片“另存”可修改内容后保存为新条目。右侧选择框可取消条目、点击名称返回所在步骤。更改选择不会自动覆盖手动编辑的预览。\n\nCtrl+F 搜索；Ctrl+Enter 生成\nCtrl+Shift+C 复制全文；Ctrl+S 导出TXT\nTab 切换控件；空格勾选；Esc 清除搜索\n\n版本1.0.0 · 本机草稿及自定义条目：\n" + Profile + "\n\n目标：Windows7 SP1 + .NET Framework4.8。Windows7尚未实机验证。", "使用说明");
        }
    }
}
