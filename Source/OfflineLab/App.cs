using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

#if !OFFLINE_LAB_EMBEDDED
[assembly: AssemblyTitle("离线检验智能整理")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
#endif
namespace OfflineLab
{
    #if !OFFLINE_LAB_EMBEDDED
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            bool test = args.Length > 0 && args[0] == "--ui-test";
            string directory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "OfflineLabTests");
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") return Tests.Core(directory);
                Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; if (test) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "ui-error.txt"), e.Exception.ToString()); app.Shutdown(1); } else MessageBox.Show("操作未完成：" + e.Exception.Message, "离线检验智能整理"); };
                MainWindow main = new MainWindow();
                if (test) main.Window.Loaded += delegate { main.Window.Dispatcher.BeginInvoke(new Action(delegate { Tests.UI(main, directory); }), DispatcherPriority.ApplicationIdle); };
                return app.Run(main.Window);
            }
            catch (Exception ex) { if (test) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "startup-error.txt"), ex.ToString()); } else MessageBox.Show("启动未完成：" + ex.Message, "离线检验智能整理"); return 1; }
        }
    }
    #endif
    public class MainWindow
    {
        public Window Window; public TextBox Input, Output, CombinedName; public CheckBox RenameLiver, IncludeTime; public Button ArrangeButton, CopyButton, ExportButton; public TextBlock Status; public DataGrid Details; public FormatResult Parsed;
        private string signature = "";
        public const string Sample = "2026100809:58 血细胞五项：粒细胞↑，白细胞↓，\r\n2026-10-08 10:06 肝功能：丙氨酸氨基转移酶↑\r\n202610081009 肾功能：\r\n202610081013 甲功：\r\n202610081015 凝血：";
        public MainWindow()
        {
            Window = new Window { Title = "离线检验智能整理 v1.0.0", Width = 1240, Height = 860, MinWidth = 860, MinHeight = 680, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brush("#F3F6F8"), FontFamily = new FontFamily("Microsoft YaHei, Segoe UI"), FontSize = 14 };
            Grid root = new Grid { Margin = new Thickness(22) }; Window.Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(185) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel header = new StackPanel(); header.Children.Add(Text("离线检验智能整理", 26, "#153D49")); header.Children.Add(Text("整批粘贴 → 本地识别 → 连续编号输出。仅按空冒号规则汇总，不根据数值判断异常。", 13, "#68838D")); root.Children.Add(header);
            WrapPanel options = new WrapPanel { Margin = new Thickness(0, 8, 0, 12), VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(options, 1); root.Children.Add(options);
            RenameLiver = new CheckBox { Content = "肝功能改为组合表头", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) }; options.Children.Add(RenameLiver);
            CombinedName = Box(new FormatOptions().CombinedName); CombinedName.Width = 340; CombinedName.Height = 36; CombinedName.AcceptsReturn = false; CombinedName.AcceptsTab = false; CombinedName.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; CombinedName.VerticalContentAlignment = VerticalAlignment.Center; options.Children.Add(CombinedName);
            IncludeTime = new CheckBox { Content = "正文保留原始时间", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 4, 0, 4) }; options.Children.Add(IncludeTime);
            Grid editors = new Grid { Margin = new Thickness(0, 0, 0, 12) }; editors.ColumnDefinitions.Add(new ColumnDefinition()); editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); editors.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(editors, 2); root.Children.Add(editors);
            Input = Box(""); Output = Box(""); Output.IsReadOnly = true; Output.Background = Brush("#FFFFFF");
            editors.Children.Add(Editor("1  粘贴医院检验全文", "每个项目换行；兼容不同数字时间串和中英文冒号。", Input));
            Grid right = Editor("2  整理结果 · 连续一段", "有文字的项目在前；空结果合并到最后编号。", Output); Grid.SetColumn(right, 2); editors.Children.Add(right);
            Grid detailArea = new Grid(); detailArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detailArea.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(detailArea, 3); root.Children.Add(detailArea); detailArea.Children.Add(Text("识别明细 · 原名、原始时间和结果可逐项核对", 13, "#68838D"));
            Details = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, Background = Brushes.White, BorderBrush = Brush("#D7E2E7"), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, FontSize = 12 };
            AddColumn("原项目名", "SourceTitle", 140); AddColumn("原始时间", "RawTime", 175); AddColumn("输出项目名", "Title", 260); AddColumn("原结果", "Result", 230); AddColumn("整理方式", "Status", 165); Grid.SetRow(Details, 1); detailArea.Children.Add(Details);
            StackPanel footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(footer, 4); root.Children.Add(footer);
            Status = Text("粘贴全文后点击“开始整理”。", 13, "#68838D"); Status.MaxHeight = 88; footer.Children.Add(Status);
            WrapPanel actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
            actions.Children.Add(Button("载入测试示例", delegate { if (Input.Text.Length > 0 && MessageBox.Show(Window, "用合成示例替换当前输入？", "载入示例", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; Input.Text = Sample; Arrange(); }));
            actions.Children.Add(Button("清空", delegate { Input.Clear(); Output.Clear(); Details.ItemsSource = null; Parsed = null; Changed(); }));
            ArrangeButton = Button("开始整理  Ctrl+Enter", Arrange); ArrangeButton.Background = Brush("#10837D"); ArrangeButton.Foreground = Brushes.White; actions.Children.Add(ArrangeButton);
            CopyButton = Button("复制整理结果", delegate { Copy(); }); actions.Children.Add(CopyButton);
            ExportButton = Button("导出TXT", delegate { if (!CanOutput()) return; SaveFileDialog dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "检验整理结果.txt" }; if (dialog.ShowDialog(Window) == true) ExportTo(dialog.FileName); }); actions.Children.Add(ExportButton);
            Input.TextChanged += delegate { Changed(); }; CombinedName.TextChanged += delegate { Changed(); }; RenameLiver.Checked += delegate { Changed(); }; RenameLiver.Unchecked += delegate { Changed(); }; IncludeTime.Checked += delegate { Changed(); }; IncludeTime.Unchecked += delegate { Changed(); };
            Window.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && e.Key == System.Windows.Input.Key.Enter) { Arrange(); e.Handled = true; } };
            CopyButton.IsEnabled = ExportButton.IsEnabled = false;
        }
        public FormatOptions Options() { return new FormatOptions { RenameLiver = RenameLiver.IsChecked == true, CombinedName = CombinedName.Text, IncludeTime = IncludeTime.IsChecked == true }; }
        private string Token() { return Input.Text + "\n" + RenameLiver.IsChecked + "\n" + CombinedName.Text + "\n" + IncludeTime.IsChecked; }
        private void Changed() { if (CopyButton == null) return; CopyButton.IsEnabled = ExportButton.IsEnabled = false; Status.Foreground = Brush("#A37424"); Status.Text = "输入或选项已变化，请点击开始整理。"; }
        public void Arrange()
        {
            Parsed = Formatter.Parse(Input.Text, Options()); signature = Token(); Output.Text = Parsed.Output; Details.ItemsSource = Parsed.Records;
            int empty = Parsed.Records.Count(r => r.Empty); Status.Text = "识别 " + Parsed.Records.Count + " 项：有文字 " + (Parsed.Records.Count - empty) + " 项，空结果 " + empty + " 项。" + (Parsed.Ready ? "已生成连续一段，请核对后复制。" : "有 " + Parsed.Issues.Count + " 处待核对：" + String.Join("；", Parsed.Issues)); Status.Foreground = Brush(Parsed.Ready ? "#10837D" : "#A37424");
            CopyButton.IsEnabled = ExportButton.IsEnabled = CanOutput();
        }
        public bool CanOutput() { return Parsed != null && Parsed.Ready && signature == Token() && Output.Text == Parsed.Output; }
        public bool Copy()
        {
            if (!CanOutput()) return false;
            for (int i = 0; i < 5; i++) try { Clipboard.SetText(Output.Text, TextDataFormat.UnicodeText); Status.Text = "已复制连续编号的整理结果。"; return true; } catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(40); }
            Status.Text = "剪贴板被占用，请重试或导出TXT。"; return false;
        }
        public bool ExportTo(string path) { if (!CanOutput()) return false; File.WriteAllText(path, Output.Text, new UTF8Encoding(true)); Status.Text = "已导出TXT。"; return true; }
        private void AddColumn(string header, string key, int width) { Details.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(key), Width = new DataGridLength(width) }); }
        public static Brush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        private static TextBlock Text(string text, int size, string color) { return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) }; }
        private static TextBox Box(string text) { return new TextBox { Text = text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), BorderBrush = Brush("#D7E2E7"), Background = Brushes.White, Foreground = Brush("#244958") }; }
        private static Button Button(string text, Action action) { Button button = new Button { Content = text, Padding = new Thickness(15, 10, 15, 10), Margin = new Thickness(8, 3, 0, 3), Background = Brushes.White, Foreground = Brush("#244958"), BorderBrush = Brush("#D7E2E7") }; button.Click += delegate { action(); }; return button; }
        private static Grid Editor(string title, string hint, TextBox box) { Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.Children.Add(Text(title, 17, "#244958")); TextBlock caption = Text(hint, 12, "#68838D"); Grid.SetRow(caption, 1); grid.Children.Add(caption); Grid.SetRow(box, 2); grid.Children.Add(box); return grid; }
    }
}
