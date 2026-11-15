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

namespace ProgressNotesAssistant
{
    public static class Program
    {
        public static bool ExamsOnly;
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        public static Library ReadLibrary() { using (Stream stream = Resource("library.xml")) return Library.Load(stream); }
        [STAThread] public static int Main(string[] args)
        {
            bool clipboardTest = args.Length > 0 && args[0] == "--clipboard-test";
            bool test = args.Length > 0 && (args[0] == "--preview-test" || args[0] == "--custom-test" || args[0] == "--fix-test" || args[0] == "--category-test" || args[0] == "--smart-test" || clipboardTest);
            string testDir = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "ProgressNotesTests");
            if (args.Length > 0 && args[0] == "--self-test") return CoreTests.Run(testDir);
            bool first; using (Mutex mutex = new Mutex(true, test ? "Local\\ProgressNotesTest_" + Guid.NewGuid().ToString("N") : (ExamsOnly ? "Local\\ExaminationAssistantPublic_1" : "Local\\ProgressNotesAssistantPublic_1"), out first))
            {
                if (!first) { MessageBox.Show("日常病程助手已打开。", "日常病程助手"); return 0; }
                Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; if (test) { Directory.CreateDirectory(testDir); File.WriteAllText(Path.Combine(testDir, "ui-error.txt"), e.Exception.ToString()); Environment.ExitCode = 1; app.Shutdown(1); } else MessageBox.Show("操作未完成：" + e.Exception.Message, "日常病程助手"); };
                try
                {
                    MainWindow main = new MainWindow(test ? Path.Combine(testDir, "profile-" + Guid.NewGuid().ToString("N")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ExamsOnly ? "ExaminationAssistantPublic" : "ProgressNotesAssistantPublic"));
                    if (test) main.Window.Loaded += delegate { main.Window.Dispatcher.BeginInvoke(new Action(delegate { if (args[0] == "--smart-test") SmartLabTests.Run(main, testDir); else if (args[0] == "--category-test") CategoryTests.Run(main, testDir); else if (clipboardTest) Tests.ClipboardOnly(main, testDir); else if(args[0] == "--fix-test") FixTests.Run(main,testDir); else if(args[0] == "--custom-test") CustomTests.Run(main,testDir); else if(ExamsOnly) FixTests.Run(main,testDir); else Tests.UI(main, testDir); }), DispatcherPriority.ApplicationIdle); };
                    app.Run(main.Window); return Environment.ExitCode;
                }
                catch (Exception ex) { if (test) { Directory.CreateDirectory(testDir); File.WriteAllText(Path.Combine(testDir, "ui-error.txt"), ex.ToString()); } else MessageBox.Show("启动失败：" + ex.Message, "日常病程助手"); return 1; }
            }
        }
    }
    public static class UI
    {
        public static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
        public static TextBlock Text(string text, double size = 14, string color = "#254C59") { return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }; }
        public static TextBox Box(string text, double height = 48, bool readOnly = false) { return new TextBox { Text = text ?? "", Height = height, IsReadOnly = readOnly, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(9), BorderBrush = Brush("#D7E2E7"), Background = Brush(readOnly ? "#F4F8F9" : "#FFFFFF") }; }
        public static Button Button(string text, Action action) { Button button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4, 2, 4, 2) }; button.Click += delegate { action(); }; return button; }
        public static Window Dialog(MainWindow main, string title, double width, double height) { return new Window { Title = title, Owner = main.Window, Width = width, Height = height, MinWidth = 560, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#F3F6F8"), FontFamily = main.Window.FontFamily, FontSize = 14, Resources = main.Window.Resources, ShowInTaskbar = false }; }
        public static Grid DialogGrid(string hint, UIElement content, UIElement buttons)
        {
            Grid grid = new Grid { Margin = new Thickness(20) }; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = Text(hint, 13, "#68838D"); label.Margin = new Thickness(0, 0, 0, 12); grid.Children.Add(label); Grid.SetRow(content, 1); grid.Children.Add(content); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); return grid;
        }
        public static StackPanel Footer(params UIElement[] children) { StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; foreach (UIElement child in children) panel.Children.Add(child); return panel; }
    }
    public partial class MainWindow
    {
        public Window Window; public Library Library; public State State; public string Profile; public TextBox Preview;
        public Dictionary<string, Control> Fields = new Dictionary<string, Control>();
        private StackPanel form; private ListBox modules; private bool changing; private DispatcherTimer timer;
        private string[] moduleIds = { "day2", "daily", "chief", "preop", "postop", "discharge", "special" };
        private string[] titles = { "第二天主任医师查房", "日常病程", "主任医师查房", "术前小结（待整理）", "术后第一天", "出院当天", "特殊记录" };
        public MainWindow(string profile)
        {
            Profile = profile; Library = Program.ReadLibrary(); RestoreCustom(); State = State.New(Library); string note = customWritable ? "" : "自定义库无法读取，原文件保留；暂不能覆盖保存。";
            string path = Path.Combine(Profile, "draft.xml");
            if (File.Exists(path)) try { State = State.Load(path); } catch { try { State = State.Load(path + ".bak"); note = "草稿损坏，已恢复备份；请核对。"; } catch { note = "草稿无法读取，已打开空白患者。"; } }
            if (!Library.Modules.Any(m => m.Id == State.CurrentModule)) State.CurrentModule = "day2";
            using (Stream stream = Program.Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            Preview = (TextBox)Window.FindName("PreviewBox"); form = (StackPanel)Window.FindName("FormContent"); modules = (ListBox)Window.FindName("ModuleList");
            RebuildModules();
            modules.SelectionChanged += delegate { if (!changing && modules.SelectedItem != null) { string id = (string)((ListBoxItem)modules.SelectedItem).Tag; SelectModule(id == "special" ? "lp" : id); } };
            Bind("CustomButton", delegate { CustomManager().Window.ShowDialog(); }); Bind("ExamsButton", OpenExams); Bind("SmartLabButton", delegate { OpenSmartLab(Window); }); Bind("HandlingButton", OpenHandling); Bind("GenerateButton", Generate); Bind("NewButton", delegate { NewPatient(true); });
            Bind("LumbarButton", delegate { SelectModule("lp"); }); Bind("ThrombolysisButton", delegate { SelectModule("thrombolysis"); });
            Bind("CopyButton", delegate { if (!CanOutput()) { ShowPending(); return; } try { CopyToClipboard(); Status("已复制，可直接粘贴。 "); } catch (Exception ex) { MessageBox.Show(Window, "复制未完成：" + ex.Message, "复制全文"); } });
            Bind("ExportButton", Export); Bind("SmallerButton", delegate { ResizeFont(-1); }); Bind("LargerButton", delegate { ResizeFont(1); });
            Bind("HelpButton", delegate { MessageBox.Show(Window, "1. 选择病程模块。括号项双击修改，方框可输入或粘贴；留空使用标明的模板默认。\n2. 双击辅助检查：勾选空白汇总为无明显异常，勾选有文字逐项输出。参考须手动载入。\n3. 双击异常处理：为有文字的检查逐项填写处理，或取消纳入。结果变化后须修改或确认原处理。\n4. 生成后核对全文，再复制或导出 TXT。修改输入后需重新生成。\n5. 检查与处理在当前患者共用；新患者清空所有模块。\n\n特殊记录仅开放标注项和时间；固定剂量、评分和经过沿用你提供的原文。\n术前小结保留入口，待逐段整理；主任查房暂沿用日常病程结构。", "使用说明"); });
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) { Generate(); e.Handled = true; } else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) { if (CanOutput()) CopyToClipboard(); else ShowPending(); e.Handled = true; } else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Export(); e.Handled = true; } };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; timer.Tick += delegate { timer.Stop(); Save(); };
            Window.SizeChanged += delegate { Responsive(); }; Window.Closing += delegate { timer.Stop(); Save(); };
            SelectModule(State.CurrentModule); if (note.Length > 0) Status(note);
        }
        private void Bind(string name, Action action) { ((Button)Window.FindName(name)).Click += delegate { action(); }; }
        private void ResizeFont(int delta) { State.FontSize = Math.Max(12, Math.Min(24, State.FontSize + delta)); Preview.FontSize = State.FontSize; Changed(); }
        public void Status(string text) { ((TextBlock)Window.FindName("StatusText")).Text = text; }
        public void Changed() { UpdateSummary(); UpdateReady(); timer.Stop(); timer.Start(); }
        public void Save() { try { State.Save(Path.Combine(Profile, "draft.xml")); } catch (Exception ex) { Status("草稿保存失败：" + ex.Message); } }
        public void SelectModule(string id)
        {
            if (!Library.Modules.Any(m => m.Id == id)) id = "day2"; State.CurrentModule = id; changing = true;
            int index = Array.IndexOf(moduleIds, id == "lp" || id == "thrombolysis" ? "special" : id); modules.SelectedIndex = index; if (index < 0) modules.SelectedItem = modules.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == id); changing = false;
            ModuleDefinition module = Library.GetModule(id); ((TextBlock)Window.FindName("ModuleTitle")).Text = module.Title;
            ((TextBlock)Window.FindName("ModuleHint")).Text = module.Pending ? "按你逐部分整理的方式，先保留入口" : id == "chief" ? "暂沿用日常病程结构，主任姓名可双击修改" : id == "lp" || id == "thrombolysis" ? "仅修改标注输入项和时间；其他文字沿用原文" : "括号项双击修改 · 方框可输入或粘贴";
            ((UIElement)Window.FindName("SpecialButtons")).Visibility = id == "lp" || id == "thrombolysis" ? Visibility.Visible : Visibility.Collapsed;
            form.Children.Clear(); Fields.Clear();
            if (module.Pending) form.Children.Add(UI.Text("术前小结尚未提供按输入项划分的完整格式。这里先保留入口，后续按你的步骤继续整理。", 15));
            foreach (FieldDefinition field in module.Fields)
            {
                FieldDefinition captured = field; StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) }; panel.Children.Add(UI.Text(field.Label + (field.Edit == "double" ? "  · 双击修改" : "")));
                TextBox box = UI.Box(field.Edit == "double" ? Generator.FieldValue(State, id, field) : State.Get(id, field.Key), field.Size, field.Edit == "double"); Fields[field.Key] = box;
                if (field.Edit == "double") { box.Cursor = Cursors.Hand; box.MouseDoubleClick += delegate { CreateFieldDialog(captured).Window.ShowDialog(); }; box.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CreateFieldDialog(captured).Window.ShowDialog(); e.Handled = true; } }; }
                else box.TextChanged += delegate { State.Set(id, captured.Key, box.Text); Changed(); };
                panel.Children.Add(box);
                if (!String.IsNullOrWhiteSpace(field.Default)) panel.Children.Add(UI.Text("留空使用模板默认：" + field.Default, 12, "#728994"));
                else if (field.Required) panel.Children.Add(UI.Text("未填写会标记待填写，补齐后可复制或导出。", 12, "#728994"));
                if (id == "thrombolysis" && (field.Key == "start" || field.Key == "stop")) panel.Children.Add(UI.Text("跨日可填 yyyy-MM-dd HH:mm，起止格式须一致。", 12, "#728994"));
                form.Children.Add(panel);
            }
            if (module.Body.Contains("{{EXAMS}}")) form.Children.Add(Entry("辅助检查", "双击打开检查选择与粘贴框", OpenExams));
            if (module.Body.Contains("{{HANDLING}}")) form.Children.Add(Entry(id == "day2" || id == "chief" ? "主任医师查房后示" : "处理", "双击填写检查对应的处理", OpenHandling));
            if (!module.Pending) { string template = module.Body.Replace("{{EXAMS}}", "[辅助检查]").Replace("{{HANDLING}}", "[处理]"); foreach (FieldDefinition field in module.Fields) template = template.Replace("{{" + field.Key + "}}", "[" + field.Label + "]"); Expander fixedText = new Expander { Header = "查看固定模板原文", Margin = new Thickness(0, 8, 0, 8), Content = UI.Text(template, 12, "#68838D") }; form.Children.Add(fixedText); }
            string value; Preview.Text = State.Previews.TryGetValue(id, out value) ? value : ""; Preview.FontSize = State.FontSize;
            ConfigureExamsOnly(); ((ScrollViewer)Window.FindName("FormScroll")).ScrollToTop(); UpdateSummary(); UpdateReady(); Responsive(); Save();
        }
        private Border Entry(string title, string hint, Action action)
        {
            StackPanel panel = new StackPanel(); panel.Children.Add(UI.Text(title + "  ›", 15)); panel.Children.Add(UI.Text(hint, 12, "#68838D"));
            Border border = new Border { Child = panel, Background = UI.Brush("#EAF5F4"), Padding = new Thickness(12), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 12), Cursor = Cursors.Hand, Focusable = true };
            border.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) action(); }; border.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { action(); e.Handled = true; } }; return border;
        }
        private void UpdateSummary()
        {
            int count = Library.Exams.Count(e => State.Exam(e.Id).Selected), abnormal = Library.Exams.Count(e => State.Exam(e.Id).Selected && !String.IsNullOrWhiteSpace(State.Exam(e.Id).Result));
            ((TextBlock)Window.FindName("CaseSummary")).Text = "当前患者 · 已选检查 " + count + " 项 · 有文字 " + abnormal + " 项 · " + (ExamsOnly ? "独立检查草稿" : "各模块分别保存");
        }
        public void Generate() { string value = BuildText(); State.Previews[State.CurrentModule] = value; State.Generated[State.CurrentModule] = value; Preview.Text = value; UpdateReady(); Save(); Status(Generator.Pending(value) ? "已生成草稿，补齐待填写 / 待核对内容后可输出。" : "已生成；请核对原文与患者情况后复制或导出。 "); }
        public bool CanOutput() { string value, current = BuildText(); return State.Generated.TryGetValue(State.CurrentModule, out value) && value == current && Preview.Text == current && !Generator.Pending(current) && !String.IsNullOrWhiteSpace(current); }
        private void UpdateReady()
        {
            string cached; bool exists = !String.IsNullOrWhiteSpace(Preview.Text), stale = exists && (!State.Generated.TryGetValue(State.CurrentModule, out cached) || cached != BuildText());
            ((TextBlock)Window.FindName("PreviewState")).Text = !exists ? "填写后点击生成" : stale ? "输入已变化 · 请重新生成" : Generator.Pending(Preview.Text) ? "有待填写或待核对项" : "已生成 · 核对后可输出";
            ((Button)Window.FindName("CopyButton")).IsEnabled = CanOutput(); ((Button)Window.FindName("ExportButton")).IsEnabled = CanOutput();
        }
        private void ShowPending() { MessageBox.Show(Window, "请先生成最新病程，并补齐待填写、待核对或待完善内容。", "输出前核对"); }
        public bool CopyToClipboard() { if (!CanOutput()) return false; Exception failure = null; for (int i = 0; i < 5; i++) try { Clipboard.SetText(Preview.Text, TextDataFormat.UnicodeText); return true; } catch (Exception ex) { failure = ex; Thread.Sleep(40); } throw failure; }
        public bool ExportTo(string path) { if (!CanOutput()) return false; File.WriteAllText(path, Preview.Text, new UTF8Encoding(true)); return true; }
        private void Export() { if (!CanOutput()) { ShowPending(); return; } SaveFileDialog dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = (ExamsOnly ? "辅助检查" : Library.GetModule(State.CurrentModule).Title).Replace("：", "_") + ".txt" }; if (dialog.ShowDialog(Window) == true) { ExportTo(dialog.FileName); Status("已导出 TXT：" + dialog.FileName); } }
        public void NewPatient(bool confirm) { if (confirm && MessageBox.Show(Window, "开始新患者将清空所有模块输入、检查、处理和生成文本。继续吗？", "新患者", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; timer.Stop(); State = State.New(Library); SelectModule("day2"); Save(); Status("已清空当前患者，所有模块和检查均为空白。 "); }
        public ExamDialog CreateExamsDialog() { return new ExamDialog(this); } public HandlingDialog CreateHandlingDialog() { return new HandlingDialog(this); } public FieldDialog CreateFieldDialog(FieldDefinition field) { return new FieldDialog(this, field); }
        public void OpenExams() { CreateExamsDialog().Window.ShowDialog(); } public void OpenHandling() { CreateHandlingDialog().Window.ShowDialog(); }
        private void Responsive()
        {
            Grid body = (Grid)Window.FindName("Body"); Border nav = (Border)Window.FindName("ModulesPanel"), formPanel = (Border)Window.FindName("FormPanel"), previewPanel = (Border)Window.FindName("PreviewPanel"); bool narrow = Window.Width < 1100;
            if(ExamsOnly) { nav.Visibility=Visibility.Collapsed; body.ColumnDefinitions[0].Width=new GridLength(0); body.ColumnDefinitions[1].Width=new GridLength(narrow?1:350,narrow?GridUnitType.Star:GridUnitType.Pixel); body.ColumnDefinitions[2].Width=new GridLength(narrow?0:14); body.ColumnDefinitions[3].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star); body.RowDefinitions[0].Height=new GridLength(narrow?0.8:1,GridUnitType.Star);body.RowDefinitions[1].Height=new GridLength(narrow?12:0);body.RowDefinitions[2].Height=new GridLength(narrow?1:0,GridUnitType.Star);Grid.SetRow(previewPanel,narrow?2:0);Grid.SetColumn(previewPanel,narrow?1:3);return; }
            body.ColumnDefinitions[0].Width = new GridLength(narrow ? 170 : 185); body.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star); body.ColumnDefinitions[2].Width = new GridLength(narrow ? 0 : 14); body.ColumnDefinitions[3].Width = new GridLength(narrow ? 0 : 435);
            body.RowDefinitions[0].Height = new GridLength(narrow ? 1.1 : 1, GridUnitType.Star); body.RowDefinitions[1].Height = new GridLength(narrow ? 12 : 0); body.RowDefinitions[2].Height = new GridLength(narrow ? 1 : 0, GridUnitType.Star);
            Grid.SetRowSpan(nav, narrow ? 3 : 1); Grid.SetRow(previewPanel, narrow ? 2 : 0); Grid.SetColumn(previewPanel, narrow ? 1 : 3); formPanel.Padding = new Thickness(narrow ? 12 : 16); previewPanel.Padding = new Thickness(narrow ? 12 : 16);
            ((UIElement)Window.FindName("PreviewHint")).Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    public class FieldDialog
    {
        public Window Window; public TextBox Input; public ComboBox Options; private MainWindow main; private FieldDefinition field; private string module;
        public FieldDialog(MainWindow owner, FieldDefinition definition)
        {
            main = owner; field = definition; module = main.State.CurrentModule; Window = UI.Dialog(main, "修改：" + field.Label, 650, 440);
            StackPanel panel = new StackPanel(); panel.Children.Add(UI.Text(field.Label, 18)); Input = UI.Box(main.State.Get(module, field.Key), 130); panel.Children.Add(Input);
            Options = new ComboBox { Margin = new Thickness(0, 12, 0, 0), Visibility = field.Options.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
            foreach (string option in field.Options) Options.Items.Add(option); Options.SelectionChanged += delegate { if (Options.SelectedItem != null) Input.Text = (string)Options.SelectedItem; }; panel.Children.Add(Options);
            Window.Content = UI.DialogGrid(String.IsNullOrWhiteSpace(field.Default) ? "直接输入或粘贴；缺项将标记待填写。" : "留空使用模板默认：" + field.Default, panel, UI.Footer(UI.Button("取消", Cancel), UI.Button("保存", Save)));
        }
        public void Save() { main.State.Set(module, field.Key, Input.Text); Window.Close(); main.SelectModule(module); main.Changed(); } public void Cancel() { Window.Close(); }
    }
    public class ExamRow
    {
        public CheckBox Selected; public TextBox Result; public ComboBox Reference, ChoiceA, ChoiceB; public Button LoadReference; public Border Panel;
    }
    public class ExamDialog
    {
        public Window Window; public Dictionary<string, ExamRow> Rows = new Dictionary<string, ExamRow>(); private MainWindow main;
        public TabControl Categories; public TextBox Search; public Button SmartLabButton;
        private HashSet<string> initialized;
        private Dictionary<string, StackPanel> lists = new Dictionary<string, StackPanel>();
        private Dictionary<string, TextBlock> emptyHints = new Dictionary<string, TextBlock>();
        public string CurrentCategory { get { TabItem tab = Categories.SelectedItem as TabItem; return tab == null ? "" : (string)tab.Tag; } }
        public void SelectCategory(string category) { Categories.SelectedItem = Categories.Items.Cast<TabItem>().First(t => (string)t.Tag == category); }
        private void EnterCategory()
        {
            string category = CurrentCategory; if (category.Length == 0 || initialized.Contains(category)) return;
            foreach (ExamDefinition definition in main.Library.Exams.Where(e => ExamCategory.Of(e) == category && e.DefaultSelected)) Rows[definition.Id].Selected.IsChecked = true;
            initialized.Add(category);
        }
        public void SelectAll(string category, bool selected) { foreach (ExamDefinition definition in main.Library.Exams.Where(e => ExamCategory.Of(e) == category)) Rows[definition.Id].Selected.IsChecked = selected; }
        private void Filter()
        {
            foreach (ExamDefinition definition in main.Library.Exams) Rows[definition.Id].Panel.Visibility = main.Library.Matches(definition, Search.Text) ? Visibility.Visible : Visibility.Collapsed;
            foreach (string category in ExamCategory.Ids) emptyHints[category].Visibility = main.Library.Exams.Any(e => ExamCategory.Of(e) == category && Rows[e.Id].Panel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
        public ExamDialog(MainWindow owner)
        {
            main = owner; Window = UI.Dialog(main, "辅助检查 · 四类选择", 1100, 820);
            StackPanel list = new StackPanel(); Search = UI.Box("", 38); Search.AcceptsReturn = false; Search.ToolTip = "搜索当前分类：中文、拼音或首字母";
            SmartLabButton = UI.Button("整批粘贴 · 离线智能整理", delegate { main.OpenSmartLab(Window); }); list.Children.Add(SmartLabButton);
            list.Children.Add(UI.Text("搜索当前分类（中文 / 拼音 / 首字母）", 13)); list.Children.Add(Search);
            Categories = new TabControl { Margin = new Thickness(0, 10, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            initialized = new HashSet<string>(main.State.ExamCategories);
            foreach (string category in ExamCategory.Ids)
            {
                string capturedCategory = category;
                StackPanel categoryList = new StackPanel(); lists.Add(category, categoryList);
                StackPanel body = new StackPanel { Margin = new Thickness(4, 12, 4, 4) };
                body.Children.Add(UI.Text(ExamCategory.Title(category), 18)); body.Children.Add(UI.Text(ExamCategory.Hint(category), 13, "#68838D"));
                StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(UI.Button("本类全选", delegate { SelectAll(capturedCategory, true); })); actions.Children.Add(UI.Button("本类全不选", delegate { SelectAll(capturedCategory, false); })); body.Children.Add(actions);
                TextBlock empty = UI.Text("当前分类没有匹配项目。", 14, "#728994"); empty.Visibility = Visibility.Collapsed; emptyHints.Add(category, empty); body.Children.Add(empty); body.Children.Add(categoryList);
                Categories.Items.Add(new TabItem { Header = ExamCategory.TabTitle(category), Tag = category, Padding = new Thickness(12, 10, 12, 10), Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
            }
            foreach (ExamDefinition definition in main.Library.Exams)
            {
                ExamDefinition captured = definition; ExamState saved = main.State.Exam(definition.Id); ExamRow row = new ExamRow(); Rows.Add(definition.Id, row);
                Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 0) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
                StackPanel left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; row.Selected = new CheckBox { Content = UI.Text(definition.Title), IsChecked = saved.Selected, VerticalContentAlignment = VerticalAlignment.Top }; left.Children.Add(row.Selected); left.Children.Add(UI.Text("勾选空白 = 无明显异常", 12, "#728994")); grid.Children.Add(left);
                StackPanel right = new StackPanel(); Grid.SetColumn(right, 1); grid.Children.Add(right); row.Result = UI.Box(saved.Result, definition.References.Any(r => r.Text.Length > 180) ? 105 : 75); right.Children.Add(row.Result);
                row.Reference = new ComboBox { Margin = new Thickness(0, 5, 0, 5) }; row.ChoiceA = new ComboBox { Margin = new Thickness(0, 6, 0, 3) }; row.ChoiceB = new ComboBox { Margin = new Thickness(0, 3, 0, 6) };
                if (definition.Kind == "gene")
                {
                    AddChoices(row.ChoiceA, definition, "clopidogrel", saved.ChoiceA); AddChoices(row.ChoiceB, definition, "warfarin", saved.ChoiceB);
                    right.Children.Add(UI.Text("氯吡格雷（单选，可不纳入）", 12)); right.Children.Add(row.ChoiceA); right.Children.Add(UI.Text("华法林（单选，可不纳入）", 12)); right.Children.Add(row.ChoiceB);
                }
                else if (definition.References.Count > 0) { foreach (ReferenceText reference in definition.References) row.Reference.Items.Add(reference); row.Reference.SelectedIndex = 0; right.Children.Add(row.Reference); }
                row.LoadReference = UI.Button("手动载入参考", delegate
                {
                    string text;
                    if (captured.Kind == "gene") text = Generator.GeneReference(captured, Choice(row.ChoiceA), Choice(row.ChoiceB));
                    else { ReferenceText reference = row.Reference.SelectedItem as ReferenceText; if (reference == null) return; text = reference.Text; }
                    if (Generator.Pending(text)) { MessageBox.Show(Window, "请先手动选择要载入的参考。", "载入参考"); return; }
                    if (!String.IsNullOrWhiteSpace(row.Result.Text) && MessageBox.Show(Window, "参考将替换本框已有文字，继续吗？", "载入参考", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    row.Result.Text = text; row.Selected.IsChecked = true;
                });
                if (definition.References.Count > 0) { right.Children.Add(row.LoadReference); right.Children.Add(UI.Text("参考为示例；载入后请按实际报告修改。", 12, "#728994")); }
                row.Panel = new Border { Child = grid, Padding = new Thickness(14), Background = Brushes.White, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 0) }; lists[ExamCategory.Of(definition)].Children.Add(row.Panel);
            }
            Search.TextChanged += delegate { Filter(); };
            Categories.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e) { if (e.Source == Categories) { EnterCategory(); Filter(); } };
            Grid content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition()); content.Children.Add(list); Grid.SetRow(Categories, 1); content.Children.Add(Categories);
            Window.Content = UI.DialogGrid("前三类为检验，第四类为手动录入的其他检查。勾选空白项目仍汇总为“无明显异常”，未做项目请取消勾选。", content, UI.Footer(UI.Button("取消", Cancel), UI.Button("保存检查", Save)));
            Categories.SelectedIndex = 0; EnterCategory(); Filter();
        }
        private static void AddChoices(ComboBox combo, ExamDefinition exam, string group, string selected) { combo.Items.Add(new ReferenceText { Id = "", Title = "不纳入" }); foreach (ReferenceText reference in exam.References.Where(r => r.Group == group)) combo.Items.Add(reference); combo.SelectedItem = combo.Items.Cast<ReferenceText>().FirstOrDefault(r => r.Id == selected) ?? combo.Items[0]; }
        private static string Choice(ComboBox combo) { ReferenceText selected = combo.SelectedItem as ReferenceText; return selected == null ? "" : selected.Id; }
        public void Save() { main.State.ExamCategories = new HashSet<string>(initialized); foreach (KeyValuePair<string, ExamRow> pair in Rows) { Core.UpdateResult(main.State, pair.Key, pair.Value.Selected.IsChecked == true, pair.Value.Result.Text); ExamState state = main.State.Exam(pair.Key); if (main.Library.GetExam(pair.Key).Kind == "gene") { state.ChoiceA = Choice(pair.Value.ChoiceA); state.ChoiceB = Choice(pair.Value.ChoiceB); } } Window.Close(); main.Changed(); main.Save(); }
        public void Cancel() { Window.Close(); }
    }
    public class HandlingRow
    {
        public CheckBox Included; public TextBox Treatment; public Button Confirm; public string Result, ForResult; public TextBlock Review;
    }
    public class HandlingDialog
    {
        public Window Window; public Dictionary<string, HandlingRow> Rows = new Dictionary<string, HandlingRow>(); private MainWindow main;
        public HandlingDialog(MainWindow owner)
        {
            main = owner; Window = UI.Dialog(main, "异常处理 · 当前患者共用", 940, 760); StackPanel list = new StackPanel();
            foreach (ExamDefinition definition in main.Library.Exams)
            {
                ExamState saved = main.State.Exam(definition.Id); if (!saved.Selected || String.IsNullOrWhiteSpace(saved.Result)) continue;
                HandlingRow row = new HandlingRow { Result = saved.Result, ForResult = saved.TreatmentForResult }; Rows.Add(definition.Id, row); StackPanel panel = new StackPanel(); row.Included = new CheckBox { Content = UI.Text("纳入：" + definition.Title, 16), IsChecked = saved.TreatmentEnabled }; panel.Children.Add(row.Included);
                panel.Children.Add(UI.Text("针对患者" + definition.Title + "异常：", 13)); panel.Children.Add(UI.Box(saved.Result, Math.Min(150, Math.Max(65, saved.Result.Length / 3)), true)); panel.Children.Add(UI.Text("予以以下内容处理（自己填写）", 13)); row.Treatment = UI.Box(saved.Treatment, 90); panel.Children.Add(row.Treatment);
                row.Review = UI.Text("", 12, "#AA6324"); panel.Children.Add(row.Review);
                Action review = delegate { bool pending = !String.IsNullOrWhiteSpace(row.Treatment.Text) && Generator.Normalize(row.ForResult).Trim() != Generator.Normalize(row.Result).Trim(); row.Review.Text = pending ? "检查结果已变化，原处理须重新核对。" : ""; if (row.Confirm != null) row.Confirm.Visibility = pending ? Visibility.Visible : Visibility.Collapsed; };
                row.Confirm = UI.Button("确认原处理仍适用", delegate { row.ForResult = row.Result; review(); }); panel.Children.Add(row.Confirm); review();
                row.Treatment.TextChanged += delegate { row.ForResult = row.Result; review(); };
                var presets = main.CustomItems.Where(i => i.Kind == "treatment").ToList();
                if (presets.Count > 0) { ComboBox choices = new ComboBox { ItemsSource = presets, Margin = new Thickness(0,8,0,4), MinHeight = 36 }; panel.Children.Add(choices); panel.Children.Add(UI.Button("手动载入处理参考", delegate { var selected = choices.SelectedItem as SuiteCustomization.Item; if (selected == null) return; if (row.Treatment.Text.Length > 0 && MessageBox.Show(Window,"替换当前处理文字？","载入参考",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; row.Treatment.Text = selected.Text; })); }

                list.Children.Add(new Border { Child = panel, Padding = new Thickness(16), Background = Brushes.White, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 14) });
            }
            if (Rows.Count == 0) list.Children.Add(UI.Text("目前没有勾选且填写文字的检查。先到辅助检查里选择并粘贴结果；空白正常项目无需处理。", 16));
            Window.Content = UI.DialogGrid("按你的规则，有文字的检查关联到处理栏。软件不判断异常含义、不自动填写诊疗方案；无需纳入的项目可取消勾选。", new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, UI.Footer(UI.Button("取消", Cancel), UI.Button("保存处理", Save)));
        }
        public void Save() { foreach (KeyValuePair<string, HandlingRow> pair in Rows) { ExamState state = main.State.Exam(pair.Key); state.Treatment = pair.Value.Treatment.Text; state.TreatmentEnabled = pair.Value.Included.IsChecked == true; state.TreatmentForResult = pair.Value.ForResult; } Window.Close(); main.Changed(); main.Save(); }
        public void Cancel() { Window.Close(); }
    }
}
