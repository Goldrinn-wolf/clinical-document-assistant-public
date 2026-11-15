using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ProgressNotesAssistant
{
    // These checks operate the same controls and dialog Save/Cancel paths as the application.
    // All patient-like values below are synthetic test strings and are stored in a test profile.
    public static class Tests
    {
        private static readonly List<string> Results = new List<string>();
        private static readonly List<Window> ExtraWindows = new List<Window>();
        private static int Passed;
        private static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAIL " + label);
            Passed++; Results.Add("PASS " + label);
        }
        private static void Flush(Window window)
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        private static void Click(Button button)
        {
            if (button == null) throw new Exception("测试所需按钮不存在。");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        private static void Input(MainWindow main, string key, string value)
        {
            TextBox input = main.Fields[key] as TextBox;
            if (input == null) throw new Exception("字段不是输入框：" + key);
            input.Text = value; Flush(main.Window);
        }
        private static void SelectReference(ComboBox combo, ReferenceText reference)
        {
            if (combo == null) throw new Exception("参考选择框不存在。");
            object selected = null;
            foreach (object item in combo.Items)
            {
                ReferenceText direct = item as ReferenceText;
                ComboBoxItem card = item as ComboBoxItem;
                ReferenceText tagged = card == null ? null : card.Tag as ReferenceText;
                if ((direct != null && direct.Id == reference.Id) ||
                    (tagged != null && tagged.Id == reference.Id) ||
                    (card != null && String.Equals(Convert.ToString(card.Tag), reference.Id, StringComparison.Ordinal)) ||
                    (card != null && String.Equals(Convert.ToString(card.Content), reference.Title, StringComparison.Ordinal)) ||
                    String.Equals(Convert.ToString(item), reference.Title, StringComparison.Ordinal))
                { selected = item; break; }
            }
            if (selected == null) throw new Exception("参考选项未包含：" + reference.Id);
            combo.SelectedItem = selected;
        }
        private static void Open(Window window)
        {
            ExtraWindows.Add(window); window.Show(); Flush(window);
        }
        private static FrameworkElement Named(MainWindow main, string name)
        {
            FrameworkElement item = main.Window.FindName(name) as FrameworkElement;
            if (item == null) throw new Exception("界面命名元素不存在：" + name);
            return item;
        }
        private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                T typed = child as T; if (typed != null) yield return typed;
                foreach (T descendant in Visuals<T>(child)) yield return descendant;
            }
        }
        private static void DoubleClickField(MainWindow main, string key, string value)
        {
            bool opened = false; Exception failure = null;
            main.Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                Window editor = null;
                try
                {
                    editor = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Owner == main.Window && w.Title.StartsWith("修改：", StringComparison.Ordinal) && w.IsVisible);
                    if (editor == null) throw new Exception("双击后没有打开字段编辑窗口。");
                    opened = true; Visuals<TextBox>(editor).First().Text = value;
                    Click(Visuals<Button>(editor).First(b => Convert.ToString(b.Content) == "保存"));
                }
                catch (Exception error) { failure = error; if (editor != null) editor.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            main.Fields[key].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            Flush(main.Window);
            if (failure != null) throw failure;
            Check(opened && main.State.Get(main.State.CurrentModule, key) == value, "实际双击只读字段会打开编辑窗口，保存按钮写入对应字段");
        }
        private static bool OnScreen(Window window, FrameworkElement item)
        {
            FrameworkElement content = window.Content as FrameworkElement;
            if (content == null || !item.IsVisible || item.ActualWidth <= 0 || item.ActualHeight <= 0) return false;
            Rect bounds = item.TransformToAncestor(content).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1;
        }
        private static void Capture(Window window, string directory, string file, int width, int height)
        {
            window.Width = width; window.Height = height; Flush(window);
            FrameworkElement content = window.Content as FrameworkElement;
            if (content == null || content.ActualWidth < 1 || content.ActualHeight < 1) throw new Exception("无法捕获已布局的窗口。");
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            // VisualBrush normalizes the arranged visual's margin/offset and monitor DPI.
            // Rendering content directly includes its layout translation and clips right/bottom edges.
            DrawingVisual drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                Rect bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                context.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
                context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, bounds);
            }
            bitmap.Render(drawing);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(Path.Combine(directory, file))) encoder.Save(stream);
        }
        public static string ClipboardText()
        {
            for (int attempt = 0; attempt < 10; attempt++)
                try { return Clipboard.GetText(); }
                catch (ExternalException) { System.Threading.Thread.Sleep(40); }
            throw new ExternalException("系统剪贴板占用。");
        }
        public static bool SnapshotClipboard(out IDataObject snapshot)
        {
            snapshot = null;
            try
            {
                IDataObject source = Clipboard.GetDataObject();
                if (source != null)
                {
                    DataObject copy = new DataObject();
                    foreach (string format in source.GetFormats(false))
                    {
                        object data = source.GetData(format, false); if (data != null) copy.SetData(format, data, false);
                    }
                    snapshot = copy;
                }
                return true;
            }
            catch (ExternalException) { return false; }
        }
        public static void ClipboardOnly(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); Results.Clear(); Passed = 0;
            IDataObject original; bool backedUp = SnapshotClipboard(out original), changed = false;
            try
            {
                if (!backedUp) Results.Add("SKIP 本轮无法备份系统剪贴板；保留原内容，实际复制未验证");
                else
                {
                    main.NewPatient(false); main.SelectModule("daily");
                    Input(main, "vitals", "中文剪贴板测试：36.5℃，120/70mmHg，符号<&>\r\n第二行测试内容");
                    main.Generate(); Check(main.CanOutput(), "独立剪贴板测试使用完整已生成的 Unicode 文本");
                    try
                    {
                        changed = true; Check(main.CopyToClipboard(), "应用实际复制接口成功写入系统剪贴板");
                        string copied = ClipboardText();
                        Check(copied == main.Preview.Text && copied.Contains("中文剪贴板测试") && copied.Contains("℃") && copied.Contains("<&>") && copied.Contains("\r\n第二行测试内容"), "Unicode 中文、符号及换行的系统剪贴板内容与预览逐字一致");
                        Input(main, "vitals", "未重新生成的输入");
                        Check(!main.CopyToClipboard() && ClipboardText() == copied, "过期预览拒绝复制并保留原剪贴板内容");
                    }
                    catch (ExternalException) { Results.Add("SKIP 本轮系统剪贴板被占用；实际复制未验证"); }
                }
                Results.Add("TOTAL " + Passed + " clipboard checks passed");
            }
            catch (Exception error) { Results.Add(error.ToString()); Environment.ExitCode = 1; }
            finally
            {
                if (changed) try { if (original == null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true); } catch (ExternalException) { Results.Add("SKIP 系统剪贴板占用，未能恢复测试前的剪贴板"); }
                File.WriteAllLines(Path.Combine(directory, "clipboard-tests.txt"), Results, new UTF8Encoding(true));
                main.Window.Close();
            }
        }
        public static void UI(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); Results.Clear(); ExtraWindows.Clear(); Passed = 0;
            IDataObject oldClipboard = null; bool clipboardRead = false, clipboardChanged = false;
            clipboardRead = SnapshotClipboard(out oldClipboard);
            try
            {
                main.NewPatient(false); main.SelectModule("day2"); main.Generate(); Flush(main.Window);
                Check(main.Library.Modules.Count == 8 && main.Library.Exams.Count == 37, "8 份模块正文与 37 个检查项目完整载入实际界面");
                Check(main.Preview.IsReadOnly && main.CanOutput() && main.Preview.Text == Generator.Record(main.Library, main.State, "day2"), "生成预览只读，默认正文沿用指定模板且完整时允许输出");
                Check(main.Preview.Text.Contains("辅助检查：\r\n某某主任医师查房看过病人后示：\r\n") && !main.Preview.Text.Contains("主动脉反流"), "初始辅助检查与处理为空，不自行载入患者示例");

                Input(main, "vitals", "测试体温36.5℃，测试血压120/70mmHg");
                Check(main.State.Get("day2", "vitals") == "测试体温36.5℃，测试血压120/70mmHg" && !main.CanOutput(), "实际输入生命体征写入本模块并使旧预览暂不可输出");
                main.Generate(); Check(main.Preview.Text.Contains("查体：测试体温36.5℃，测试血压120/70mmHg。") && main.CanOutput(), "重新生成后仅替换标记的生命体征位置");
                Input(main, "vitals", ""); main.Generate();
                Check(main.Preview.Text.Contains("体温:36.2℃,脉搏:65次/分,呼吸:15次/分,血压:135/68mmHg"), "清空输入框恢复用户指定默认生命体征");
                FieldDefinition diet = main.Library.GetModule("day2").Fields.First(f => f.Key == "diet");
                FieldDialog field = main.CreateFieldDialog(diet);
                Check(field.Input.Text == "", "双击编辑弹窗不把默认值当作已经输入的患者字段");
                field.Input.Text = "食纳欠佳（测试）"; field.Save();
                Check(main.State.Get("day2", "diet") == "食纳欠佳（测试）", "双击字段编辑保存到当前模块");
                DoubleClickField(main, "diet", "食纳欠佳（测试）");
                FieldDialog cancelField = main.CreateFieldDialog(diet); cancelField.Input.Text = "取消内容不应保存"; cancelField.Cancel();
                Check(main.State.Get("day2", "diet") == "食纳欠佳（测试）", "取消双击修改保留原字段，不写入草稿");

                ExamDialog canceledExams = main.CreateExamsDialog();
                canceledExams.Rows["echo"].Selected.IsChecked = true; canceledExams.Rows["echo"].Result.Text = "取消检查不应保存"; canceledExams.Cancel();
                Check(!main.State.Exam("echo").Selected && main.State.Exam("echo").Result == "", "辅助检查取消时不修改当前患者检查池");
                ExamDialog exams = main.CreateExamsDialog();
                Check(exams.Rows.Count == main.Library.Exams.Count && exams.Rows.Values.All(r => r.Result.Text == ""), "辅助检查逐项提供勾选与空白粘贴框");
                exams.Rows["stool"].Selected.IsChecked = true; exams.Rows["urine"].Selected.IsChecked = true;
                ExamRow echo = exams.Rows["echo"];
                SelectReference(echo.Reference, main.Library.GetExam("echo").References[0]);
                Check(echo.Result.Text == "", "选择参考名称不会自动填入示例正文");
                Click(echo.LoadReference);
                Check(echo.Result.Text == main.Library.GetExam("echo").References[0].Text && exams.Rows["holter"].Result.Text == "", "只有手动载入参考才填入所选项目，其他检查保持空白");
                echo.Selected.IsChecked = true; echo.Result.Text = "测试报告：主动脉反流\r\n测试第二行";
                exams.Rows["coagulation"].Result.Text = "未勾选的暂存测试结果";
                exams.Rows["coagulation"].Selected.IsChecked = false; exams.Save(); main.Generate();
                Check(main.State.Exam("echo").Selected && main.State.Exam("echo").Result == echo.Result.Text, "辅助检查保存勾选及多行手工粘贴内容");
                Check(main.Preview.Text.Contains("心脏彩超：测试报告：主动脉反流\r\n测试第二行\r\n" + String.Join("、", main.Library.Exams.Where(e => e.Category == "basic").Select(e => e.Title)) + "、粪便常规、尿常规无明显异常。"), "有字项目带原前缀输出，空白勾选项目汇总在辅助检查末尾");
                Check(!main.Preview.Text.Contains("未勾选的暂存测试结果") && main.Preview.Text.Split(new[] { "无明显异常。" }, StringSplitOptions.None).Length == 2, "撤勾内容不输出，多个正常项目只写一次正常结尾");
                Check(!main.CanOutput(), "有异常但尚未填写人工处理时，复制与导出就绪判定失败");
                Check(!main.ExportTo(Path.Combine(directory, "pending-must-not-export.txt")) && !File.Exists(Path.Combine(directory, "pending-must-not-export.txt")), "待填写处理的正文不能写出成最终 TXT");

                HandlingDialog handling = main.CreateHandlingDialog();
                Check(handling.Rows.Count == 1 && handling.Rows.ContainsKey("echo") && handling.Rows["echo"].Treatment.Text == "", "处理弹窗只列非空勾选检查并提供空白人工方案框");
                handling.Rows["echo"].Treatment.Text = "本例手工填写的处理方案"; handling.Rows["echo"].Included.IsChecked = true; handling.Save(); main.Generate();
                Check(main.Preview.Text.Contains("针对患者心脏彩超异常（测试报告：主动脉反流\r\n测试第二行），予以本例手工填写的处理方案处理。") && main.CanOutput(), "人工方案按统一格式关联完整异常结果并可输出");
                HandlingDialog canceledHandling = main.CreateHandlingDialog(); canceledHandling.Rows["echo"].Treatment.Text = "取消方案不应保存"; canceledHandling.Cancel();
                Check(main.State.Exam("echo").Treatment == "本例手工填写的处理方案", "取消处理弹窗不会改变已保存方案");
                main.SelectModule("daily"); main.Generate();
                Check(main.Preview.Text.Contains("本例手工填写的处理方案") && main.Preview.Text.Contains("食纳、二便、夜休") && main.State.Get("daily", "diet") == "", "同患者日常模块复用异常和处理，模块本例字段保持独立");
                main.SelectModule("chief");
                FieldDialog physician = main.CreateFieldDialog(main.Library.GetModule("chief").Fields.First(f => f.Key == "physician")); physician.Input.Text = "测试主任"; physician.Save(); main.Generate();
                Check(main.Preview.Text.Contains("测试主任主任医师查房看过病人后示") && main.Preview.Text.Contains("本例手工填写的处理方案"), "普通主任查房医生字段可改并共用当前患者处理池");
                ExamDialog changed = main.CreateExamsDialog(); changed.Rows["echo"].Result.Text = "测试更新后的检查结果"; changed.Save(); main.Generate();
                Check(Core.TreatmentNeedsReview(main.State.Exam("echo")) && main.State.Exam("echo").Treatment == "本例手工填写的处理方案" && !main.CanOutput(), "检查结果改变保留原方案并要求明确核对，旧方案不直接输出");
                HandlingDialog noChange = main.CreateHandlingDialog(); noChange.Save(); main.Generate();
                Check(Core.TreatmentNeedsReview(main.State.Exam("echo")) && !main.CanOutput(), "单纯保存处理弹窗不会静默确认变更后的结果");
                HandlingDialog confirm = main.CreateHandlingDialog(); Click(confirm.Rows["echo"].Confirm); confirm.Save(); main.Generate();
                Check(!Core.TreatmentNeedsReview(main.State.Exam("echo")) && main.CanOutput() && main.Preview.Text.Contains("测试更新后的检查结果"), "明确核对按钮使原处理与新检查结果重新关联");
                ExamDialog deselect = main.CreateExamsDialog(); deselect.Rows["echo"].Selected.IsChecked = false; deselect.Save(); main.Generate();
                Check(!main.Preview.Text.Contains("心脏彩超") && main.State.Exam("echo").Treatment == "本例手工填写的处理方案", "取消检查停止输出相关结果及处理且保留人工暂存内容");
                ExamDialog reselect = main.CreateExamsDialog(); reselect.Rows["echo"].Selected.IsChecked = true; reselect.Save(); main.Generate();
                Check(main.Preview.Text.Contains("本例手工填写的处理方案") && main.CanOutput(), "重新勾选同一结果可恢复原人工方案");
                ExamDialog normalEcho = main.CreateExamsDialog(); normalEcho.Rows["echo"].Result.Clear(); normalEcho.Save(); main.Generate();
                HandlingDialog emptyHandling = main.CreateHandlingDialog();
                Check(main.Preview.Text.Contains("粪便常规、尿常规、心脏彩超无明显异常。") && !main.Preview.Text.Contains("予以本例手工填写的处理方案处理") && emptyHandling.Rows.Count == 0, "将检查清为空白后只纳入正常汇总，旧处理不残留输出"); emptyHandling.Cancel();

                ExamDialog variants = main.CreateExamsDialog();
                ExamRow holter = variants.Rows["holter"];
                SelectReference(holter.Reference, main.Library.GetExam("holter").References[1]);
                Check(holter.Result.Text == "" && main.Library.GetExam("holter").References.Count == 2, "两份动态心电图作为同一检查的两个手动参考选项");
                Click(holter.LoadReference); holter.Selected.IsChecked = true;
                Check(holter.Result.Text == main.Library.GetExam("holter").References[1].Text && !holter.Result.Text.Contains("不完全性右束支阻滞"), "载入动态心电图第二参考不会叠加第一参考");
                ExamRow gene = variants.Rows["genetic"];
                Check(gene.Result.Text == "", "基因检查不默认患者代谢型及原例用药建议");
                SelectReference(gene.ChoiceA, main.Library.GetExam("genetic").References.First(r => r.Id == "genetic-r1"));
                SelectReference(gene.ChoiceB, main.Library.GetExam("genetic").References.First(r => r.Id == "genetic-r4"));
                Check(gene.Result.Text == "", "分别选择两种药物基因参考仍需手动载入");
                Click(gene.LoadReference); gene.Selected.IsChecked = true;
                Check(gene.Result.Text.Contains("正常代谢型") && gene.Result.Text.Contains("中间敏感型") && !gene.Result.Text.Contains("中间代谢型") && !gene.Result.Text.Contains("快代谢 敏感型"), "两药参考各只载入手选的一个类型");
                variants.Save(); main.Generate();
                HandlingDialog excluded = main.CreateHandlingDialog();
                Check(excluded.Rows.Count == 2 && excluded.Rows.ContainsKey("holter") && excluded.Rows.ContainsKey("genetic"), "新载入的非空检查分别进入异常处理池");
                excluded.Rows["holter"].Included.IsChecked = false; excluded.Rows["genetic"].Included.IsChecked = false; excluded.Save(); main.Generate();
                Check(main.CanOutput() && main.Preview.Text.Contains("动态心电图：") && !main.Preview.Text.Contains("针对患者动态心电图异常"), "可单独不纳入处理，检查结果仍保留输出");

                main.SelectModule("preop"); main.Generate();
                Check(Generator.Pending(main.Preview.Text) && main.Fields.Count == 0 && !main.CanOutput() && !main.ExportTo(Path.Combine(directory, "preop-must-not-export.txt")), "术前小结待整理入口不制造模板或允许作为完成文书导出");
                main.SelectModule("postop"); main.Generate(); Check(!main.CanOutput(), "术后症状、患者描述和造影结果未填时不能输出");
                FieldDialog symptom = main.CreateFieldDialog(main.Library.GetModule("postop").Fields.First(f => f.Key == "symptom")); symptom.Input.Text = "测试头晕"; symptom.Save();
                FieldDialog patient = main.CreateFieldDialog(main.Library.GetModule("postop").Fields.First(f => f.Key == "patienttype")); patient.Input.Text = "测试病情描述"; patient.Save();
                Input(main, "angio", "本例人工粘贴的造影报告\r\n报告第二行"); main.Generate();
                Check(main.CanOutput() && ((TextBox)main.Fields["angio"]).AcceptsReturn && main.Preview.Text.Contains("此次脑血管造影示本例人工粘贴的造影报告\r\n报告第二行"), "术后大框允许多行粘贴并按原文位置生成");
                Check(main.Preview.Text.Contains("右侧腹股沟穿刺点敷料干燥，无渗血渗液") && main.Preview.Text.Contains("继续完善基底动脉高分辨、高分辨增强以及BPAS检查"), "术后未标记文字完整保持用户提供原文");
                Capture(main.Window, directory, "界面_术后第一天.png", 1340, 860);
                main.SelectModule("discharge");
                FieldDialog dischargeSymptom = main.CreateFieldDialog(main.Library.GetModule("discharge").Fields.First(f => f.Key == "symptom")); dischargeSymptom.Input.Text = "测试眩晕"; dischargeSymptom.Save();
                FieldDialog dischargeDay = main.CreateFieldDialog(main.Library.GetModule("discharge").Fields.First(f => f.Key == "day"));
                Check(dischargeDay.Options != null && dischargeDay.Options.Items.Count == 2, "出院安排弹窗提供今日与明日选项");
                dischargeDay.Options.SelectedItem = dischargeDay.Options.Items.Cast<object>().First(i => Convert.ToString(i) == "明日"); dischargeDay.Save(); main.Generate();
                Check(main.CanOutput() && main.Preview.Text.Contains("测试眩晕症状较前好转") && main.Preview.Text.Contains("拟于明日带方案"), "出院好转症状和今日明日按实际编辑生成");

                main.SelectModule("lp");
                Check(main.Fields.Count == 2 && main.Fields.ContainsKey("pressure") && main.Fields.ContainsKey("glucose") && main.Preview.IsReadOnly, "腰穿只暴露两个标记输入项，固定正文不可修改");
                Input(main, "pressure", "180"); Input(main, "glucose", "5.6"); main.Generate();
                Check(main.CanOutput() && main.Preview.Text.Contains("测压力180mmH2O") && main.Preview.Text.Contains("术后即刻血糖： 5.6mmol/L") && main.Preview.Text.Contains("第4/5腰椎棘突间隙") && main.Preview.Text.Contains("去枕平卧6小时"), "腰穿数值替换正确，部位、术程和术后原文固定");
                Capture(main.Window, directory, "界面_特殊记录腰穿.png", 1340, 860);
                main.SelectModule("thrombolysis");
                Check(main.Fields.Keys.OrderBy(k => k).SequenceEqual(new[] { "after30", "after50", "bp-after", "bp-before", "start", "stop" }.OrderBy(k => k)), "溶栓只开放两处血压与绝对及相对时间，不开放剂量和NIHSS");
                Input(main, "bp-before", "测试180/90"); Input(main, "bp-after", "测试145/80"); main.Generate();
                Check(main.CanOutput() && main.Preview.Text.Contains("15:10分开始静脉溶栓") && main.Preview.Text.Contains("16:00停止溶栓") && main.Preview.Text.Contains("乌拉地尔12.5mg") && main.Preview.Text.Contains("5mg阿替普酶") && main.Preview.Text.Contains("余45mg") && main.Preview.Text.Contains("NIHSS评分2分"), "溶栓默认时间与未标记的药量、评分、事件正文保持原样");
                Input(main, "start", "17:10"); Input(main, "stop", "18:00"); Input(main, "after30", "35"); Input(main, "after50", "45"); main.Generate();
                Check(main.CanOutput() && main.Preview.Text.Contains("17:10分开始") && main.Preview.Text.Contains("18:00停止") && main.Preview.Text.Contains("溶栓35分钟") && main.Preview.Text.Contains("溶栓后45分钟"), "起止时间和两个事件分钟按人工填写更新");
                Input(main, "start", "25:10"); main.Generate(); Check(!main.CanOutput() && main.Preview.Text.Contains("有效的"), "无效时钟格式不能作为完成记录输出");
                Input(main, "start", "23:40"); Input(main, "stop", "00:30"); main.Generate(); Check(!main.CanOutput() && main.Preview.Text.Contains("是否跨日"), "仅时分的停止早于开始时要求人工核对跨日");
                Input(main, "start", "2026-10-14 23:40"); Input(main, "stop", "2026-10-15 00:30"); main.Generate();
                Check(main.CanOutput(), "明确填写完整日期时间可记录合法跨日溶栓时序");
                Input(main, "start", "17:10"); Input(main, "stop", "18:00"); main.Generate();
                Capture(main.Window, directory, "界面_特殊记录溶栓.png", 1340, 860);

                string export = Path.Combine(directory, "ui-export.txt");
                Check(main.ExportTo(export) && File.ReadAllText(export) == main.Preview.Text && File.ReadAllBytes(export).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "实际 TXT 导出保留预览正文、中文和 UTF-8 BOM");
                if (clipboardRead)
                    try { clipboardChanged = true; Check(main.CopyToClipboard() && ClipboardText() == main.Preview.Text, "实际系统剪贴板复制与生成预览完全一致"); }
                    catch (ExternalException) { Results.Add("SKIP 本轮系统剪贴板被占用；实际复制未验证"); }
                else Results.Add("SKIP 本轮无法备份系统剪贴板；保留原剪贴板，实际复制未验证");
                main.Preview.Text += "\r\n非生成文本";
                Check(!main.CanOutput() && !main.ExportTo(Path.Combine(directory, "tampered-must-not-export.txt")), "非生成或过期预览不能导出为完成文书"); main.Generate();

                main.Save(); MainWindow restored = new MainWindow(main.Profile); ExtraWindows.Add(restored.Window);
                Check(restored.State.CurrentModule == "thrombolysis" && restored.State.Get("day2", "diet") == "食纳欠佳（测试）" && restored.State.Get("chief", "physician") == "测试主任" && restored.State.Get("thrombolysis", "start") == "17:10", "重启恢复各模块独立字段和当前模块");
                Check(restored.State.Exam("holter").Selected && restored.State.Exam("genetic").Result == main.State.Exam("genetic").Result && !restored.State.Exam("genetic").TreatmentEnabled && restored.State.Exam("echo").Treatment == "本例手工填写的处理方案", "重启恢复共享检查、基因参考、处理开关及人工暂存方案");
                Check(restored.Preview.Text == main.Preview.Text && restored.CanOutput(), "重启恢复完整生成文本与输出就绪状态");

                // Screenshots use a concise synthetic case to show the shared examination/handling workflow.
                main.SelectModule("day2");
                ExamDialog screenshotExams = main.CreateExamsDialog(); screenshotExams.Rows["echo"].Selected.IsChecked = true; screenshotExams.Rows["echo"].Result.Text = "测试异常结果：主动脉反流";
                screenshotExams.Rows["holter"].Selected.IsChecked = false; screenshotExams.Rows["genetic"].Selected.IsChecked = false; screenshotExams.Save();
                HandlingDialog screenshotHandling = main.CreateHandlingDialog(); screenshotHandling.Rows["echo"].Treatment.Text = "临床人员填写的处理（测试）"; screenshotHandling.Rows["echo"].Included.IsChecked = true; screenshotHandling.Save(); main.Generate();
                Capture(main.Window, directory, "界面_入院第二天.png", 1340, 860);
                Check(OnScreen(main.Window, Named(main, "ModulesPanel")) && OnScreen(main.Window, Named(main, "FormPanel")) && OnScreen(main.Window, Named(main, "PreviewPanel")), "1340 窗口模块、表单与生成文本完整可见");
                Capture(main.Window, directory, "界面_1000宽.png", 1000, 780);
                Check(OnScreen(main.Window, Named(main, "PreviewPanel")) && main.Preview.ActualHeight >= 80, "1000 窗口生成预览可见且可滚动阅读");
                Capture(main.Window, directory, "界面_860宽.png", 860, 700);
                Check(OnScreen(main.Window, Named(main, "FormPanel")) && OnScreen(main.Window, Named(main, "PreviewPanel")) && OnScreen(main.Window, Named(main, "GenerateButton")) && main.Preview.ActualHeight >= 60, "860 最小窗口表单、预览与生成按钮仍在可用区域");
                main.Window.Width = 1340; main.Window.Height = 860; Flush(main.Window);
                ExamDialog examScreenshot = main.CreateExamsDialog(); Open(examScreenshot.Window); Capture(examScreenshot.Window, directory, "界面_辅助检查.png", 1030, 850); examScreenshot.Cancel();
                HandlingDialog handlingScreenshot = main.CreateHandlingDialog(); Open(handlingScreenshot.Window); Capture(handlingScreenshot.Window, directory, "界面_异常处理.png", 880, 650); handlingScreenshot.Cancel();
                FieldDialog fieldScreenshot = main.CreateFieldDialog(diet); Open(fieldScreenshot.Window); Capture(fieldScreenshot.Window, directory, "界面_双击修改.png", 600, 380); fieldScreenshot.Cancel();

                main.NewPatient(false); main.Generate();
                Check(main.State.Values.Count == 0 && main.State.Exams.Values.All(e => !e.Selected && e.Result == "" && e.Treatment == "" && e.ChoiceA == "" && e.ChoiceB == ""), "新患者清空各模块输入、共享检查、异常处理及参考选择");
                Check(!main.Preview.Text.Contains("测试") && !main.Preview.Text.Contains("主动脉反流") && main.Library.GetExam("echo").References[0].Text.Contains("主动脉反流"), "新患者不复用上一例事实，通用检查参考仍保留");
                string backup = Path.Combine(main.Profile, "draft.xml.bak");
                if (File.Exists(backup))
                {
                    State cleanBackup = State.Load(backup);
                    Check(cleanBackup.Values.Count == 0 && cleanBackup.Exams.Values.All(e => !e.Selected && e.Result == "" && e.Treatment == ""), "新患者保存连同备份清除上一例患者资料");
                }
                else Check(false, "新患者草稿备份存在并可验证清除上一例患者资料");
                Results.Add("TOTAL " + Passed + " UI checks passed");
                File.WriteAllLines(Path.Combine(directory, "ui-tests.txt"), Results, new UTF8Encoding(true));
            }
            catch (Exception error)
            {
                Results.Add(error.ToString()); File.WriteAllLines(Path.Combine(directory, "ui-tests.txt"), Results, new UTF8Encoding(true)); Environment.ExitCode = 1;
            }
            finally
            {
                if (clipboardChanged) try { if (oldClipboard == null) Clipboard.Clear(); else Clipboard.SetDataObject(oldClipboard, true); } catch (ExternalException) { }
                foreach (Window window in ExtraWindows) try { window.Close(); } catch { }
                main.Window.Close();
            }
        }
    }
}
