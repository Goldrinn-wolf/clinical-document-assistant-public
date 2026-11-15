using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("入院首程助手")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
namespace FirstCourseAssistant
{
    public static class Program
    {
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        public static Library ReadLibrary() { using (Stream s = Resource("templates.xml")) return Library.Load(s); }
        [STAThread]
        public static int Main(string[] args)
        {
            bool testing = args.Contains("--self-test") || args.Contains("--preview-test") || args.Contains("--custom-test") || args.Contains("--plan-test");
            try
            {
                if (args.Contains("--self-test")) return Tests.Core(args[args.Length - 1]);
                bool first;
                using (Mutex mutex = new Mutex(true, "Local\\FirstCourseAssistantPublic_1", out first))
                {
                    if (!first && !testing) { MessageBox.Show("入院首程助手已在运行，请从任务栏打开。", "入院首程助手"); return 0; }
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += (s, e) => { e.Handled = true; if (testing) { File.WriteAllText(Path.Combine(args[args.Length - 1], "ui-error.txt"), e.Exception.ToString()); app.Shutdown(1); } else MessageBox.Show("操作未完成：" + e.Exception.Message, "入院首程助手"); };
                    MainWindow w = new MainWindow(testing ? Path.Combine(args[args.Length - 1], "profile-" + Guid.NewGuid().ToString("N")) : null);
                    if (testing) w.Window.Loaded += (s, e) => w.Window.Dispatcher.BeginInvoke(new Action(() => (args.Contains("--plan-test") ? (Action)(() => PlanTests.Run(w, args[args.Length - 1])) : args.Contains("--custom-test") ? (Action)(() => CustomTests.Run(w, args[args.Length - 1])) : () => Tests.UI(w, args[args.Length - 1]))()), DispatcherPriority.ApplicationIdle);
                    int code = app.Run(w.Window); if (first) mutex.ReleaseMutex(); return code != 0 ? code : Environment.ExitCode;
                }
            }
            catch (Exception e) { if (testing) { Directory.CreateDirectory(args[args.Length - 1]); File.WriteAllText(Path.Combine(args[args.Length - 1], "startup-error.txt"), e.ToString()); } else MessageBox.Show("无法启动：" + e.Message, "入院首程助手"); return 1; }
        }
    }
    public partial class MainWindow
    {
        public Window Window;
        public Library Library;
        public State State;
        public string Profile;
        public TextBox Preview;
        public Dictionary<string, Control> Fields = new Dictionary<string, Control>();
        private bool building, customWritable = true;
        private ListBox steps;
        private StackPanel form;
        private TextBlock status, previewState;
        private DispatcherTimer saveTimer, searchTimer;
        private TextBox ddxSearch;
        private StackPanel ddxList;
        private TextBlock vteSummary;
        public MainWindow(string profile = null)
        {
            Profile = profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirstCourseAssistantPublic"); Library = Program.ReadLibrary(); State = State.New(Library);
            using (Stream s = Program.Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(s);
            Preview = Find<TextBox>("PreviewBox"); steps = Find<ListBox>("StepList"); form = Find<StackPanel>("FormContent"); status = Find<TextBlock>("StatusText"); previewState = Find<TextBlock>("PreviewState"); Restore();
            building = true; Preview.Text = State.Preview; Preview.FontSize = State.FontSize; building = false;
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; searchTimer.Tick += (s, e) => { searchTimer.Stop(); if (State.Step == 2) RenderDdxList(); };
            Preview.TextChanged += (s, e) => { if (!building) { State.Edited = true; RefreshMeta(); ScheduleSave(); } };
            steps.SelectionChanged += (s, e) => { if (!building && steps.SelectedItem != null) GoTo((int)((ListBoxItem)steps.SelectedItem).Tag); };
            Find<Button>("PreviousButton").Click += (s, e) => GoTo(Math.Max(1, State.Step - 1));
            Find<Button>("NextButton").Click += (s, e) => { if (State.Step == 3) Generate(true); else GoTo(State.Step + 1); };
            Find<Button>("GenerateButton").Click += (s, e) => Generate(true);
            Find<Button>("NewButton").Click += (s, e) => { if (MessageBox.Show(Window, "清空当前患者字段、VTE、临时改写及预览，开始新患者？\n已保存的自定义模板保留。", "新患者", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) NewPatient(Find<CheckBox>("KeepGroups").IsChecked == true); };
            Find<Button>("CopyButton").Click += (s, e) => CopyAll();
            Find<Button>("CopyPartButton").Content = "复制当前模块（按表单）";
            Find<Button>("CopyPartButton").Click += (s, e) => CopyPart();
            Find<Button>("ExportButton").Click += (s, e) => Export();
            Find<Button>("CustomButton").Click += (s, e) => CustomManager().Window.ShowDialog();
            Find<Button>("ReferenceButton").Click += (s, e) => ReferenceDialog().ShowDialog();
            Find<Button>("SmallerButton").Click += (s, e) => Font(-1);
            Find<Button>("LargerButton").Click += (s, e) => Font(1);
            Window.PreviewKeyDown += KeyDown; Window.SizeChanged += (s, e) => Responsive();
            Window.Closing += (s, e) => { saveTimer.Stop(); Save(); };
            Render();
        }
        public T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        public static Brush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        private void Restore()
        {
            string path = Path.Combine(Profile, "custom-templates.xml");
            if (File.Exists(path)) try { Library.MergeCustom(path); } catch { try { Library.MergeCustom(path + ".bak"); status.Text = "自定义模板异常，已恢复备份"; } catch { customWritable = false; status.Text = "自定义模板无法读取，原文件已保留，暂不能覆盖保存"; } }
            path = Path.Combine(Profile, "draft.xml");
            if (File.Exists(path)) try { State = State.Load(path); status.Text = "已恢复上次首程草稿"; } catch { try { State = State.Load(path + ".bak"); status.Text = "草稿异常，已恢复上一份备份"; } catch { status.Text = "草稿无法读取，原文件已保留"; } }
            State.Selected.RemoveWhere(id => !Library.Templates.Any(t => t.Id == id && t.Kind == "ddx")); if (PlanComposer.Migrate(Library, State)) status.Text = "旧版通用计划已更新，降压降糖等请按本例勾选；原预览需重新生成";
        }
        public void Save() { try { State.Preview = Preview.Text; State.FontSize = Preview.FontSize; State.Save(Path.Combine(Profile, "draft.xml")); } catch (Exception e) { status.Text = "保存失败：" + e.Message; } }
        private void ScheduleSave() { if (saveTimer == null) return; saveTimer.Stop(); saveTimer.Start(); }
        private void Changed(string key, string value) { if (building) return; State.Set(key, value); if (State.Step == 3 && vteSummary != null && (key == "VteScore" || key == "VteScale")) vteSummary.Text = Generator.Vte(Library, State); RefreshMeta(); ScheduleSave(); }
        public bool Stale { get { return Preview.Text.Length > 0 && State.Signature != Generator.Signature(Library, State); } }
        public void RefreshMeta()
        {
            previewState.Text = Preview.Text.Length == 0 ? "可先生成草稿，再核对并编辑" : Stale ? "表单已变化，请重新生成或保留当前编辑" : State.Edited ? "已手动编辑 · 自动保存" : Generator.Pending(Preview.Text) ? "有待补充或待核对项，请填写后输出" : "已生成 · 可编辑后复制";
            previewState.Foreground = Brush(Stale || Generator.Pending(Preview.Text) ? "#A37424" : "#728994");
            string[] names = Library.Templates.Where(t => t.Kind == "ddx" && State.Selected.Contains(t.Id)).OrderBy(t => t.Order).Select(t => t.Title).ToArray();
            Find<TextBlock>("SelectedGroups").Text = "鉴别组：" + (names.Length == 0 ? "未选择" : String.Join("、", names));
            Find<Button>("CopyButton").IsEnabled = Find<Button>("ExportButton").IsEnabled = Preview.Text.Length > 0;
        }
        public void GoTo(int step) { State.Step = Math.Max(1, Math.Min(3, step)); searchTimer.Stop(); Find<ScrollViewer>("FormScroll").ScrollToTop(); Render(); ScheduleSave(); }
        public void Render()
        {
            building = true;
            if (steps.Items.Count == 0) for (int i = 1; i <= 3; i++) steps.Items.Add(new ListBoxItem { Tag = i, Content = new TextBlock { Text = i + ". " + new[] { "诊断依据", "鉴别诊断", "诊疗计划 / VTE" }[i - 1], TextWrapping = TextWrapping.Wrap, FontSize = 14 } });
            foreach (ListBoxItem item in steps.Items) item.IsSelected = (int)item.Tag == State.Step;
            Find<TextBlock>("StageTitle").Text = new[] { "诊断依据 · 填写患者变量", "鉴别诊断 · 整组选择", "诊疗计划 · 疾病模板与可选处理" }[State.Step - 1];
            Find<TextBlock>("StageHint").Text = new[] { "选项可点击，也可直接输入修改；不默认填写正常查体或患者信息。", "13个原Word分组，勾选后整组载入，可为本例改写。", "按病情选择模板，再勾选合并症和可选用药；未勾选不加入。VTE按实际评估填写。" }[State.Step - 1];
            Find<Button>("PreviousButton").IsEnabled = State.Step > 1; Find<Button>("NextButton").Content = State.Step == 3 ? "完成并生成" : "下一步";
            form.Children.Clear(); Fields.Clear();
            if (State.Step == 1) RenderDiagnosis(); else if (State.Step == 2) RenderDdx(); else RenderPlan();
            building = false; RefreshMeta();
        }
        private void Label(Panel panel, string text) { panel.Children.Add(new TextBlock { Text = text, Foreground = Brush("#254C59"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) }); }
        private TextBox Input(Panel panel, string key, string label, int height = 40)
        {
            Label(panel, label); TextBox box = new TextBox { Text = State.Get(key), Height = height, AcceptsReturn = height > 40, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = height > 40 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Margin = new Thickness(0, 0, 0, 10) };
            box.TextChanged += (s, e) => Changed(key, box.Text); panel.Children.Add(box); Fields[key] = box; return box;
        }
        private ComboBox Choice(Panel panel, string key, string label, string[] options)
        {
            Label(panel, label); ComboBox combo = new ComboBox { IsEditable = true, ItemsSource = options, Text = State.Get(key), Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 10), MinHeight = 36 };
            combo.SelectionChanged += (s, e) => { if (combo.SelectedItem != null) Changed(key, combo.SelectedItem.ToString()); };
            combo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => Changed(key, combo.Text)));
            panel.Children.Add(combo); Fields[key] = combo; return combo;
        }
        private CheckBox Flag(Panel panel, string key, string label, bool rerender = false)
        {
            CheckBox check = new CheckBox { Content = label, IsChecked = State.Flag(key), Foreground = Brush("#476771"), Margin = new Thickness(0, 6, 0, 6) };
            RoutedEventHandler change = (s, e) => { Changed(key, check.IsChecked == true ? "true" : "false"); if (!building && rerender) Window.Dispatcher.BeginInvoke(new Action(Render)); };
            check.Checked += change; check.Unchecked += change; panel.Children.Add(check); Fields[key] = check; return check;
        }
        private void RenderDiagnosis()
        {
            Grid choices = new Grid(); for (int i = 0; i < 3; i++) choices.ColumnDefinitions.Add(new ColumnDefinition());
            string[] keys = { "Age", "Gender", "Onset" }; string[] labels = { "年龄组（可改写）", "性别（可改写）", "起病方式（可改写）" };
            string[][] options = { new[] { "老年", "中年", "青年" }, new[] { "男性", "女性" }, new[] { "急性起病", "亚急性起病", "慢性病程", "隐匿起病", "反复发作" } };
            for (int i = 0; i < 3; i++) { StackPanel p = new StackPanel { Margin = new Thickness(0, 0, i == 2 ? 0 : 8, 0) }; Choice(p, keys[i], labels[i], options[i]); Grid.SetColumn(p, i); choices.Children.Add(p); } form.Children.Add(choices);
            Input(form, "Chief", "因何入院：症状及病程（不必再写“因 / 入院”）", 68);
            Input(form, "Duration", "每次持续时间（选填，如数秒至数分钟）");
            Flag(form, "CompanionOn", "加入伴随症状（不需要时取消勾选）", true);
            if (State.Flag("CompanionOn")) Input(form, "Companion", "伴什么：填写本例实际症状", 60);
            Flag(form, "ExamAsAbove", "明确引用前述查体：写“神经系统查体大致同前”", true);
            if (!State.Flag("ExamAsAbove")) Input(form, "ExamSummary", "神经系统查体要点 / 阳性体征", 80);
            Flag(form, "RiskOn", "加入既往危险因素（无须写时取消勾选）", true);
            if (State.Flag("RiskOn")) Input(form, "Risks", "既往合并什么：如高血压、吸烟（填写事实）", 60);
            Flag(form, "TestsAsAbove", "明确引用已写在上文的检查：写“院前辅助检查结果如上”", true);
            if (!State.Flag("TestsAsAbove")) { Input(form, "Tests", "院前辅助检查结果：大框粘贴，可保留日期与换行", 180); CustomPresets(form, "exam", "Tests", true); }
            Input(form, "ExtraEvidence", "其他诊断依据（选填，追加为下一项）", 70);
        }
        private void RenderDdx()
        {
            Label(form, "搜索整组或组内疾病：中文 / 全拼 / 首字母"); ddxSearch = new TextBox { Height = 40, Margin = new Thickness(0, 0, 0, 10), ToolTip = "例如急性脑血管病 / jxnxgb / 头晕 / ty / 脑梗死 / ngs" };
            ddxSearch.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); }; form.Children.Add(ddxSearch); Fields["DdxSearch"] = ddxSearch;
            ddxList = new StackPanel(); form.Children.Add(ddxList); RenderDdxList();
            Input(form, "DdxExtra", "鉴别诊断补充 / 本例支持与不支持依据（选填）", 120);
        }
        public void RenderDdxList()
        {
            if (ddxList == null) return; ddxList.Children.Clear();
            foreach (Template t in Library.Templates.Where(t => t.Kind == "ddx" && Library.Matches(t, ddxSearch == null ? "" : ddxSearch.Text)).OrderBy(t => t.Order))
            {
                DockPanel content = new DockPanel(); Button edit = new Button { Content = "查看 / 本例改写", Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top }; edit.Click += (s, e) => GroupDialog(t).ShowDialog(); DockPanel.SetDock(edit, Dock.Right); content.Children.Add(edit);
                StackPanel label = new StackPanel(); label.Children.Add(new TextBlock { Text = t.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#234C59"), TextWrapping = TextWrapping.Wrap });
                string body = State.Overrides.ContainsKey(t.Id) ? State.Overrides[t.Id] : t.Text; string summary = body.Replace("\r\n", " ").Replace("\n", " ");
                label.Children.Add(new TextBlock { Text = summary.Length > 150 ? summary.Substring(0, 150) + "…" : summary, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#637D88"), Margin = new Thickness(0, 6, 0, 0) });
                label.Children.Add(new TextBlock { Text = (t.Custom ? "自定义模板" : "Word整组模板") + (State.Overrides.ContainsKey(t.Id) ? " · 已为本例改写" : ""), Foreground = Brush("#10837D"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
                CheckBox check = new CheckBox { Tag = t.Id, Content = label, IsChecked = State.Selected.Contains(t.Id), VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(7, 0, 0, 0) };
                Border card = new Border { Child = content, Background = Brush(check.IsChecked == true ? "#EDF7F5" : "#FAFCFD"), CornerRadius = new CornerRadius(8), BorderBrush = Brush("#DFE9ED"), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 0, 5, 10) };
                check.Checked += (s, e) => { State.Selected.Add(t.Id); card.Background = Brush("#EDF7F5"); RefreshMeta(); ScheduleSave(); };
                check.Unchecked += (s, e) => { State.Selected.Remove(t.Id); card.Background = Brush("#FAFCFD"); RefreshMeta(); ScheduleSave(); };
                content.Children.Add(check); ddxList.Children.Add(card);
            }
            if (ddxList.Children.Count == 0) ddxList.Children.Add(new TextBlock { Text = "没有匹配模板，可清除搜索或自定义添加。", Foreground = Brush("#728994"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 20, 8, 20) });
        }
        private void RenderPlan()
        {
            Label(form, "选择疾病 / 入院情形模板（载入后可修改）"); ComboBox preset = new ComboBox { Padding = new Thickness(7), MinHeight = 36, Margin = new Thickness(0, 0, 0, 10) };
            foreach (Template t in Library.Templates.Where(t => t.Kind == "plan").OrderBy(t => t.Order)) { ComboBoxItem item = new ComboBoxItem { Content = t.Title, Tag = t }; preset.Items.Add(item); if (t.Id == State.Get("PlanTemplateId") || (State.Get("PlanTemplateId").Length == 0 && t.Text == State.Get("PlanBody"))) preset.SelectedItem = item; }
            preset.SelectionChanged += (s, e) => { if (!building && preset.SelectedItem != null) { Template t = (Template)((ComboBoxItem)preset.SelectedItem).Tag; Changed("PlanTemplateId", t.Id); ((TextBox)Fields["PlanBody"]).Text = t.Text; } }; form.Children.Add(preset); Fields["PlanPreset"] = preset;
            Input(form, "PlanBody", "本例基础诊疗计划（检查、治疗与观察护理）", 180);
            RenderPlanOptions();
            Flag(form, "AnalysisOn", "补充初步考虑与病情分析（选填，参考末尾两例）", true);
            if (State.Flag("AnalysisOn"))
            {
                UniformGrid buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 3, 0, 6) };
                Button stroke = new Button { Content = "脑血管病骨架" }; Button dizziness = new Button { Content = "头晕骨架" }; stroke.Click += (s, e) => Skeleton("stroke"); dizziness.Click += (s, e) => Skeleton("dizziness"); buttons.Children.Add(stroke); buttons.Children.Add(dizziness); form.Children.Add(buttons);
                Input(form, "Analysis", "填写检查证据、初步考虑、风险及本例安排", 170);
            }
            Label(form, "VTE模板：手动选择，工具不计算风险评分"); ComboBox mode = new ComboBox { Padding = new Thickness(7), MinHeight = 36, Margin = new Thickness(0, 0, 0, 10) };
            string[] codes = { "", "low", "high", "custom", "skip" }; string[] labels = { "请选择VTE模板", "低危（原模板＜4分）", "中高危（原模板≥4分）", "自定义VTE正文", "本次不纳入VTE（由我确认）" };
            for (int i = 0; i < codes.Length; i++) { ComboBoxItem item = new ComboBoxItem { Content = labels[i], Tag = codes[i] }; mode.Items.Add(item); if (State.Get("VteMode") == codes[i]) mode.SelectedItem = item; }
            mode.SelectionChanged += (s, e) => { if (!building && mode.SelectedItem != null) { Changed("VteMode", (string)((ComboBoxItem)mode.SelectedItem).Tag); Window.Dispatcher.BeginInvoke(new Action(Render)); } }; form.Children.Add(mode); Fields["VteMode"] = mode;
            if (new[] { "low", "high" }.Contains(State.Get("VteMode")))
            {
                Input(form, "VteScale", "采用量表名称（选填，照实际填写）"); Input(form, "VteScore", "已评估的VTE评分（只填数值，不自动推算）");
                vteSummary = new TextBlock { Text = Generator.Vte(Library, State), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#637D88"), FontSize = 12, Margin = new Thickness(0, 4, 4, 12) }; form.Children.Add(vteSummary);
            }
            else if (State.Get("VteMode") == "custom") { Input(form, "VteCustom", "本例VTE评估与处理正文（自行填写）", 140); CustomPresets(form, "vte-preset", "VteCustom", false); }
        }
        public void Skeleton(string kind)
        {
            if (State.Get("Analysis").Length > 0 && MessageBox.Show(Window, "载入骨架将替换当前病情分析文字，是否继续？", "载入骨架", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            State.Set("AnalysisOn", "true"); State.Set("Analysis", Generator.AnalysisSkeleton(kind)); Render(); ScheduleSave();
        }
        public void Generate(bool confirm)
        {
            if (confirm && State.Edited && Preview.Text.Length > 0 && MessageBox.Show(Window, "重新生成将替换右侧手动编辑的文本，是否继续？", "重新生成", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            building = true; Preview.Text = Generator.All(Library, State); building = false; State.Edited = false; State.Signature = Generator.Signature(Library, State); RefreshMeta(); ScheduleSave();
            status.Text = Generator.Pending(Preview.Text) ? "已生成草稿，请填写待补充、待核对项后再输出" : "已生成，可继续编辑或复制到病历系统";
        }
        public bool Ready(string text, bool confirm, bool checkStale)
        {
            if (String.IsNullOrWhiteSpace(text)) { status.Text = "请先生成首程文本。"; return false; }
            if (Generator.Pending(text)) { status.Text = "请填写待补充、待核对项后再输出。"; return false; }
            if (confirm && checkStale && Stale && MessageBox.Show(Window, "表单已变化，当前预览仍是之前的内容。是否输出当前预览？", "确认输出", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            return true;
        }
        public bool CopyText(string text)
        {
            for (int i = 0; i < 5; i++) try { Clipboard.SetText(text, TextDataFormat.UnicodeText); return true; } catch { Thread.Sleep(40); }
            status.Text = "剪贴板被占用，请重试或导出TXT。"; return false;
        }
        public void CopyAll() { if (Ready(Preview.Text, true, true) && CopyText(Preview.Text)) status.Text = "已复制全文（含右侧手动编辑内容）"; }
        public void CopyPart() { string text = Generator.Part(Library, State, State.Step); if (Ready(text, false, false) && CopyText(text)) status.Text = "已复制当前表单模块（不含右侧预览的手动改写）"; }
        private void Export() { if (!Ready(Preview.Text, true, true)) return; SaveFileDialog dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = "入院首程_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt" }; if (dialog.ShowDialog(Window) == true) { File.WriteAllText(dialog.FileName, Preview.Text, new UTF8Encoding(true)); status.Text = "已导出TXT：" + dialog.FileName; } }
        public void NewPatient(bool keepGroups)
        {
            HashSet<string> groups = keepGroups ? new HashSet<string>(State.Selected) : new HashSet<string>(); State = State.New(Library); State.Selected = groups;
            building = true; Preview.Clear(); building = false; Render(); ScheduleSave(); status.Text = "已开始新患者，患者字段与VTE已清空，自定义模板保留";
        }
        private void Font(int delta) { Preview.FontSize = Math.Max(12, Math.Min(24, Preview.FontSize + delta)); ScheduleSave(); }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) { Generate(true); e.Handled = true; }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) { CopyAll(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Export(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { if (State.Step != 2) GoTo(2); ddxSearch.Focus(); ddxSearch.SelectAll(); e.Handled = true; }
        }
        public void Responsive()
        {
            bool small = Window.ActualWidth < 1100; Grid body = Find<Grid>("Body"); Border preview = Find<Border>("PreviewPanel");
            body.ColumnDefinitions[0].Width = new GridLength(small ? 140 : 170); body.ColumnDefinitions[3].Width = new GridLength(small ? 0 : Math.Min(470, Window.ActualWidth * 0.35)); body.ColumnDefinitions[2].Width = new GridLength(small ? 0 : 14);
            body.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); body.RowDefinitions[1].Height = new GridLength(small ? 10 : 0); body.RowDefinitions[2].Height = small ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(preview, small ? 2 : 0); Grid.SetColumn(preview, small ? 1 : 3); Grid.SetColumnSpan(preview, small ? 3 : 1); Grid.SetRowSpan(Find<Border>("StepsPanel"), small ? 3 : 1);
            preview.Padding = new Thickness(small ? 12 : 16); Preview.Padding = new Thickness(small ? 6 : 10); previewState.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("StageHint").Visibility = small && Window.ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("SelectedGroups").MaxHeight = small ? 28 : 75;
            Find<Button>("CopyPartButton").Padding = Find<Button>("CopyButton").Padding = Find<Button>("ExportButton").Padding = new Thickness(10, small ? 5 : 9, 10, small ? 5 : 9);
        }
        public Template AddCustom(string kind, string title, string text, string py)
        {
            if (!customWritable) throw new InvalidOperationException("自定义模板文件无法读取，暂不能覆盖保存。");
            Template t = Library.Add(kind, title, text, py); try { Library.SaveCustom(Path.Combine(Profile, "custom-templates.xml")); } catch { Library.Templates.Remove(t); throw; } Render(); status.Text = "已保存自定义模板：" + t.Title; return t;
        }
        public void SetOverride(Template t, string text) { if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("本例改写正文不能为空。"); State.Overrides[t.Id] = Generator.Normalize(text); RefreshMeta(); ScheduleSave(); if (State.Step == 2) RenderDdxList(); }
        public Window GroupDialog(Template t)
        {
            Window w = Dialog("鉴别诊断整组 / 本例改写", 770, 720); DockPanel p = new DockPanel { Margin = new Thickness(20) };
            TextBlock heading = new TextBlock { Text = t.Title + " · 整组正文", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brush("#254C59"), Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(heading, Dock.Top); p.Children.Add(heading);
            UniformGrid actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) }; Button apply = new Button { Content = "应用本例改写" }; Button save = new Button { Content = "另存为自定义模板" }; actions.Children.Add(apply); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Bottom); p.Children.Add(actions);
            TextBox body = new TextBox { Text = State.Overrides.ContainsKey(t.Id) ? State.Overrides[t.Id] : Generator.Normalize(t.Text), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
            apply.Click += (s, e) => { try { SetOverride(t, body.Text); w.Close(); } catch (Exception ex) { MessageBox.Show(w, ex.Message); } };
            save.Click += (s, e) => { Template copy = new Template { Kind = "ddx", Title = t.Title + "（自定义）", Text = body.Text }; CustomDialog(copy).ShowDialog(); };
            p.Children.Add(body); w.Content = p; return w;
        }
        private Window Dialog(string title, int width, int height) { return new Window { Title = title, Owner = Window, Width = width, Height = height, MinWidth = 480, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Window.FontFamily, FontSize = 14, Background = Brush("#F3F6F8") }; }
        public Window CustomDialog(Template original)
        {
            Window w = Dialog("自定义可复用模板", 650, 650); StackPanel p = new StackPanel { Margin = new Thickness(24) };
            p.Children.Add(new TextBlock { Text = "保存通用模板，患者信息另在表单填写", FontSize = 19, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#254C59") });
            ComboBox kind = new ComboBox { ItemsSource = new[] { "鉴别诊断整组模板", "VTE前通用诊疗计划" }, SelectedIndex = original != null && original.Kind == "plan" ? 1 : 0, Margin = new Thickness(0, 15, 0, 12), Padding = new Thickness(7) }; p.Children.Add(kind);
            Label(p, "模板名称 *"); TextBox title = new TextBox { Text = original == null ? "" : original.Title, Height = 40, Margin = new Thickness(0, 0, 0, 12) }; p.Children.Add(title);
            Label(p, "通用正文 *（不要保存当前患者事实）"); TextBox body = new TextBox { Text = original == null ? "" : original.Text, Height = 260, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 12) }; p.Children.Add(body);
            Label(p, "拼音（选填，空格分隔每字）"); TextBox py = new TextBox { Height = 40, Margin = new Thickness(0, 0, 0, 15) }; p.Children.Add(py);
            Button save = new Button { Content = "保存模板", Background = Brush("#10837D"), Foreground = Brushes.White }; save.Click += (s, e) => { try { AddCustom(kind.SelectedIndex == 0 ? "ddx" : "plan", title.Text, body.Text, py.Text); w.Close(); } catch (Exception ex) { MessageBox.Show(w, ex.Message, "未保存"); } }; p.Children.Add(save);
            w.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; return w;
        }
        public Window ReferenceDialog()
        {
            Window w = Dialog("工作方式与原Word参考病例", 780, 740); TabControl tabs = new TabControl { Margin = new Thickness(18) };
            string help = "建议流程：新患者 → 选择年龄/性别/起病 → 填入院原因、伴随症状、查体 → 粘贴辅助检查 → 勾选整组鉴别 → 选择疾病计划 → 勾选共病及可选用药 → 核对VTE → 生成、核对、复制。\n\n减少反复改写：不需要的伴随症状、危险因素可取消；已在上文写过的查体、检查可明确勾选引用；常用鉴别组可在新患者保留，但临时改写和患者信息会清空。\n\n诊疗计划提供13类模板；高血压、糖尿病、血脂异常、睡眠障碍及3种静脉用药分别按本例勾选，勾选文字可改，未勾选不加入。新患者清空这些勾选。\n\n脑血管病、头晕两个原病例只在这里供阅读。可在诊疗计划加入选填病情分析，并使用带待补充字段的空白骨架。示例患者的性别、侧别、检查、治疗、病重或知情同意情况不会自动载入。\n\n复制全文保留右侧手动编辑；复制当前模块按左侧表单重新生成。待补充、待核对字段填写后才能输出。更改表单不会覆盖预览。\n\nCtrl+F 鉴别搜索；Ctrl+Enter 生成；Ctrl+Shift+C 复制全文；Ctrl+S 导出TXT。\n\nVTE仅按原文提供低危/中高危模板，不计算评分，不推断量表或预防方案。\n\n本机明文草稿和自定义模板目录：\n" + Profile + "\n版本1.3.0 · Windows7 SP1＋.NET Framework4.8，Windows7尚未实机验证。";
            foreach (KeyValuePair<string, string> pair in new[] { new KeyValuePair<string, string>("使用与节省时间", help), new KeyValuePair<string, string>("脑血管病参考（示例）", Library.References["reference-stroke"]), new KeyValuePair<string, string>("头晕参考（示例）", Library.References["reference-dizziness"]) })
                tabs.Items.Add(new TabItem { Header = pair.Key, Content = new TextBox { Text = pair.Value, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(14) } });
            w.Content = tabs; return w;
        }
    }
}
