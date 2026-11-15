using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FirstCourseAssistant
{
    public static class Tests
    {
        private static List<string> results = new List<string>();
        private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); results.Add("PASS " + label); }
        private static State Filled(Library lib)
        {
            State s = State.New(lib); foreach (KeyValuePair<string, string> p in new Dictionary<string, string> { { "Age", "老年" }, { "Gender", "男性" }, { "Onset", "急性起病" }, { "Chief", "发作性头晕2天" }, { "Duration", "数秒至数分钟" }, { "Companion", "行走不稳" }, { "ExamSummary", "本例查体要点" }, { "Risks", "高血压、吸烟" }, { "Tests", "测试检查日期\r\n测试结果" }, { "VteMode", "low" }, { "VteScore", "2" } }) s.Set(p.Key, p.Value);
            s.Selected.Add("ddx-7"); return s;
        }
        public static int Core(string dir)
        {
            Directory.CreateDirectory(dir); results.Clear();
            try
            {
                Library lib = Program.ReadLibrary(); Check(lib.Templates.Count(t => t.Kind == "ddx") == 13, "13个完整鉴别诊断分组");
                Template stroke = lib.Get("ddx-7"); Check(stroke.Text.Split('\n').Length == 6 && stroke.Text.Contains("脑梗死：") && stroke.Text.EndsWith("头部CT和MRI多正常，排除其他疾病后可以诊断。"), "急性脑血管病整组6段直至TIA结尾");
                Check(lib.Get("ddx-36").Text.Split('\n').Length == 4 && lib.Get("ddx-36").Text.Contains("脑出血："), "黄色脑出血正文保留在意识丧失/晕厥组内");
                Check(lib.Get("ddx-68").Text.Split('\n').Length == 3, "最后面瘫组边界到诊疗计划前正确");
                Check(lib.Matches(stroke, "jxnxgb") && lib.Matches(stroke, "jixingnaoxueguanbing") && lib.Matches(stroke, "ngs"), "组名和组内疾病支持首字母与全拼搜索");
                State blank = State.New(lib); string blankText = Generator.All(lib, blank);
                Check(Generator.Pending(blankText) && blank.Get("Age") == "" && blank.Get("Gender") == "" && !blankText.Contains("神经系统查体大致同前"), "空白病例不默认患者信息或正常查体");
                Check(!blankText.Contains("右侧肢体活动不灵") && !blankText.Contains("头晕40余年") && !blankText.Contains("暂考虑急性脑梗死"), "不把两个示例病例事实载入新患者");
                State s = Filled(lib); string text = Generator.All(lib, s);
                Check(!Generator.Pending(text), "完整填写后生成文本无待补充字段");
                Check(text.StartsWith("诊断依据：\r\n1.患者系老年男性，急性起病，因发作性头晕2天入院"), "诊断依据第一句话按原格式拼接");
                Check(text.Contains("，持续数秒至数分钟，伴行走不稳"), "持续时间和伴随症状可填入");
                Check(text.Contains("3.既往合并高血压、吸烟危险因素；4.院前辅助检查：测试检查日期\r\n测试结果"), "既往风险与粘贴辅助检查保留原编号和换行");
                s.Set("RiskOn", "false"); s.Set("CompanionOn", "false"); text = Generator.Diagnosis(s);
                Check(!text.Contains("危险因素") && !text.Contains("，伴") && text.Contains("3.院前辅助检查"), "取消伴随症状和危险因素并连续编号");
                s.Set("ExamAsAbove", "true"); s.Set("TestsAsAbove", "true"); text = Generator.Diagnosis(s);
                Check(text.Contains("神经系统查体大致同前") && text.Contains("院前辅助检查结果如上") && !text.Contains("测试检查日期"), "仅显式勾选时引用上文查体与检查");
                s.Set("ExtraEvidence", "其他实际依据"); Check(Generator.Diagnosis(s).Contains("4.其他实际依据"), "额外诊断依据追加连续编号");
                s.Selected.Add("ddx-14"); text = Generator.Differential(lib, s);
                Check(text.IndexOf("急性脑血管病：") < text.IndexOf("头晕：") && text.Contains("小脑出血或梗死"), "多选鉴别组按原顺序完整输出");
                s.Overrides[stroke.Id] = "本例改写内容"; Check(Generator.Differential(lib, s).Contains("本例改写内容") && lib.Get(stroke.Id).Text != "本例改写内容", "本例改写不修改原始模板库");
                s.Set("VteMode", "low"); s.Set("VteScore", "2"); text = Generator.Vte(lib, s); Check(text.Contains("评分2分<4分，低危人群") && text.Contains("基础预防"), "VTE低危按手选原模板和填写评分生成");
                s.Set("VteMode", "high"); s.Set("VteScore", "4"); text = Generator.Vte(lib, s); Check(text.Contains("评分4分≥4分") && text.Contains("机械预防"), "VTE中高危模板与评分生成");
                s.Set("VteScore", "2"); Check(Generator.Vte(lib, s).Contains("[待核对"), "评分与手选原模板不一致时标记待核对");
                s.Set("VteScore", ""); Check(Generator.Vte(lib, s).Contains("[待补充"), "VTE未填评分不会补造数字");
                s.Set("VteMode", "skip"); Check(!Generator.Plan(lib, s).Contains("患者VTE"), "明确选择不纳入时省略VTE");
                s.Set("VteMode", "custom"); s.Set("VteCustom", "本例自行填写的VTE评估与安排"); Check(Generator.Plan(lib, s).Contains(s.Get("VteCustom")), "支持自定义本例VTE正文");
                s.Set("VteMode", "low"); s.Set("VteScore", "2"); s.Set("VteScale", "测试量表"); Check(Generator.Vte(lib, s).Contains("VTE（测试量表）评分2分"), "量表名称仅按用户填写输出");
                Check(State.New(lib).Get("PlanBody") == lib.Get("plan-common").Text, "新患者使用当前通用评估计划模板");
                s.Set("AnalysisOn", "true"); s.Set("Analysis", Generator.AnalysisSkeleton("stroke")); Check(Generator.Pending(Generator.Plan(lib, s)), "病例分析骨架带待补充字段而不带示例事实");
                Check(!Generator.AnalysisSkeleton("dizziness").Contains("老年女性") && !Generator.AnalysisSkeleton("stroke").Contains("双联抗血小板"), "两种骨架不替用户决定诊断、治疗或性别");
                s.Set("Analysis", "本例临床人员填写的分析。"); text = Generator.Plan(lib, s); Check(text.IndexOf("病情分析与个体化计划") < text.IndexOf("患者VTE"), "选填个体化分析置于VTE之前");
                string customPath = Path.Combine(dir, "custom-core.xml"); Template custom = lib.Add("ddx", "自定义鉴别组", "通用鉴别正文\r\n第二行", ""); lib.Add("plan", "自定义通用计划", "通用计划正文", ""); lib.SaveCustom(customPath);
                Library loaded = Program.ReadLibrary(); loaded.MergeCustom(customPath); Check(loaded.Templates.Count(t => t.Custom) == 2 && loaded.Get(custom.Id).Text == custom.Text, "鉴别与计划自定义模板保存恢复且多行不变");
                Check(loaded.Matches(loaded.Get(custom.Id), "zdyjbz"), "新增模板自动首字母搜索");
                bool rejected = false; try { lib.Add("ddx", "", "", ""); } catch (ArgumentException) { rejected = true; } Check(rejected, "自定义必填验证");
                s.Preview = text + "\r\n人工编辑"; s.Edited = true; s.FontSize = 19; s.Step = 3; s.Signature = Generator.Signature(lib, s);
                string draftPath = Path.Combine(dir, "draft-core.xml"); s.Save(draftPath); State recovered = State.Load(draftPath);
                Check(recovered.Selected.SetEquals(s.Selected) && recovered.Get("Chief") == s.Get("Chief") && recovered.Preview == s.Preview && recovered.Overrides[stroke.Id] == s.Overrides[stroke.Id] && recovered.Get("VteScore") == "2" && recovered.Edited && recovered.FontSize == 19 && recovered.Step == 3, "草稿恢复全部字段、鉴别临时改写、VTE、预览和字号");
                s.Preview = "再次保存"; s.Save(draftPath); Check(State.Load(draftPath + ".bak").Preview == recovered.Preview, "原子保存与前一份草稿备份");
                string export = Path.Combine(dir, "export.txt"); File.WriteAllText(export, text, new UTF8Encoding(true)); Check(File.ReadAllText(export) == text && File.ReadAllBytes(export)[0] == 0xEF, "TXT正文、换行、中文及UTF8 BOM保留");
                File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true)); return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true)); return 1; }
        }
        public static void UI(MainWindow w, string dir)
        {
            results.Clear(); string oldText = null; try { oldText = ClipboardText(); } catch { }
            try
            {
                ((ComboBox)w.Fields["Age"]).SelectedItem = "老年"; ((ComboBox)w.Fields["Gender"]).SelectedItem = "男性"; ((ComboBox)w.Fields["Onset"]).SelectedItem = "急性起病";
                ((TextBox)w.Fields["Chief"]).Text = "发作性头晕2天"; ((TextBox)w.Fields["Duration"]).Text = "数秒至数分钟"; ((TextBox)w.Fields["Companion"]).Text = "行走不稳"; ((TextBox)w.Fields["ExamSummary"]).Text = "本例查体要点"; ((TextBox)w.Fields["Risks"]).Text = "高血压、吸烟"; ((TextBox)w.Fields["Tests"]).Text = "测试日期\r\n本例辅助检查结果";
                Check(w.State.Get("Age") == "老年" && w.State.Get("Gender") == "男性" && w.State.Get("Chief") == "发作性头晕2天", "实际选择和输入控件正确写入患者字段");
                ((ComboBox)w.Fields["Age"]).Text = "高龄"; w.Window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle); Check(w.State.Get("Age") == "高龄", "选项也能直接改写");
                ((ComboBox)w.Fields["Age"]).Text = "老年";
                ((CheckBox)w.Fields["RiskOn"]).IsChecked = false; w.Window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle); Check(!w.Fields.ContainsKey("Risks") && !Generator.Diagnosis(w.State).Contains("危险因素"), "取消危险因素隐藏框并省略正文");
                w.GoTo(2); TextBox search = (TextBox)w.Fields["DdxSearch"]; search.Text = "jxnxgb"; w.RenderDdxList();
                StackPanel cards = (StackPanel)((StackPanel)w.Find<StackPanel>("FormContent")).Children.OfType<StackPanel>().First();
                Check(cards.Children.OfType<Border>().Count() == 1, "首字母搜索定位完整鉴别组");
                CheckBox groupCheck = ((DockPanel)cards.Children.OfType<Border>().First().Child).Children.OfType<CheckBox>().First(); groupCheck.IsChecked = true; Check(w.State.Selected.Contains("ddx-7"), "实际勾选整组事件生效");
                search.Clear(); w.RenderDdxList(); Check(w.State.Selected.Contains("ddx-7"), "清除搜索保留整组选择");
                w.GoTo(3); ComboBox vte = (ComboBox)w.Fields["VteMode"]; vte.SelectedItem = vte.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "low"); w.Window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
                ((TextBox)w.Fields["VteScore"]).Text = "2"; w.Generate(false); Check(!Generator.Pending(w.Preview.Text) && w.Preview.Text.Contains("评分2分<4分"), "表单完整生成诊断依据、整组鉴别和VTE");
                w.CopyAll(); string clipboard = null; try { clipboard = ClipboardText(); } catch { results.Add("SKIP 本轮系统剪贴板被占用，完整复制未验证"); } if (clipboard != null) Check(clipboard == w.Preview.Text, "系统剪贴板复制完整首程正文");
                w.Preview.AppendText("\r\n人工补充"); w.State.Set("Chief", "新的本例主诉"); w.RefreshMeta(); Check(w.Stale && w.State.Edited && w.Preview.Text.Contains("人工补充"), "修改患者变量不覆盖人工编辑且提示需更新");
                w.Generate(false); w.GoTo(1); Capture(w.Window, dir, "界面_诊断依据.png", 1320, 860); Capture(w.Window, dir, "界面_小窗口.png", 1000, 780); Capture(w.Window, dir, "界面_最小窗口.png", 820, 720);
                Check(Grid.GetRow(w.Find<Border>("PreviewPanel")) == 2 && w.Preview.ActualHeight >= 35, "最小窗口上下布局且文本预览可见");
                w.GoTo(2); Capture(w.Window, dir, "界面_鉴别整组.png", 1320, 860);
                Window groupDialog = w.GroupDialog(w.Library.Get("ddx-7")); groupDialog.Show(); Capture(groupDialog, dir, "界面_整组改写.png", 770, 720); groupDialog.Close();
                w.GoTo(3); Capture(w.Window, dir, "界面_诊疗计划VTE.png", 1320, 860);
                w.State.Set("AnalysisOn", "true"); w.State.Set("Analysis", Generator.AnalysisSkeleton("dizziness")); w.Render(); w.Generate(false); Check(!w.Ready(w.Preview.Text, false, false), "有骨架待填字段时拦截输出");
                w.State.Set("Analysis", "本例填写的病情分析"); w.State.Set("VteScore", "5"); w.Generate(false); Check(!w.Ready(w.Preview.Text, false, false), "所填评分与所选分组不一致时拦截输出");
                w.State.Set("VteScore", "2"); w.Generate(false); Check(w.Ready(w.Preview.Text, false, false), "所有待填核对完成后允许输出");
                Template custom = w.AddCustom("ddx", "自定义鉴别", "自定义通用正文", ""); w.State.Selected.Add(custom.Id); w.SetOverride(w.Library.Get("ddx-7"), "仅用于当前患者的临时改写"); w.Preview.FontSize = 20; w.Generate(false); w.Preview.AppendText("\r\n手动编辑"); w.Save();
                MainWindow restored = new MainWindow(w.Profile); Check(restored.Library.Templates.Any(t => t.Id == custom.Id) && restored.State.Selected.SetEquals(w.State.Selected) && restored.State.Get("Chief") == w.State.Get("Chief") && restored.State.Overrides["ddx-7"] == w.State.Overrides["ddx-7"] && restored.Preview.Text == w.Preview.Text && restored.Preview.FontSize == 20, "重启恢复自定义模板、患者字段、本例改写与预览");
                w.Save(); File.WriteAllText(Path.Combine(w.Profile, "draft.xml"), "broken"); MainWindow backup = new MainWindow(w.Profile); Check(backup.Preview.Text == w.Preview.Text && backup.Find<TextBlock>("StatusText").Text.Contains("备份"), "损坏草稿恢复上一份备份");
                w.NewPatient(true); Check(w.State.Selected.Contains("ddx-7") && w.State.Get("Age") == "" && w.State.Get("Chief") == "" && w.State.Get("VteScore") == "" && w.State.Overrides.Count == 0 && w.Preview.Text == "" && w.Library.Templates.Any(t => t.Id == custom.Id), "新患者保留组选择和通用模板但清空患者事实、临时改写与VTE");
                w.GoTo(2); Check(w.Ready(Generator.Part(w.Library, w.State, 2), false, false), "可单独复制鉴别模块，无须先填其他患者字段");
                Window reference = w.ReferenceDialog(); reference.Show(); Capture(reference, dir, "界面_参考说明.png", 780, 740); reference.Close();
                File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true));
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true)); Environment.ExitCode = 1; }
            finally { if (oldText != null) try { Clipboard.SetText(oldText); } catch { } w.Window.Close(); }
        }
        private static string ClipboardText() { for (int i = 0; i < 20; i++) try { return Clipboard.GetText(); } catch { System.Threading.Thread.Sleep(50); } throw new Exception("系统剪贴板占用"); }
        private static void Capture(Window w, string dir, string file, int width, int height)
        {
            w.Width = width; w.Height = height; w.UpdateLayout(); FrameworkElement content = (FrameworkElement)w.Content;
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right), (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32); bitmap.Render(w); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (FileStream f = File.Create(Path.Combine(dir, file))) encoder.Save(f);
        }
    }
}
