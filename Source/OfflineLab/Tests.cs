using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OfflineLab
{
    public static class Tests
    {
        private static List<string> checks = new List<string>();
        private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks.Add("PASS " + name); }
        private static FormatResult Ready(string input, string expected, string label, FormatOptions options = null)
        {
            FormatResult parsed = Formatter.Parse(input, options ?? new FormatOptions());
            Check(parsed.Ready && parsed.Output == expected, label + (parsed.Output == expected ? "" : "；实际=" + parsed.Output)); return parsed;
        }
        public static int Core(string directory)
        {
            Directory.CreateDirectory(directory); checks.Clear();
            try
            {
                Ready("2026100809:58 血细胞五项：粒细胞↑，白细胞↓，", "1、血细胞五项：粒细胞↑，白细胞↓。", "用户给定血细胞示例准确拆分并去除末尾逗号");
                Ready(MainWindow.Sample, "1、血细胞五项：粒细胞↑，白细胞↓；2、肝肾功血糖血脂心肌酶+HYC：丙氨酸氨基转移酶↑；3、肾功能、甲功、凝血无明显异常。", "混合示例有字在前、肝功能替名、三项空结果合并最后");
                foreach (string stamp in new[] { "202610080958", "2026100809:58", "2026-10-08 09:58", "2026/10/8 9:58:32", "2026.10.08 09：58", "2026年10月8日09时58分", "123456789012345678", "99999999 9999", "2026-10-08T09:58:00" })
                {
                    FormatResult p = Formatter.Parse(stamp + " 血常规：白细胞↓", new FormatOptions { IncludeTime = true });
                    Check(p.Ready && p.Records.Count == 1 && p.Records[0].RawTime == stamp && p.Records[0].SourceTitle == "血常规" && p.Records[0].Result == "白细胞↓", "不同数字时间串按原样保留：" + stamp);
                }
                Ready("202610080958血常规：白细胞↓", "1、血常规：白细胞↓。", "数字时间和中文项目名紧贴时也可识别");
                Ready("20261008\t09:58\t血细胞五项:\t白细胞↓\r\n20261008\t10:00\t肝功能:\t", "1、血细胞五项：白细胞↓；2、肝肾功血糖血脂心肌酶+HYC无明显异常。", "网页制表符及半角冒号识别");
                Ready("202610080958 肝功能：\r\n202610080959 肾功能：\r\n202610081000 甲功：", "1、肝肾功血糖血脂心肌酶+HYC、肾功能、甲功无明显异常。", "全部空结果只输出一个正常汇总编号，肾功能不改名");
                Ready("202610080958 肝功能9项：ALT↑\n202610080959 肾功能：肌酐↑", "1、肝肾功血糖血脂心肌酶+HYC：ALT↑；2、肾功能：肌酐↑。", "仅肝功能别名使用组合表头，其他项目保留原名");
                Ready("202610080958 甲状腺功能：TSH↓\n202610080959 肝功能：", "1、甲状腺功能：TSH↓；2、肝功能无明显异常。", "关闭组合表头后保留肝功能原名", new FormatOptions { RenameLiver = false });
                Ready("202610080958 肝功（9项）：", "1、肝肾功血糖血脂心肌酶+HYC无明显异常。", "肝功括号项数别名可识别");
                Ready("202610080958 肝功能：", "1、自定义组合名称无明显异常。", "组合表头可以按科室习惯修改", new FormatOptions { CombinedName = "自定义组合名称" });
                Ready("202610080958 血细胞五项：粒细胞↑\n白细胞↓\n血红蛋白：110↓\n202610081010 甲功：", "1、血细胞五项：粒细胞↑，白细胞↓，血红蛋白：110↓；2、甲功无明显异常。", "异常结果换行及子指标冒号作为续行保留");
                Ready("血常规：白细胞↓\n肝功能：\n肾功能：", "1、血常规：白细胞↓；2、肝肾功血糖血脂心肌酶+HYC、肾功能无明显异常。", "无时间的常用项目按换行辅助识别");
                Ready("1234567890 未知检验组合ABC：结果内容\n1234567891 未知抗体XYZ：", "1、未知检验组合ABC：结果内容；2、未知抗体XYZ无明显异常。", "带数字时间的未预设项目保留原名和结果");
                Ready("202610080958 25羟维生素D：偏低", "1、25羟维生素D：偏低。", "有时间分隔符时保留数字开头的项目名");
                Ready("202610080958 TSH检测：偏低", "1、TSH检测：偏低。", "英文字母开头项目名不被时间串吞掉");
                Ready("202610080958 凝血：比值：0.25↑", "1、凝血：比值：0.25↑。", "结果内部的冒号和小数保持原文");
                Ready("202610080958 甲功：阴性\n202610080959 血糖：0\n202610081000 凝血：待报告", "1、甲功：阴性；2、血糖：0；3、凝血：待报告。", "非空的阴性、零值和待报告均保留，不自动判断正常");
                Ready("202610080958 甲功：—", "1、甲功：—。", "占位横线不是空结果，不自动汇总正常");
                Ready("202610080958 甲功：  ；\n202610080959 凝血：\t", "1、甲功、凝血无明显异常。", "仅分隔符与空白的结果按空冒号规则处理");
                Ready("\u00a0202610080958 血常规：白细胞↓\r202610080959 凝血：\u2028202610081000 甲功：", "1、血常规：白细胞↓；2、凝血、甲功无明显异常。", "兼容不换行空格、回车和Unicode行分隔符");
                Ready("202610080958 肝功能：\n202610080958 甲功：\n202610081000 凝血：", "1、202610080958 肝肾功血糖血脂心肌酶+HYC、甲功无明显异常；2、202610081000 凝血无明显异常。", "开启时间后正常项目按原时间分别汇总", new FormatOptions { IncludeTime = true });
                Ready("202610080958 甲功：\n202610080958 甲功：", "1、甲功无明显异常。", "正常汇总中的完全同名项目不重复堆叠");
                var conflict = Formatter.Parse("202610080958 甲功：\n202610081000 甲功：TSH↑", new FormatOptions());
                Check(!conflict.Ready && conflict.Output.Contains("待核对"), "同名项目有空和有字结果时不隐藏冲突");
                Ready("202610080958 甲功：\n202610081000 甲功：TSH↑", "1、202610081000 甲功：TSH↑；2、202610080958 甲功无明显异常。", "不同时间同名项目保留时间后可分别表达", new FormatOptions { IncludeTime = true });
                Check(!Formatter.Parse("202610080958 甲功：\n202610080958 甲功：TSH↑", new FormatOptions { IncludeTime = true }).Ready, "同一时间同一项目矛盾结果仍需核对");
                foreach (string bad in new[] { "", "202610080958 血常规", "只有一串无法识别的文字", "202610080958", "姓名：合成姓名" })
                {
                    var p = Formatter.Parse(bad, new FormatOptions()); Check(!p.Ready && p.Output.Contains("待核对"), "无法确认的输入不当成正常：" + (bad.Length == 0 ? "空输入" : bad));
                }
                var mixed = Formatter.Parse("202610080958 血常规：\n202610081000 肝功能 缺少冒号", new FormatOptions());
                Check(!mixed.Ready && mixed.Output.Contains("肝功能 缺少冒号") && mixed.Records.Count == 1, "部分可识别时仍保留未识别原文并阻止输出");
                var uncertain = Formatter.Parse("血常规：白细胞↓\n未知字段：", new FormatOptions());
                Check(!uncertain.Ready && uncertain.Output.Contains("未知字段"), "无时间的新未知字段与续行歧义会提示核对");
                Check(!Formatter.Parse("血常规：\n肝功能", new FormatOptions()).Ready, "缺少冒号的常见项目名不静默当作上一项结果");
                Check(!Formatter.Parse("备注：", new FormatOptions()).Ready, "空备注表头不会被认成无异常检验");
                Ready("202610080958\n血常规：白细胞↓\n\n202610081000\n肝功能：", "1、血常规：白细胞↓；2、肝肾功血糖血脂心肌酶+HYC无明显异常。", "时间独立成行且分段时能关联下行项目");
                Ready("血常规：白细胞↓\n\n未知检验XYZ：", "1、血常规：白细胞↓；2、未知检验XYZ无明显异常。", "空行可辅助确定无时间的未知项目段落");
                var invalidName = Formatter.Parse("202610080958 肝功能：", new FormatOptions { CombinedName = "" });
                Check(!invalidName.Ready, "空的自定义组合表头阻止直接输出");
                var lines = Enumerable.Range(1, 1000).Select(i => "202610080958 检验项目" + i + "：" + (i % 2 == 0 ? "合成结果↑" : ""));
                var large = Formatter.Parse(String.Join("\n", lines), new FormatOptions());
                Check(large.Ready && large.Records.Count == 1000 && large.ItemCount == 501 && !large.Output.Contains("\n") && !large.Output.Contains("\r"), "1000项目整批输入仍按一段编号输出，有字500项加正常汇总");
                string sampleOutput = Formatter.Parse(MainWindow.Sample, new FormatOptions()).Output;
                File.WriteAllText(Path.Combine(directory, "用户示例加模拟输入.txt"), MainWindow.Sample, new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(directory, "实际连续输出.txt"), sampleOutput, new UTF8Encoding(true));
                List<string> examples = new List<string> { "# 离线检验整理：测试样例与实际输出", "", "下列均为当前程序实际解析得到的结果。除第1例沿用用户提供的文本外，其余为合成测试，不是真实患者资料。", "", "规则：默认隐藏时间，保留原始项目名；仅肝功能替换为指定组合表头；空冒号项目汇总到最后；输出文本无换行。", "" };
                string[] exampleNames = { "用户给定例子", "混合项目与肝功能组合表头", "全部空结果", "结果换行及子指标冒号", "不同数字时间格式并保留时间", "未识别内容不自动判正常" };
                string[] exampleInputs = { "2026100809:58 血细胞五项：粒细胞↑，白细胞↓，", MainWindow.Sample, "202610080958 肝功能：\n202610080959 肾功能：\n202610081000 甲功：", "202610080958 血细胞五项：粒细胞↑\n白细胞↓\n血红蛋白：110↓\n202610081010 凝血：", "2026/10/8 9:58:32 血常规：白细胞↓\n123456789012345678 甲功：", "202610080958 甲功：\n202610081000 肝功能 缺少冒号" };
                for (int i = 0; i < exampleInputs.Length; i++)
                {
                    FormatResult example = Formatter.Parse(exampleInputs[i], new FormatOptions { IncludeTime = i == 4 });
                    examples.Add("## " + (i + 1) + "、" + exampleNames[i]); examples.Add(""); examples.Add("输入："); examples.Add(""); examples.Add("```text\n" + exampleInputs[i] + "\n```"); examples.Add(""); examples.Add("实际输出："); examples.Add(""); examples.Add(example.Output); examples.Add(""); examples.Add(example.Ready ? "状态：可核对后复制/导出。" : "状态：含待核对内容，复制与导出按钮禁用，需先修正输入。"); examples.Add("");
                }
                File.WriteAllLines(Path.Combine(directory, "测试样例与实际输出.md"), examples, new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(directory, "core-tests.txt"), checks.Concat(new[] { "TOTAL " + checks.Count + " core checks passed" }), new UTF8Encoding(true)); return 0;
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(directory, "core-tests.txt"), checks.Concat(new[] { ex.ToString() }), new UTF8Encoding(true)); return 1; }
        }
        public static void UI(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); checks.Clear();
            try
            {
                Check(!main.CanOutput() && !main.CopyButton.IsEnabled, "空窗口不允许复制或导出");
                main.Input.Text = MainWindow.Sample; main.ArrangeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(main.CanOutput() && main.Details.Items.Count == 5 && main.Output.Text.StartsWith("1、血细胞五项"), "整批粘贴和实际整理按钮生成五条识别明细");
                Check(!main.Output.Text.Contains("\n") && !main.Output.Text.Contains("\r") && main.Output.Text.EndsWith("3、肾功能、甲功、凝血无明显异常。"), "实际界面输出为连续编号一段且正常项目在最后");
                string file = Path.Combine(directory, "界面导出.txt"); Check(main.ExportTo(file) && File.ReadAllText(file) == main.Output.Text && File.ReadAllBytes(file).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "实际TXT导出与预览逐字一致并保留UTF8 BOM");
                Capture(main.Window, Path.Combine(directory, "01_混合检验离线整理.png"));
                main.Input.AppendText("\n202610081020 甲状腺功能：TSH↑");
                Check(!main.CanOutput() && !main.CopyButton.IsEnabled && !main.ExportTo(Path.Combine(directory, "过期不应导出.txt")), "输入变化后阻止复制和导出旧结果");
                main.Arrange(); main.IncludeTime.IsChecked = true;
                Check(!main.CanOutput(), "时间显示选项变化后需要重新整理"); main.Arrange(); Check(main.CanOutput() && main.Output.Text.Contains("2026100809:58"), "勾选保留时间后原数字时间进入正文");
                main.Input.Text = "202610080958 甲功：\n202610081000 肝功能 缺少冒号"; main.Arrange();
                Check(!main.CanOutput() && !main.CopyButton.IsEnabled && main.Output.Text.Contains("待核对"), "格式有问题时界面提示核对且复制按钮禁用");
                Capture(main.Window, Path.Combine(directory, "02_未识别内容需核对.png"));
                main.Input.Text = MainWindow.Sample; main.IncludeTime.IsChecked = false; main.Arrange(); main.Window.Width = 900; main.Window.Height = 700;
                Capture(main.Window, Path.Combine(directory, "03_小屏界面.png"));
                Check(main.ArrangeButton.IsVisible && main.Output.ActualHeight > 100 && main.Details.ActualHeight > 80, "900宽窗口保留输入输出、识别表及操作按钮");
                // Preserve the clipboard if it can be snapshotted. Do not treat a
                // busy desktop clipboard as proof of a successful copy.
                IDataObject backup = null; bool snapshot = false, changed = false;
                try
                {
                    IDataObject source = Clipboard.GetDataObject(); DataObject copy = new DataObject();
                    if (source != null) foreach (string format in source.GetFormats(false)) { object data = source.GetData(format, false); if (data != null) copy.SetData(format, data, false); }
                    backup = source == null ? null : copy; snapshot = true; changed = true;
                    if (main.Copy()) Check(Clipboard.GetText() == main.Output.Text, "真实系统剪贴板内容与连续输出逐字一致"); else checks.Add("SKIP 剪贴板占用，实际复制未验证");
                }
                catch (System.Runtime.InteropServices.ExternalException) { checks.Add("SKIP 无法访问系统剪贴板，实际复制未验证"); }
                finally { if (snapshot && changed) try { if (backup == null) Clipboard.Clear(); else Clipboard.SetDataObject(backup, true); } catch (System.Runtime.InteropServices.ExternalException) { checks.Add("SKIP 剪贴板占用，未能恢复测试前剪贴板"); } }
                File.WriteAllLines(Path.Combine(directory, "ui-tests.txt"), checks.Concat(new[] { "TOTAL " + checks.Count(c => c.StartsWith("PASS ")) + " UI checks passed" }), new UTF8Encoding(true));
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(directory, "ui-tests.txt"), checks.Concat(new[] { ex.ToString() }), new UTF8Encoding(true)); Environment.ExitCode = 1; }
            finally { main.Window.Close(); }
        }
        public static void Capture(Window window, string file)
        {
            window.Show(); window.UpdateLayout(); window.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle);
            FrameworkElement content = (FrameworkElement)window.Content; DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) { Rect rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight); dc.DrawRectangle(window.Background, null, rect); dc.DrawRectangle(new VisualBrush(content), null, rect); }
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (Stream s = File.Create(file)) png.Save(s);
        }
    }
}
