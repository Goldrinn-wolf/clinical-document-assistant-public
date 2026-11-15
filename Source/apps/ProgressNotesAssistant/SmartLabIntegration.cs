using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SuiteCustomization;

namespace ProgressNotesAssistant
{
    public partial class MainWindow
    {
        public OfflineLab.MainWindow ActiveSmartLab;
        public OfflineLab.MainWindow CreateSmartLab(Window owner)
        {
            var smart = new OfflineLab.MainWindow(); smart.Window.Owner = owner; smart.Window.WindowStartupLocation = WindowStartupLocation.CenterOwner; smart.Window.ShowInTaskbar = false; smart.Window.Title = "检验智能整理 · 四合一 v1.4.1-public"; return smart;
        }
        public void OpenSmartLab(Window owner)
        {
            ActiveSmartLab = CreateSmartLab(owner); try { ActiveSmartLab.Window.ShowDialog(); } finally { ActiveSmartLab = null; }
        }
    }
    public static class SmartLabTests
    {
        private static void ClickSmart(MainWindow main, Button button, Window expectedOwner, string directory, string screenshot)
        {
            Exception error = null; bool reached = false;
            main.Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    var smart = main.ActiveSmartLab; reached = true;
                    TestSupport.Check(smart != null && smart.Window.IsVisible && smart.Window.Owner == expectedOwner, "实际点击打开内嵌智能整理窗口：" + screenshot);
                    smart.Input.Text = OfflineLab.MainWindow.Sample; smart.Arrange();
                    TestSupport.Check(smart.CanOutput() && smart.Output.Text.EndsWith("3、肾功能、甲功、凝血无明显异常。") && !smart.Output.Text.Contains("\n"), "内嵌窗口生成连续编号和正常汇总：" + screenshot);
                    string file = Path.Combine(directory, screenshot + ".txt"); TestSupport.Check(smart.ExportTo(file) && File.ReadAllText(file) == smart.Output.Text, "内嵌窗口实际TXT导出一致：" + screenshot);
                    TestSupport.Capture(smart.Window, Path.Combine(directory, screenshot + ".png"));
                }
                catch (Exception ex) { error = ex; }
                finally { if (main.ActiveSmartLab != null) main.ActiveSmartLab.Window.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (error != null) throw error; TestSupport.Check(reached && main.ActiveSmartLab == null, "关闭内嵌窗口返回原助手：" + screenshot);
        }
        public static void Run(MainWindow main, string directory)
        {
            Directory.CreateDirectory(directory); TestSupport.Results.Clear(); ExamDialog exams = null;
            try
            {
                main.NewPatient(false); Core.UpdateResult(main.State, "urine", true, "原检查合成结果"); string before = Generator.Auxiliary(main.Library, main.State);
                var button = (Button)main.Window.FindName("SmartLabButton"); main.Window.UpdateLayout();
                TestSupport.Check(button.IsVisible, "原助手顶部可见检验智能整理入口");
                ClickSmart(main, button, main.Window, directory, "01_助手顶部进入智能整理");
                TestSupport.Check(Generator.Auxiliary(main.Library, main.State) == before && main.State.Exam("urine").Result == "原检查合成结果", "智能整理不改写手动检查选择和结果");
                exams = main.CreateExamsDialog(); exams.Window.Show(); exams.Window.UpdateLayout();
                TestSupport.Check(exams.SmartLabButton.IsVisible && exams.Categories.Items.Count == 4, "四分类窗口同时保留手动检查和智能整理入口");
                ClickSmart(main, exams.SmartLabButton, exams.Window, directory, "02_辅助检查内进入智能整理"); exams.Cancel(); exams = null;
                TestSupport.Check(Generator.Auxiliary(main.Library, main.State) == before, "嵌套关闭后仍保留原有检查正文");
                TestSupport.Capture(main.Window, Path.Combine(directory, "03_原助手与新增入口.png"));
                TestSupport.Check(OfflineLab.Tests.Core(Path.Combine(directory, "parser")) == 0, "内嵌组件46项识别回归通过");
                var embedded = main.CreateSmartLab(main.Window); OfflineLab.Tests.UI(embedded, Path.Combine(directory, "embedded-ui"));
                TestSupport.Check(Environment.ExitCode == 0, "内嵌组件界面和导出回归通过");
                main.NewPatient(false); TestSupport.Check(main.State.Exam("urine").Result == "" && !main.State.Exam("urine").Selected, "原新患者清空逻辑仍正常");
                File.WriteAllLines(Path.Combine(directory, "integration-tests.txt"), TestSupport.Results.Concat(new[] { "TOTAL " + TestSupport.Results.Count + " integration checks passed" }));
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(directory, "integration-tests.txt"), TestSupport.Results.Concat(new[] { ex.ToString() })); Environment.ExitCode = 1; }
            finally { if (exams != null) exams.Cancel(); if (main.ActiveSmartLab != null) main.ActiveSmartLab.Window.Close(); main.Window.Close(); }
        }
    }
}
