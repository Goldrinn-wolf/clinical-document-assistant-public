using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SuiteCustomization;

namespace FirstCourseAssistant
{
    public static class PlanComposer
    {
        public static readonly string[] Intravenous = { "plan-gastrodin", "plan-danhong", "plan-qingkailing" };
        public const string LegacyCommon = "完善血常规、生化、凝血、头颈部血管检查及头颅影像学等检查明确病因，予抗血小板聚集、调脂固斑、调控血压血糖、营养神经、活血、改善循环、止晕、改善睡眠、调节情绪等对症支持治疗。严密观察意识、瞳孔、神经系统体征变化，警惕病情进展及并发症。加强护理、预防卧床相关并发症，适时康复治疗，充分告知家属病情，依据结果调整后续方案。";
        public static string FlagKey(string id) { return "PlanOption:" + id; }
        public static string TextKey(string id) { return "PlanOptionText:" + id; }
        public static string OptionText(Template option, State state) { return state.Fields.ContainsKey(TextKey(option.Id)) ? state.Get(TextKey(option.Id)) : option.Text; }
        public static bool Migrate(Library library, State state)
        {
            if (state.Get("PlanTemplateId").Length == 0 && state.Get("PlanBody").Trim() == LegacyCommon)
            {
                state.Set("PlanBody", library.Get("plan-common").Text); state.Set("PlanTemplateId", "plan-common"); state.Signature = ""; return true;
            }
            return false;
        }
        public static string Additions(Library library, State state)
        {
            List<string> groups = new List<string>();
            foreach (bool intravenous in new[] { false, true })
            {
                var selected = library.Templates.Where(t => t.Kind == "plan-option" && Intravenous.Contains(t.Id) == intravenous && state.Flag(FlagKey(t.Id))).OrderBy(t => t.Order).ToList();
                if (selected.Count == 0) continue;
                var lines = selected.Select(t => String.IsNullOrWhiteSpace(OptionText(t, state)) ? "[待补充：" + t.Title + "处理文字]" : Generator.Normalize(OptionText(t, state).Trim()));
                groups.Add((intravenous ? "可选静脉治疗：" : "合并症及伴随症状处理：") + "\r\n" + String.Join("\r\n", lines));
            }
            if (state.Get("PlanTemplateId") == "plan-hemorrhage" && state.Flag(FlagKey("plan-danhong"))) groups.Add("[待核对：脑出血模板与丹红选择冲突，请取消丹红并核对实际方案]");
            return String.Join("\r\n", groups);
        }
    }

    public partial class MainWindow
    {
        private void RenderPlanOptions()
        {
            Label(form, "合并症 / 伴随症状（按本例勾选加入）");
            RenderOptionGroup(false);
            Label(form, "常用静脉用药（按本例医嘱选择）");
            TextBlock hint = new TextBlock { Text = "以下均不默认勾选；剂量、溶媒、配伍与疗程按本院品种说明书及实际医嘱确定。", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#728994"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }; form.Children.Add(hint);
            RenderOptionGroup(true);
            if (State.Get("PlanTemplateId") == "plan-hemorrhage" && State.Flag(PlanComposer.FlagKey("plan-danhong"))) Label(form, "待核对：脑出血模板与丹红选择冲突，请取消丹红并核对实际方案。");
            foreach (Template option in Library.Templates.Where(t => t.Kind == "plan-option" && State.Flag(PlanComposer.FlagKey(t.Id))).OrderBy(t => t.Order))
            {
                TextBox box = Input(form, PlanComposer.TextKey(option.Id), option.Title + " · 本例处理文字（可修改）", 78); box.Text = PlanComposer.OptionText(option, State);
            }
        }
        private void RenderOptionGroup(bool intravenous)
        {
            var options = Library.Templates.Where(t => t.Kind == "plan-option" && PlanComposer.Intravenous.Contains(t.Id) == intravenous).OrderBy(t => t.Order).ToList();
            WrapPanel checks = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) }; form.Children.Add(checks);
            foreach (Template option in options)
            {
                CheckBox check = Flag(checks, PlanComposer.FlagKey(option.Id), option.Title, true); check.Margin = new Thickness(0, 5, 18, 5);
                check.Content = new TextBlock { Text = option.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 350 };
                if (option.Id == "plan-qingkailing") check.ToolTip = "按说明书适应证及辨证选择，关注过敏性休克及禁忌；不设置默认剂量或长期疗程。";
                if (option.Id == "plan-danhong") check.ToolTip = "有出血倾向者禁用；结合适应证、辨证及抗栓用药评估。";
            }
        }
    }

    public static class PlanTests
    {
        private static void Flush(MainWindow main) { main.Window.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle); }
        private static void Select(MainWindow main, string id)
        {
            ComboBox choice = (ComboBox)main.Fields["PlanPreset"];
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().First(i => ((Template)i.Tag).Id == id); Flush(main);
        }
        public static void Run(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); TestSupport.Results.Clear(); MainWindow restored = null;
            try
            {
                main.NewPatient(false); main.State.Set("VteMode", "skip"); main.GoTo(3); Flush(main);
                TestSupport.Check(main.Library.Templates.Count(t => t.Kind == "plan") == 13 && main.Library.Templates.Count(t => t.Kind == "plan-option") == 7, "13类计划与7项可选处理完整载入");
                string plain = Generator.Plan(main.Library, main.State);
                TestSupport.Check(!plain.Contains("降糖") && !plain.Contains("降压") && !plain.Contains("天麻素") && !plain.Contains("丹红") && !plain.Contains("清开灵"), "新患者默认计划不加入降压降糖或三种静脉用药");
                Select(main, "plan-dizziness"); plain = Generator.Plan(main.Library, main.State);
                TestSupport.Check(plain.Contains("发作时间") && plain.Contains("MRI") && plain.Contains("短期止晕") && plain.Contains("防跌倒"), "头晕模板包含分型评估、按指征检查、对症处理及护理");
                TestSupport.Check(!plain.Contains("降糖") && !plain.Contains("降压") && !plain.Contains("丹红"), "头晕基础计划无自动降压降糖或输液药物组合");
                TestSupport.Capture(main.Window, Path.Combine(directory, "01_头晕计划与勾选.png"));
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-htn")]).IsChecked = true; Flush(main);
                string htn = Generator.Plan(main.Library, main.State);
                TestSupport.Check(htn.Contains("降压治疗") && !htn.Contains("降糖治疗"), "单独勾选高血压只增加血压管理");
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-dm")]).IsChecked = true; Flush(main);
                TestSupport.Check(Generator.Plan(main.Library, main.State).Contains("降糖治疗") && Generator.Plan(main.Library, main.State).Contains("预防低血糖"), "勾选糖尿病增加血糖监测和个体化降糖文字");
                ((TextBox)main.Fields[PlanComposer.TextKey("plan-dm")]).Text = "本例降糖处理合成测试\r\n第二行";
                TestSupport.Check(Generator.Plan(main.Library, main.State).Contains("本例降糖处理合成测试\r\n第二行"), "勾选项本例文字可编辑并保留换行");
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-dm")]).IsChecked = false; Flush(main);
                TestSupport.Check(!Generator.Plan(main.Library, main.State).Contains("本例降糖处理"), "取消糖尿病勾选立即停止输出其文字");
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-dm")]).IsChecked = true; Flush(main);
                TestSupport.Check(((TextBox)main.Fields[PlanComposer.TextKey("plan-dm")]).Text.Contains("本例降糖处理"), "同患者重新勾选恢复本例编辑");
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-gastrodin")]).IsChecked = true; Flush(main);
                plain = Generator.Plan(main.Library, main.State);
                TestSupport.Check(plain.Contains("天麻素") && !plain.Contains("丹红") && !plain.Contains("清开灵") && !plain.Contains("ml"), "单独选择天麻素不带入其他药物或固定液体剂量");
                main.State.Set("Age", "中年"); main.State.Set("Gender", "女性"); main.State.Set("Onset", "急性起病"); main.State.Set("Chief", "头晕（合成测试）"); main.State.Set("CompanionOn", "false"); main.State.Set("RiskOn", "false"); main.State.Set("ExamSummary", "查体（合成测试）"); main.State.Set("Tests", "检查（合成测试）"); main.State.Selected.Add("ddx-14"); main.Generate(false);
                TestSupport.Check(!Generator.Pending(main.Preview.Text) && main.Preview.Text.Contains("天麻素") && main.Preview.Text.Contains("本例降糖处理合成测试"), "疾病模板与勾选文字进入完整首程预览");
                File.WriteAllText(Path.Combine(directory, "首程计划导出.txt"), main.Preview.Text, new System.Text.UTF8Encoding(true));
                TestSupport.Check(File.ReadAllText(Path.Combine(directory, "首程计划导出.txt")) == main.Preview.Text, "首程生成正文TXT逐字一致");
                main.Preview.ScrollToEnd(); TestSupport.Capture(main.Window, Path.Combine(directory, "02_合并高血压糖尿病.png"));
                main.Save(); restored = new MainWindow(main.Profile); restored.Window.Show(); restored.GoTo(3); Flush(restored);
                TestSupport.Check(restored.State.Flag(PlanComposer.FlagKey("plan-htn")) && restored.State.Flag(PlanComposer.FlagKey("plan-dm")) && restored.State.Flag(PlanComposer.FlagKey("plan-gastrodin")) && Generator.Plan(restored.Library, restored.State) == Generator.Plan(main.Library, main.State), "重启恢复疾病模板、勾选及本例处理文字");
                restored.Window.Close(); restored = null;
                Select(main, "plan-bppv");
                TestSupport.Check(main.State.Get("PlanBody").Contains("复位") && main.State.Get("PlanBody").Contains("Dix-Hallpike") && !main.State.Get("PlanBody").Contains("降糖"), "耳石症选择位置试验和复位方案");
                Select(main, "plan-ischemic"); TestSupport.Check(main.State.Get("PlanBody").Contains("再灌注") && main.State.Get("PlanBody").Contains("排除出血"), "脑梗死模板优先卒中流程和再灌注评估");
                TestSupport.Check(main.State.Flag(PlanComposer.FlagKey("plan-dm")) && main.State.Get(PlanComposer.TextKey("plan-dm")).Contains("本例降糖处理"), "同患者切换基础疾病保留已勾选共病及编辑");
                foreach (Template t in main.Library.Templates.Where(t => t.Kind == "plan")) { Select(main, t.Id); TestSupport.Check(main.State.Get("PlanBody") == t.Text && !Generator.Pending(t.Text), "可载入完整模板：" + t.Title); }
                main.NewPatient(true); main.State.Set("VteMode", "skip"); main.GoTo(3); Flush(main);
                TestSupport.Check(main.Library.Templates.Where(t => t.Kind == "plan-option").All(t => !main.State.Flag(PlanComposer.FlagKey(t.Id))) && !main.State.Fields.Keys.Any(k => k.StartsWith("PlanOptionText:")), "新患者清空全部共病药物勾选和处理编辑，即使保留鉴别组");
                Select(main, "plan-hemorrhage"); ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-danhong")]).IsChecked = true; Flush(main);
                TestSupport.Check(Generator.Pending(Generator.Plan(main.Library, main.State)) && !main.Ready(Generator.Plan(main.Library, main.State), false, false), "脑出血与丹红冲突标为待核对并阻止直接输出");
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-danhong")]).IsChecked = false; Flush(main);
                TestSupport.Check(!Generator.Pending(Generator.Plan(main.Library, main.State)), "取消冲突用药后恢复可输出计划");
                Select(main, "plan-dizziness"); ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-qingkailing")]).IsChecked = true; Flush(main);
                TestSupport.Check(Generator.Plan(main.Library, main.State).Contains("适应证及辨证") && !main.State.Get("PlanBody").Contains("清开灵"), "清开灵独立勾选，保留适应证辨证条件，基础模板不预置");
                ((TextBox)main.Fields[PlanComposer.TextKey("plan-qingkailing")]).Clear();
                TestSupport.Check(Generator.Pending(Generator.Plan(main.Library, main.State)), "已勾选但清空处理文字会标记待补充");
                main.NewPatient(false); main.State.Set("VteMode", "skip"); main.GoTo(3); Select(main, "plan-dizziness"); main.Generate(false);
                ((CheckBox)main.Fields[PlanComposer.FlagKey("plan-dm")]).IsChecked = true; Flush(main);
                TestSupport.Check(main.Stale, "更改共病勾选会标记旧预览需要更新");
                main.Window.Width = 860; main.Window.Height = 740; TestSupport.Capture(main.Window, Path.Combine(directory, "03_小屏诊疗计划.png"));
                TestSupport.Check(main.Find<Button>("GenerateButton").IsVisible && main.Preview.ActualHeight > 30, "小屏保留生成与预览并可滚动编辑计划");
                var old = new State(); old.Set("PlanBody", PlanComposer.LegacyCommon); old.Set("Chief", "旧患者资料");
                TestSupport.Check(PlanComposer.Migrate(main.Library, old) && old.Get("PlanBody") == main.Library.Get("plan-common").Text && old.Get("Chief") == "旧患者资料", "旧版原封通用计划升级为按需计划并保留患者字段");
                string legacyProfile = Path.Combine(directory, "legacy-profile"); old.Set("PlanTemplateId", ""); old.Set("PlanBody", PlanComposer.LegacyCommon); old.Preview = "旧版已生成文本"; old.Save(Path.Combine(legacyProfile, "draft.xml"));
                restored = new MainWindow(legacyProfile);
                TestSupport.Check(restored.State.Get("PlanBody") == main.Library.Get("plan-common").Text && restored.State.Get("Chief") == "旧患者资料" && restored.Preview.Text == "旧版已生成文本" && restored.Stale, "真实旧草稿启动升级正文，保留既有预览并标记需重新生成");
                restored.Window.Close(); restored = null;
                old.Set("PlanTemplateId", ""); old.Set("PlanBody", "已经手动修改的旧计划");
                TestSupport.Check(!PlanComposer.Migrate(main.Library, old) && old.Get("PlanBody") == "已经手动修改的旧计划", "旧版人工编辑计划保留原文");
                main.NewPatient(false); main.State.Set("VteMode", "low"); main.State.Set("VteScore", "2");
                TestSupport.Check(!Generator.Plan(main.Library, main.State).Contains("控制血糖血压"), "低危VTE不再带入通用血糖血压宣教文字");
                File.WriteAllLines(Path.Combine(directory, "plan-tests.txt"), TestSupport.Results.Concat(new[] { "TOTAL " + TestSupport.Results.Count + " plan checks passed" }));
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(directory, "plan-tests.txt"), TestSupport.Results.Concat(new[] { ex.ToString() })); Environment.ExitCode = 1; }
            finally { if (restored != null) restored.Window.Close(); main.Window.Close(); }
        }
    }
}
