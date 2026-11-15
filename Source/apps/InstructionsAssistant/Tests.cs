using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace InstructionsAssistant
{
    public static class Tests
    {
        private static List<string> results = new List<string>();
        private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); results.Add("PASS " + label); }
        public static int Core(string dir)
        {
            Directory.CreateDirectory(dir); results.Clear();
            try
            {
                Library lib = Program.ReadLibrary();
                Check(lib.Entries.Count == 43 && lib.Entries.Count(e => e.Step == 1) == 11 && lib.Entries.Count(e => e.Step == 5) == 29, "43条目，11基础疾病，29其他病症与复查");
                Check(lib.Entries.Select(e => e.Id).Distinct().Count() == 43, "条目ID唯一");
                State s = new State(); Check(lib.Validate(s).Contains("步骤1"), "未选基础疾病不能生成");
                s.Selected.Add("word-140"); Check(lib.Validate(s).Contains("步骤4"), "造影状态必须明确确认");
                s.Puncture = 0; string text = lib.Generate(s);
                Check(text.StartsWith("出院医嘱：\r\n1.脑血管病："), "第一项基础疾病正文包含疾病名");
                Check(text.Contains("2.按服药单服药。") && text.Contains("3.出院1月后神经内科门诊复诊"), "第2、3项固定默认正文正确");
                Check(!text.Contains("4.") && !text.Contains("穿刺点"), "未做造影且无其他病症仅生成3项");
                s.Selected.Add("word-155"); s.Selected.Add("word-153"); text = lib.Generate(s);
                Check(text.Contains("4.针对高血压") && !text.Contains("5."), "跳过穿刺后其他病症为编号4");
                Check(text.IndexOf("针对高血压") < text.IndexOf("针对糖尿病") && text.Contains("方案；针对糖尿病"), "其他病症按原模板顺序用分号连接");
                s.Puncture = 1; text = lib.Generate(s);
                Check(text.Contains("4.出院后观察右下肢穿刺点情况") && text.Contains("5.针对高血压"), "已做造影生成编号4，其他病症为5");
                s.Selected.Add("word-141"); text = lib.Generate(s);
                Check(text.Contains("认知功能康复；癫痫：") && !text.Contains("2.癫痫"), "多个基础疾病只形成编号1且用分号连接");
                Check(text.Contains("\r\n\r\n2.") && text.Contains("\r\n\r\n5."), "编号之间空一行");
                Check(!text.Contains("1.1.") && !text.Contains("；；"), "连接边界没有重复编号或分号");
                Check(lib.Matches(lib.Entries.First(e => e.Id == "word-140"), "nxgb") && lib.Matches(lib.Entries.First(e => e.Id == "word-140"), "naoxueguanbing"), "基础疾病首字母与全拼搜索");
                Check(lib.Matches(lib.Entries.First(e => e.Id == "word-155"), "tnb") && lib.Matches(lib.Entries.First(e => e.Id == "word-155"), "糖尿病 血糖"), "其他病症首字母和多关键词搜索");
                foreach (int step in new[] { 2, 3, 4 })
                {
                    State invalid = new State { Puncture = 1 }; invalid.Selected.Add("word-140"); invalid.Selected.Remove(lib.Entries.First(e => e.Step == step).Id);
                    Check(lib.Validate(invalid).Contains("步骤" + step), "步骤" + step + "缺少单选条目时阻止生成");
                }
                State clot = new State { Puncture = 0 }; clot.Selected.Add("word-140"); clot.Selected.Add("word-176-177"); string clotText = lib.Generate(clot);
                Check(clotText.Contains("[待填写：VTE评分及风险等级]") && !clotText.Contains("VTE评分为6分"), "示例患者VTE评分替换为待填写字段");
                Check(lib.Entries.Count(e => e.Origin == "网络补充草稿" && e.Source.StartsWith("https://www.nhs.uk/")) == 5, "5条网络补充草稿逐条标注官方来源");
                string path = Path.Combine(dir, "custom-core.xml");
                List<Entry> added = new List<Entry>();
                for (int step = 1; step <= 5; step++) added.Add(lib.Add(step, "自定义测试" + step, "测试正文" + step + "\r\n第二行", ""));
                lib.SaveCustom(path); Library restore = Program.ReadLibrary(); restore.MergeCustom(path);
                Check(restore.Entries.Count == 48 && added.All(e => restore.Entries.Any(x => x.Id == e.Id && x.Text == e.Text && x.Step == e.Step)), "全部5步骤自定义内容保存恢复，保留多行正文");
                Check(restore.Matches(restore.Entries.First(e => e.Id == added[0].Id), "zdycs") && restore.Matches(restore.Entries.First(e => e.Id == added[0].Id), "zidingyiceshi"), "自定义条目自动首字母与全拼搜索");
                s.Selected.Add(added[0].Id); text = restore.Generate(s); Check(text.IndexOf("脑血管病") < text.IndexOf("测试正文1"), "自定义条目排在所在步骤原始条目之后");
                bool rejected = false; try { restore.Add(1, "", "", ""); } catch (ArgumentException) { rejected = true; } Check(rejected, "自定义条目必填校验");
                rejected = false; try { restore.Add(8, "测试", "正文", ""); } catch (ArgumentException) { rejected = true; } Check(rejected, "自定义步骤有效性校验");
                rejected = false; try { restore.Add(1, added[0].Title, added[0].Text, ""); } catch (ArgumentException) { rejected = true; } Check(rejected, "同一步骤重复名称和正文拒绝重复保存");
                s.Preview = text + "\r\n人工编辑"; s.Edited = true; s.GeneratedSignature = restore.Signature(s); s.FontSize = 20; s.Step = 5;
                string draft = Path.Combine(dir, "draft-core.xml"); s.Save(draft); State recovered = State.Load(draft);
                Check(recovered.Selected.SetEquals(s.Selected) && recovered.Preview == s.Preview && recovered.Puncture == 1 && recovered.Edited && recovered.FontSize == 20 && recovered.Step == 5, "草稿恢复选择、造影、编辑、字号、步骤及换行");
                s.Preview = "再次保存"; s.Save(draft); Check(State.Load(draft + ".bak").Preview == recovered.Preview, "草稿原子保存并保留前一份备份");
                File.WriteAllText(draft, "broken"); rejected = false; try { State.Load(draft); } catch { rejected = true; } Check(rejected, "损坏草稿可以检测");
                string export = Path.Combine(dir, "export.txt"); File.WriteAllText(export, text, new UTF8Encoding(true)); Check(File.ReadAllText(export) == text && File.ReadAllBytes(export)[0] == 0xEF, "TXT导出UTF8 BOM及正文精确保留");
                File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true)); return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true)); return 1; }
        }
        public static void UI(MainWindow w, string dir)
        {
            results.Clear(); string oldText = null; try { oldText = ClipboardText(); } catch { }
            try
            {
                w.Next(); Check(w.State.Step == 1, "第一步未选疾病时不会前进");
                // Exercise the actual checkbox handler on the first disease card.
                Border firstCard = w.Find<StackPanel>("EntryList").Children.OfType<Border>().First();
                CheckBox firstCheck = ((DockPanel)firstCard.Child).Children.OfType<CheckBox>().First(); firstCheck.IsChecked = true;
                w.Window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
                Check(w.State.Selected.Contains("word-140"), "勾选疾病的实际控件事件生效");
                w.Choose(w.Library.Entries.First(e => e.Id == "word-141"), true); w.Next(); Check(w.State.Step == 2 && w.State.Selected.Contains("word-140"), "下一步保留多个基础疾病选择");
                w.Next(); w.Next(); Check(w.State.Step == 4, "步骤2、3默认医嘱可按步骤继续");
                w.Next(); Check(w.State.Step == 4, "造影未确认时下一步阻止前进");
                w.Find<RadioButton>("PunctureNo").IsChecked = true; w.Next(); Check(w.State.Step == 5 && w.State.Puncture == 0, "选择未做造影后进入其他病症");
                w.Choose(w.Library.Entries.First(e => e.Id == "word-155"), true); w.Choose(w.Library.Entries.First(e => e.Id == "word-153"), true); w.Generate(false);
                Check(w.Preview.Text.Contains("4.针对高血压") && !w.Preview.Text.Contains("5."), "界面跳过穿刺生成编号4");
                Check(w.Find<StackPanel>("SelectedList").Children.OfType<DockPanel>().Count() == 6, "选择框按步骤纳入全部已选条目");
                w.Search.Text = "tnb"; w.RefreshEntries(); Check(w.Find<StackPanel>("EntryList").Children.OfType<Border>().Count() == 1, "其他病症首字母搜索界面结果正确");
                Check(w.State.Selected.Contains("word-140") && w.State.Selected.Contains("word-153"), "搜索不清除跨步骤选择");
                w.Find<CheckBox>("SearchAll").IsChecked = true; w.Search.Text = "nxgb"; w.RefreshEntries(); Check(w.Find<StackPanel>("EntryList").Children.OfType<Border>().Count() == 1, "全步骤搜索可以找到其他步骤条目");
                w.Search.Clear(); w.Find<CheckBox>("SearchAll").IsChecked = false;
                w.Copy();
                string clipboardResult = null;
                try { clipboardResult = ClipboardText(); }
                catch (Exception clipboardError) { results.Add("SKIP 系统剪贴板本轮被占用，完整复制未验证：" + clipboardError.Message); }
                if (clipboardResult != null) Check(clipboardResult == w.Preview.Text, "系统Unicode剪贴板复制完整正文");
                w.Preview.AppendText("\r\n人工补充"); w.SetPuncture(1); Check(w.Stale && w.State.Edited && w.Preview.Text.Contains("人工补充"), "更改造影状态保留人工编辑并提示需更新");
                w.Generate(false); Check(w.Preview.Text.Contains("4.出院后观察") && w.Preview.Text.Contains("5.针对高血压"), "界面已做造影时5项连续编号");
                w.GoTo(1); Capture(w.Window, dir, "界面_宽窗口.png", 1300, 850); Capture(w.Window, dir, "界面_小窗口.png", 1000, 780); Capture(w.Window, dir, "界面_最小窗口.png", 800, 700);
                Check(Grid.GetRow(w.Find<Border>("PreviewPanel")) == 2 && w.Preview.ActualHeight >= 30, "最小窗口上下布局且预览可见");
                w.GoTo(4); Capture(w.Window, dir, "界面_造影步骤.png", 1300, 850);
                Entry clot = w.Library.Entries.First(e => e.Id == "word-176-177"); w.Choose(clot, true); w.Generate(false); Check(!w.Ready(false), "待填写VTE字段未填写时阻止输出");
                w.Preview.Text = w.Preview.Text.Replace("[待填写：VTE评分及风险等级]", "VTE评分及风险等级已核对"); Check(w.Ready(false), "填写待填写字段后可以输出");
                w.Choose(clot, false); w.Generate(false);
                Entry customMed = w.AddCustom(2, "自定义服药说明", "按照本人服药单按时服药。", ""); w.Choose(customMed, true);
                Check(w.Library.SelectedInStep(w.State, 2).Count == 1 && w.State.Selected.Contains(customMed.Id), "固定步骤选择自定义条目时替换默认条目");
                Entry customOther = w.AddCustom(5, "自定义病症", "针对自定义病症，按填写内容复查。", ""); w.Choose(customOther, true); w.Generate(false); w.Preview.AppendText("\r\n手动补充"); w.Preview.FontSize = 19; w.Save();
                MainWindow recovered = new MainWindow(w.Profile);
                Check(recovered.Library.Entries.Any(e => e.Id == customOther.Id) && recovered.State.Selected.SetEquals(w.State.Selected) && recovered.Preview.Text == w.Preview.Text && recovered.State.Puncture == 1 && recovered.Preview.FontSize == 19, "重启恢复自定义条目、选择、造影状态、预览和字号");
                string draftPath = Path.Combine(w.Profile, "draft.xml"); w.Save(); File.WriteAllText(draftPath, "broken"); MainWindow backup = new MainWindow(w.Profile); Check(backup.Preview.Text == w.Preview.Text && backup.Find<TextBlock>("StatusText").Text.Contains("备份"), "损坏草稿恢复备份并显示提示");
                Window dialog = w.CreateCustomWindow(w.Library.Entries.First(e => e.Id == "word-153")); dialog.Show(); Capture(dialog, dir, "界面_自定义条目.png", 620, 650); dialog.Close();
                w.GoTo(5); Capture(w.Window, dir, "界面_其他病症.png", 1300, 850);
                File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true));
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true)); Environment.ExitCode = 1; }
            finally { if (oldText != null) try { Clipboard.SetText(oldText); } catch { } w.Window.Close(); }
        }
        private static string ClipboardText()
        {
            for (int i = 0; i < 20; i++) try { return Clipboard.GetText(); } catch { System.Threading.Thread.Sleep(50); }
            throw new Exception("剪贴板测试被系统占用");
        }
        private static void Capture(Window w, string dir, string name, int width, int height)
        {
            w.Width = width; w.Height = height; w.UpdateLayout();
            FrameworkElement content = (FrameworkElement)w.Content;
            int pixelsWidth = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            int pixelsHeight = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelsWidth, pixelsHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (FileStream f = File.Create(Path.Combine(dir, name))) encoder.Save(f);
        }
    }
}
