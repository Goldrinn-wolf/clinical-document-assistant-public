using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SuiteCustomization;

namespace ProgressNotesAssistant
{
    public static class ExamCategory
    {
        public static readonly string[] Ids = { "basic", "supplemental", "special", "other" };
        public static string Of(ExamDefinition exam) { return Ids.Contains(exam.Category) ? exam.Category : "other"; }
        public static string TabTitle(string category)
        {
            switch (category)
            {
                case "basic": return "1  基础血常规";
                case "supplemental": return "2  补充其他检验";
                case "special": return "3  特殊抗体";
                default: return "4  其他检查";
            }
        }
        public static string Title(string category) { return category == "other" ? "其他检查（不能直接复制的检查）" : TabTitle(category).Substring(3).Trim(); }
        public static string Hint(string category)
        {
            switch (category)
            {
                case "basic": return "血细胞、血糖、血脂、肝肾功能、心肌酶及同型半胱氨酸，共7项；本例首次进入默认全选，保存后保留手动选择。";
                case "supplemental": return "凝血、甲功等原有补充检验，按需勾选。原组合检验保留在此，便于继续编辑旧草稿。";
                case "special": return "血沉、风湿系列及相关抗体等7项；本例首次进入默认全选，保存后保留手动选择。";
                default: return "原有量表、心电图、超声和功能检查等放在这里；可手动录入结果，原参考须点击后载入。自定义检查也在此。";
            }
        }
    }

    public static class CategoryTests
    {
        public static void Run(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); TestSupport.Results.Clear(); MainWindow restored = null; ExamDialog dialog = null;
            try
            {
                main.NewPatient(false);
                var basic = main.Library.Exams.Where(e => e.Category == "basic").ToList();
                var special = main.Library.Exams.Where(e => e.Category == "special").ToList();
                TestSupport.Check(basic.Count == 7 && basic.All(e => e.DefaultSelected) && basic.Any(e => e.Id == "hcy"), "基础类含图片第一列6项与同型半胱氨酸共7项");
                TestSupport.Check(special.Count == 7 && special.All(e => e.DefaultSelected) && special.Any(e => e.Id == "esr") && special.Any(e => e.Id == "lupus-anticoagulant"), "特殊类含血沉至狼疮抗凝物检测共7项");
                TestSupport.Check(new[] { "coagulation", "thyroid", "genetic", "lab-main" }.All(id => main.Library.GetExam(id).Category == "supplemental"), "原凝血甲功基因检验及旧组合检验在补充类");
                TestSupport.Check(new[] { "scl90", "mmse", "moca", "echo", "holter", "evoked" }.All(id => main.Library.GetExam(id).Category == "other"), "原量表超声心电与功能检查在其他类");
                dialog = main.CreateExamsDialog();
                TestSupport.Check(dialog.Categories.Items.Count == 4 && dialog.CurrentCategory == "basic", "辅助检查直接显示四个分类并进入基础类");
                TestSupport.Check(basic.All(e => dialog.Rows[e.Id].Selected.IsChecked == true) && special.All(e => dialog.Rows[e.Id].Selected.IsChecked == false), "基础类进入即全选，未进入的特殊类不自动纳入");
                TestSupport.Check(main.State.Exams.Values.All(e => !e.Selected) && main.State.ExamCategories.Count == 0, "弹窗默认勾选尚未写入患者草稿");
                dialog.SelectCategory("special");
                TestSupport.Check(special.All(e => dialog.Rows[e.Id].Selected.IsChecked == true), "点击特殊类首次进入全部默认勾选");
                dialog.Rows["esr"].Selected.IsChecked = false; dialog.SelectCategory("basic"); dialog.SelectCategory("special");
                TestSupport.Check(dialog.Rows["esr"].Selected.IsChecked == false, "切换分类不会重新勾选手动取消项");
                dialog.Cancel(); dialog = null;
                TestSupport.Check(main.State.Exams.Values.All(e => !e.Selected) && main.State.ExamCategories.Count == 0, "取消弹窗不保存默认勾选或分类进入状态");
                dialog = main.CreateExamsDialog(); dialog.Save(); dialog = null;
                TestSupport.Check(basic.All(e => main.State.Exam(e.Id).Selected) && special.All(e => !main.State.Exam(e.Id).Selected) && !main.State.ExamCategories.Contains("special"), "只保存基础类时未进入的特殊类不纳入输出");
                main.Save(); restored = new MainWindow(main.Profile); restored.Window.Show(); dialog = restored.CreateExamsDialog(); dialog.SelectCategory("special");
                TestSupport.Check(special.All(e => dialog.Rows[e.Id].Selected.IsChecked == true), "保存并重启后首次点击未进入的特殊类仍默认全选");
                dialog.Cancel(); dialog = null; restored.Window.Close(); restored = null; main.NewPatient(false);
                dialog = main.CreateExamsDialog();
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "00_四类界面.png"));
                dialog.Rows["glucose"].Selected.IsChecked = false;
                dialog.Rows["glucose"].Result.Text = "未纳入的血糖结果（合成测试）";
                dialog.Rows["cbc"].Result.Text = "白细胞结果（合成测试）\r\n第二行<&>";
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "01_基础血常规.png"));
                dialog.SelectCategory("supplemental");
                TestSupport.Check(main.Library.Exams.Where(e => e.Category == "supplemental").All(e => dialog.Rows[e.Id].Selected.IsChecked == false), "补充类进入仍按需勾选");
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "02_补充其他检验.png"));
                dialog.Rows["coagulation"].Selected.IsChecked = true;
                dialog.SelectCategory("special");
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "00_特殊抗体默认全选.png")); dialog.Rows["esr"].Selected.IsChecked = false;
                dialog.Rows["ana"].Result.Text = "抗体结果（合成测试）";
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "03_特殊抗体.png"));
                dialog.SelectCategory("other");
                TestSupport.Check(main.Library.Exams.Where(e => e.Category == "other").All(e => dialog.Rows[e.Id].Selected.IsChecked == false), "其他检查进入不自动勾选或填入参考");
                dialog.Search.Text = "mmse";
                dialog.Search.Text = "蒙特利尔";
                TestSupport.Check(dialog.Rows["moca"].Panel.Visibility == Visibility.Visible && dialog.Rows["echo"].Panel.Visibility == Visibility.Collapsed, "分类内搜索显示匹配项目并隐藏无关项目");
                dialog.Search.Clear();
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "04_其他检查.png"));
                dialog.Search.Text = "简易智力";
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "05_量表搜索.png")); dialog.Search.Clear();
                dialog.Rows["moca"].Selected.IsChecked = true; dialog.Rows["moca"].Result.Text = "总分（合成测试）";
                dialog.Window.Width = 860; dialog.Window.Height = 650; dialog.SelectCategory("special");
                TestSupport.Capture(dialog.Window, Path.Combine(directory, "06_小屏分类.png"));
                TestSupport.Check(dialog.Categories.ActualWidth > 600 && TestSupport.Elements<Button>(dialog.Window).Any(b => Convert.ToString(b.Content) == "保存检查" && b.IsVisible && b.ActualHeight > 20), "860宽窗口保留四类与保存操作");
                dialog.Save(); dialog = null;
                main.Generate(); string generated = Generator.Auxiliary(main.Library, main.State);
                TestSupport.Check(generated.Contains("血细胞分析+五分类：白细胞结果（合成测试）\r\n第二行<&>") && generated.Contains("抗核抗体谱（流式荧光定量）：抗体结果（合成测试）") && generated.Contains("蒙特利尔认知评估量表：总分（合成测试）"), "三类结果保留完整表头、多行、中文和符号");
                TestSupport.Check(!generated.Contains("未纳入的血糖结果") && !generated.Contains("血沉") && generated.Contains("凝血") && generated.Contains("无明显异常。"), "撤勾项目不输出，勾选空白项目继续正常汇总");
                TestSupport.Check(main.State.ExamCategories.SetEquals(ExamCategory.Ids), "保存记录本例已进入的四类");
                if (main.ExamsOnly) TestSupport.Check(main.ExportTo(Path.Combine(directory, "四类辅助检查导出.txt")) && File.ReadAllText(Path.Combine(directory, "四类辅助检查导出.txt")) == main.Preview.Text, "独立工具导出四类生成正文与预览逐字相同");
                main.Save(); restored = new MainWindow(main.Profile); restored.Window.Show(); dialog = restored.CreateExamsDialog(); dialog.SelectCategory("special");
                TestSupport.Check(dialog.Rows["esr"].Selected.IsChecked == false && dialog.Rows["glucose"].Selected.IsChecked == false && dialog.Rows["glucose"].Result.Text.Contains("未纳入"), "重启后保留取消勾选和暂存结果");
                dialog.Cancel(); dialog = null; restored.Window.Close(); restored = null;
                dialog = main.CreateExamsDialog(); dialog.SelectAll("basic", false); dialog.Save(); dialog = main.CreateExamsDialog();
                TestSupport.Check(basic.All(e => dialog.Rows[e.Id].Selected.IsChecked == false), "本类全不选保存后重开仍不勾选");
                dialog.Cancel(); dialog = null;
                // Load a real old-format draft with no category metadata.
                string oldPath = Path.Combine(directory, "legacy-draft.xml");
                File.WriteAllText(oldPath, "<Draft><CurrentModule>day2</CurrentModule><Exam id='lab-main' selected='true'><Result>旧版组合检验结果</Result><Treatment>原有手动处理</Treatment></Exam><Exam id='mmse' selected='false'><Result>旧版暂存量表</Result></Exam></Draft>");
                main.State = State.Load(oldPath); dialog = main.CreateExamsDialog(); dialog.SelectCategory("other"); dialog.Save(); dialog = null;
                TestSupport.Check(main.State.Exam("lab-main").Selected && main.State.Exam("lab-main").Result == "旧版组合检验结果" && main.State.Exam("lab-main").Treatment == "原有手动处理" && !main.State.Exam("mmse").Selected && main.State.Exam("mmse").Result == "旧版暂存量表", "旧版草稿升级保留组合结果、处理及未选量表正文");
                main.SaveCustomItem(new Item { Kind = "exam", Title = "自定义检查合成测试", Text = "手动参考合成测试" });
                string customId = main.CustomItems.Single().Id; dialog = main.CreateExamsDialog(); dialog.SelectCategory("other");
                TestSupport.Check(ExamCategory.Of(main.Library.GetExam(customId)) == "other" && dialog.Rows[customId].Selected.IsChecked == false && dialog.Rows[customId].Result.Text == "", "自定义检查归入其他类并保留手动参考规则");
                dialog.Cancel(); dialog = null; main.NewPatient(false); dialog = main.CreateExamsDialog();
                TestSupport.Check(basic.All(e => dialog.Rows[e.Id].Selected.IsChecked == true && dialog.Rows[e.Id].Result.Text == "") && special.All(e => dialog.Rows[e.Id].Selected.IsChecked == false) && main.State.Exam("lab-main").Result == "" && main.CustomItems.Count == 1, "新患者清空结果与分类状态，基础类重新默认全选并保留自定义库");
                dialog.SelectCategory("special"); TestSupport.Check(special.All(e => dialog.Rows[e.Id].Selected.IsChecked == true), "新患者首次进入特殊类重新默认全选");
                dialog.Cancel(); dialog = null;
                File.WriteAllLines(Path.Combine(directory, "category-tests.txt"), TestSupport.Results.Concat(new[] { "TOTAL " + TestSupport.Results.Count + " category checks passed" }));
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(directory, "category-tests.txt"), TestSupport.Results.Concat(new[] { ex.ToString() })); Environment.ExitCode = 1; }
            finally { if (dialog != null) dialog.Cancel(); if (restored != null) restored.Window.Close(); main.Window.Close(); }
        }
    }
}
