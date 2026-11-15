using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DischargeAssistant
{
    public static class Tests
    {
        private static List<string> results = new List<string>();
        private static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); results.Add("PASS " + message); }
        public static int Run(string[] args)
        {
            string dir = args.Length > 1 ? args[args.Length - 1] : Environment.CurrentDirectory;
            Directory.CreateDirectory(dir);
            try
            {
                Library lib = Program.ReadLibrary();
                Check(lib.Categories.Count == 40 && lib.Drugs.Count == 192, "完整药库：40分类、192条目");
                Check(lib.Drugs.Select(d => d.Id).Distinct().Count() == 192, "独立ID无重复");
                Check(lib.Drugs.Count(d => d.Warnings.Count > 0) == 38, "38个待核对条目保留");
                string text = lib.Generate(new[] { "d253", "d190", "d184", "d183", "d252" });
                Check(text.StartsWith(lib.Intro.Replace("\n", "\r\n")), "复诊说明始终在最前");
                Check(text.IndexOf("d183") < 0 && text.IndexOf("阿司匹林") < text.IndexOf("硫酸氢氯吡格雷"), "药物按原文顺序而非选择顺序");
                Check(text.IndexOf("1.抗血小板") < text.IndexOf("2.降脂") && text.IndexOf("2.降脂") < text.IndexOf("3.降同型"), "分类按原文顺序连续编号");
                Check(text.Contains("\r\n\r\n2.") && text.Contains("\r\n\r\n3."), "分类之间空一行");
                Check(text.Split(new[] { lib.Categories[0].Notes[0] }, StringSplitOptions.None).Length == 2, "共用备注只出现一次");
                Check(!text.Contains("瑞舒伐他汀") && !text.Contains("头晕症状改善"), "只输出选中药物与所属分类备注");
                string one = lib.Generate(new[] { "d368" });
                Check(one.Contains("不良反应：便秘") && !one.Contains("副作用：食欲下降") && !one.Contains("闭角型青光眼"), "帕罗西汀专属备注绑定准确");
                Check(lib.Generate(new[] { "d370" }).Contains("副作用：食欲下降"), "舍曲林专属备注绑定准确");
                Check(lib.Generate(new[] { "d372" }).Contains("闭角型青光眼"), "黛力新专属备注绑定准确");
                Check(lib.Generate(new[] { "d233", "d237" }).Contains("每日3次") && lib.Generate(new[] { "d233", "d237" }).Contains("每日2次"), "同名不同用法独立保留");
                Check(lib.Generate(new[] { "d256" }).Contains("0.1mg") && lib.Generate(new[] { "d286" }).Contains("盐酸刚片"), "疑似原文错误保留不擅自修改");
                Check(lib.Generate(new[] { "d397" }).Contains("解郁除烦胶囊"), "不完整原文条目可以保留生成");
                Drug aspirin = lib.Drugs.First(d => d.Id == "d183");
                Check(lib.Matches(aspirin, "阿司匹林"), "中文药名搜索");
                Check(lib.Matches(aspirin, "asipilin") && lib.Matches(aspirin, "ASPL"), "拼音全拼和首字母搜索不区分大小写");
                Check(lib.Matches(aspirin, "a si pi lin"), "带空格拼音搜索");
                Check(lib.Matches(aspirin, "阿司匹林 100mg") && !lib.Matches(aspirin, "不存在的药名"), "多关键词和无匹配搜索");
                Check(lib.Matches(lib.Drugs.First(d => d.Id == "d190"), "atf"), "其他分类可通过首字母检索");
                string path = Path.Combine(dir, "test-draft.xml");
                Draft draft = new Draft { Selected = new HashSet<string>(new[] { "d183", "d252" }), Preview = text + "\r\n手动编辑", Intro = "复诊前，自定义\r\n说明", CategoryId = "c251", FontSize = 20, Stale = true, Edited = true, GeneratedIds = "d183,d252" };
                draft.Save(path);
                Draft restored = Draft.Load(path);
                Check(restored.Selected.SetEquals(draft.Selected) && restored.Preview == draft.Preview, "选择与编辑文本草稿往返一致");
                Check(restored.FontSize == 20 && restored.Intro == draft.Intro && restored.Edited && restored.Stale, "字号、复诊说明及预览状态恢复");
                draft.Preview = "第二次保存"; draft.Save(path);
                Check(Draft.Load(path).Preview == "第二次保存" && Draft.Load(path + ".bak").Preview == restored.Preview, "原子保存与上一份草稿备份");
                File.WriteAllText(path, "broken"); bool broken = false;
                try { Draft.Load(path); } catch { broken = true; }
                Check(broken && Draft.Load(path + ".bak").Selected.SetEquals(draft.Selected), "损坏草稿可检测且备份有效");
                lib.Intro = restored.Intro;
                Check(!lib.Generate(new[] { "d183" }).Contains("\r\r\n"), "自定义复诊说明换行正确");
                string exported = Path.Combine(dir, "test-export.txt"); File.WriteAllText(exported, text, new UTF8Encoding(true));
                Check(File.ReadAllText(exported) == text && File.ReadAllBytes(exported)[0] == 0xEF, "UTF8 BOM导出保留中文和换行");
                Library customLib = Program.ReadLibrary();
                Category module = CustomLibrary.AddCategory(customLib, "自定义测试模块", "注：模块共用备注");
                string customFile = Path.Combine(dir, "custom-core.xml");
                CustomLibrary.Save(customLib, customFile);
                Library emptyModule = Program.ReadLibrary(); CustomLibrary.Merge(emptyModule, customFile);
                Check(emptyModule.Categories.Count == 41 && emptyModule.Categories.Last().Drugs.Count == 0, "空自定义模块保存并恢复");
                Drug customDrug = CustomLibrary.AddDrug(customLib, module, "测试药物甲", "测试正文\r\n第二行", "注：甲专属备注", "");
                Drug customDrug2 = CustomLibrary.AddDrug(customLib, module, "测试药物乙", "乙正文", "", "ce shi yao wu yi");
                Drug existingCategoryDrug = CustomLibrary.AddDrug(customLib, customLib.Categories[0], "新添药物", "新添正文", "", "");
                CustomLibrary.Save(customLib, customFile);
                Library roundtrip = Program.ReadLibrary(); CustomLibrary.Merge(roundtrip, customFile);
                Check(roundtrip.Categories.Count == 41 && roundtrip.Drugs.Count == 195 && roundtrip.Drugs.First(d => d.Id == customDrug.Id).Text == customDrug.Text, "自定义模块、原分类新增药物及多行正文保存恢复");
                Check(roundtrip.Matches(roundtrip.Drugs.First(d => d.Id == customDrug.Id), "ceshiyaowujia") && roundtrip.Matches(roundtrip.Drugs.First(d => d.Id == customDrug.Id), "csywj"), "自定义药物自动全拼与首字母搜索");
                string customText = roundtrip.Generate(new[] { customDrug2.Id, customDrug.Id, existingCategoryDrug.Id, "d183" });
                Check(customText.IndexOf("阿司匹林") < customText.IndexOf("新添药物") && customText.IndexOf("测试药物甲") < customText.IndexOf("测试药物乙"), "新药排在所属模块末尾、新模块排在原分类后");
                Check(customText.Split(new[] { "模块共用备注" }, StringSplitOptions.None).Length == 2 && customText.Contains("甲专属备注") && !roundtrip.Generate(new[] { customDrug2.Id }).Contains("甲专属备注"), "自定义共用备注去重且专属备注只随对应药物");
                bool rejected = false; try { CustomLibrary.AddCategory(roundtrip, "自定义测试模块", ""); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "拒绝重复模块名称");
                rejected = false; try { CustomLibrary.AddDrug(roundtrip, roundtrip.Categories[0], "", "", "", ""); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "拒绝缺失药名或用法");
                File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true));
                return 0;
            }
            catch (Exception ex) { results.Add(ex.ToString()); File.WriteAllLines(Path.Combine(dir, "core-tests.txt"), results, new UTF8Encoding(true)); return 1; }
        }
        public static void Preview(MainWindow w, string[] args)
        {
            string dir = args[args.Length - 1]; Directory.CreateDirectory(dir);
            results.Clear();
            string oldClipboardText = null;
            try { oldClipboardText = ReadClipboard(); } catch { }
            try
            {
                w.SetSelected("d183", true); w.SetSelected("d184", true); w.SetSelected("d190", true); w.SetSelected("d252", true); w.SetSelected("d253", true);
                w.Generate(false);
                Check(w.Preview.Text.Contains("3.降同型"), "界面生成完整多类别文本");
                for (int retry = 0; retry < 10; retry++) { w.Copy(); if (ReadClipboard() == w.Preview.Text) break; System.Threading.Thread.Sleep(80); }
                Check(ReadClipboard() == w.Preview.Text, "系统剪贴板复制完整正文");
                w.Search.Text = "aspl"; w.RefreshDrugs();
                Check(w.Find<StackPanel>("DrugList").Children.OfType<System.Windows.Controls.Border>().Count() == 1, "界面全拼搜索结果准确");
                Check(w.Selected.Count == 5, "搜索时跨类选择保留");
                w.Search.Clear();
                w.Find<ListBox>("CategoryList").SelectedIndex = 1;
                Check(w.Selected.Count == 5, "切换分类时选择保留");
                w.Preview.AppendText("\r\n人工补充内容");
                Check(w.Edited, "人工编辑状态正确");
                w.SetSelected("d189", true); Check(w.Stale && w.Preview.Text.Contains("人工补充内容"), "更改选择不覆盖手动编辑并提示需更新");
                w.SetSelected("d189", false); w.Generate(false);
                string originalIntro = w.Library.Intro; w.Library.Intro = "复诊前，已修改说明"; w.SetSelected("d189", true); w.SetSelected("d189", false);
                Check(w.Stale, "修改复诊说明后即使恢复原选择仍提示重新生成");
                w.Library.Intro = originalIntro; w.Generate(false);
                w.Find<ListBox>("CategoryList").SelectedIndex = 0; w.RefreshDrugs();
                Capture(w, dir, 1280, 820, "界面_宽窗口.png");
                Capture(w, dir, 920, 740, "界面_小窗口.png");
                Capture(w, dir, 720, 640, "界面_最小窗口.png");
                Check(w.Find<Border>("PreviewPanel").ActualHeight > 100 && Grid.GetRow(w.Find<Border>("PreviewPanel")) == 2, "小窗口预览自动置于下方");
                w.Window.Width = 1280; w.Window.Height = 820; w.Responsive(); w.Window.UpdateLayout();
                w.Find<Button>("ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture(w, dir, 1280, 820, "界面_待核对.png");
                Check(w.Find<TextBlock>("ResultInfo").Text.StartsWith("38 项"), "待核对视图显示全部38条");
                string isolated = Path.Combine(dir, "isolated-profile", "draft.xml");
                MainWindow saved = new MainWindow(true, isolated);
                saved.SetSelected("d183", true); saved.Library.Intro = "复诊前，自定义复诊说明"; saved.Generate(false); saved.Preview.AppendText("\r\n独立测试编辑"); saved.Preview.FontSize = 21; saved.SaveDraft();
                MainWindow recovered = new MainWindow(true, isolated);
                Check(recovered.Selected.SetEquals(saved.Selected) && recovered.Preview.Text == saved.Preview.Text && recovered.Library.Intro == saved.Library.Intro && recovered.Preview.FontSize == 21 && recovered.Edited, "窗口重新打开恢复选择、编辑、复诊说明与字号");
                saved.SaveDraft(); File.WriteAllText(isolated, "broken");
                MainWindow backup = new MainWindow(true, isolated);
                Check(backup.Preview.Text == saved.Preview.Text && backup.Find<TextBlock>("StatusText").Text.Contains("备份"), "窗口损坏草稿恢复上一份备份并显示提示");
                Check(w.Find<TextBlock>("StatusText").Text.Length > 0, "窗口操作状态可见");
                string customDraft = Path.Combine(dir, "custom-ui-" + Guid.NewGuid().ToString("N"), "draft.xml");
                MainWindow customWindow = new MainWindow(true, customDraft);
                Category customCategory = customWindow.AddCustomCategory("界面自定义模块", "注：模块备注");
                Drug uiDrug = customWindow.AddCustomDrug(customCategory, "界面测试药物", "按填写内容使用", "注：药物备注", "");
                customWindow.SetSelected(uiDrug.Id, true); customWindow.Generate(false); customWindow.SaveDraft();
                MainWindow customReopened = new MainWindow(true, customDraft);
                Check(customReopened.Library.Drugs.Any(d => d.Id == uiDrug.Id) && customReopened.Selected.Contains(uiDrug.Id) && customReopened.Preview.Text.Contains("界面测试药物"), "新增模块药物立即可选生成并在重启后恢复");
                customWindow.RefreshDrugs(); customWindow.Window.Show(); Capture(customWindow, dir, 1280, 820, "界面_自定义模块.png");
                Window dialog = customWindow.CreateCustomWindow(); dialog.Show(); dialog.UpdateLayout();
                RenderTargetBitmap dialogBitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); dialogBitmap.Render(dialog);
                PngBitmapEncoder dialogEncoder = new PngBitmapEncoder(); dialogEncoder.Frames.Add(BitmapFrame.Create(dialogBitmap)); using (FileStream f = File.Create(Path.Combine(dir, "界面_自定义添加.png"))) dialogEncoder.Save(f);
                dialog.Close(); customWindow.Window.Close();
                File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true));
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(dir, "ui-tests.txt"), results, new UTF8Encoding(true)); Environment.ExitCode = 1; }
            finally { if (oldClipboardText != null) try { Clipboard.SetText(oldClipboardText); } catch { } w.Window.Close(); }
        }
        private static string ReadClipboard()
        {
            for (int retry = 0; retry < 20; retry++) try { return Clipboard.GetText(); } catch { System.Threading.Thread.Sleep(50); }
            throw new Exception("测试剪贴板仍被占用");
        }
        private static void Capture(MainWindow w, string dir, int width, int height, string file)
        {
            w.Window.Width = width; w.Window.Height = height; w.Responsive(); w.Window.UpdateLayout();
            w.Window.UpdateLayout();
            FrameworkElement content = (FrameworkElement)w.Window.Content;
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right), (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(w.Window); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(Path.Combine(dir, file))) encoder.Save(stream);
        }
    }
}
