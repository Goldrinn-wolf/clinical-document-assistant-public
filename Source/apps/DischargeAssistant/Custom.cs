using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DischargeAssistant
{
    public static class CustomLibrary
    {
        public static bool IsCustom(string id) { return id != null && id.StartsWith("custom-", StringComparison.Ordinal); }
        public static Category AddCategory(Library library, string title, string note)
        {
            title = (title ?? "").Trim().TrimEnd('：', ':');
            if (String.IsNullOrWhiteSpace(title)) throw new ArgumentException("请填写模块名称。");
            if (library.Categories.Any(existing => Library.SearchKey(existing.Title) == Library.SearchKey(title))) throw new ArgumentException("已有同名模块，请使用其他名称。");
            Category c = new Category { Id = "custom-c-" + Guid.NewGuid().ToString("N"), Title = title, Order = library.Categories.Max(x => x.Order) + 1 };
            if (!String.IsNullOrWhiteSpace(note)) c.Notes.Add(Normalize(note.Trim()));
            library.Categories.Add(c); return c;
        }
        public static Drug AddDrug(Library library, Category category, string name, string usage, string note, string pinyin)
        {
            if (!library.Categories.Contains(category)) throw new ArgumentException("请选择药物所属模块。");
            if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(usage)) throw new ArgumentException("请填写药物名称和用法正文。");
            string py = String.IsNullOrWhiteSpace(pinyin) ? Pinyin(name.Trim()) : pinyin.Trim().ToLowerInvariant();
            Drug d = new Drug { Id = "custom-d-" + Guid.NewGuid().ToString("N"), Category = category, Name = name.Trim(), Text = name.Trim() + "  用法：" + Normalize(usage.Trim()), Order = category.Drugs.Count == 0 ? 1 : category.Drugs.Max(x => x.Order) + 1, Pinyin = Library.SearchKey(py), Initials = Library.SearchKey(String.Concat(py.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Substring(0, 1)))), Section = "" };
            if (!String.IsNullOrWhiteSpace(note)) d.Notes.Add(Normalize(note.Trim()));
            category.Drugs.Add(d); return d;
        }
        private static Dictionary<char, string> syllables;
        public static string Pinyin(string name)
        {
            if (syllables == null)
            {
                syllables = new Dictionary<char, string>();
                using (StreamReader reader = new StreamReader(Program.Resource("pinyin.tsv"), Encoding.UTF8))
                {
                    string line; while ((line = reader.ReadLine()) != null) if (line.Length > 2) syllables[line[0]] = line.Substring(2);
                }
            }
            return String.Join(" ", name.Select(ch => syllables.ContainsKey(ch) ? syllables[ch] : ch.ToString().ToLowerInvariant()));
        }
        private static string Normalize(string text) { return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
        public static void Save(Library library, string path)
        {
            XElement cats = new XElement("Categories");
            foreach (Category c in library.Categories.Where(c => IsCustom(c.Id) || c.Drugs.Any(d => IsCustom(d.Id))))
            {
                XElement cat = new XElement("Category", new XAttribute("id", c.Id), new XAttribute("title", c.Title), new XAttribute("order", c.Order));
                if (IsCustom(c.Id)) cat.Add(c.Notes.Select(n => new XElement("Note", n)));
                foreach (Drug d in c.Drugs.Where(d => IsCustom(d.Id))) cat.Add(new XElement("Drug", new XAttribute("id", d.Id), new XAttribute("name", d.Name), new XAttribute("order", d.Order), new XAttribute("pinyin", d.Pinyin ?? ""), new XAttribute("initials", d.Initials ?? ""), new XAttribute("section", ""), new XElement("Text", d.Text), d.Notes.Select(n => new XElement("Note", n))));
                cats.Add(cat);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XDocument doc = new XDocument(new XElement("Library", new XElement("Intro", "自定义药库"), cats));
            using (XmlWriter writer = XmlWriter.Create(path + ".tmp", new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize })) doc.Save(writer);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak", true); else File.Move(path + ".tmp", path);
        }
        public static void Merge(Library library, string path)
        {
            Library custom;
            using (Stream s = File.OpenRead(path)) custom = Library.Load(s, true);
            // Validate the whole file before applying changes.
            foreach (Category c in custom.Categories)
            {
                if (!IsCustom(c.Id) && !library.Categories.Any(x => x.Id == c.Id)) throw new InvalidDataException("自定义药物所属模块不存在。");
                if (IsCustom(c.Id) && library.Categories.Any(x => x.Id == c.Id)) throw new InvalidDataException("自定义模块ID重复。");
                foreach (Drug d in c.Drugs) if (!IsCustom(d.Id) || library.Drugs.Any(x => x.Id == d.Id)) throw new InvalidDataException("自定义药物ID无效或重复。");
            }
            foreach (Category c in custom.Categories)
            {
                Category target = library.Categories.FirstOrDefault(x => x.Id == c.Id);
                if (target == null) { library.Categories.Add(c); continue; }
                foreach (Drug d in c.Drugs) { d.Category = target; target.Drugs.Add(d); }
                target.Drugs = target.Drugs.OrderBy(d => d.Order).ToList();
            }
            library.Categories = library.Categories.OrderBy(c => c.Order).ToList();
        }
    }
    public partial class MainWindow
    {
        private string customPath;
        private bool customWritable = true;
        private void RestoreCustom()
        {
            if (!File.Exists(customPath)) return;
            try { CustomLibrary.Merge(Library, customPath); }
            catch
            {
                try { CustomLibrary.Merge(Library, customPath + ".bak"); status.Text = "自定义药库异常，已恢复备份"; }
                catch { customWritable = false; status.Text = "自定义药库无法读取，原文件已保留；请备份并修复后再添加"; }
            }
        }
        private void UpdateLibraryInfo() { Find<TextBlock>("LibraryInfo").Text = Library.Categories.Count + " 个分类 · " + Library.Drugs.Count + " 个模板条目"; }
        public Category AddCustomCategory(string title, string note)
        {
            if (!customWritable) throw new InvalidOperationException("自定义药库无法读取，暂不能覆盖保存。");
            Category c = CustomLibrary.AddCategory(Library, title, note);
            try { CustomLibrary.Save(Library, customPath); } catch { Library.Categories.Remove(c); throw; }
            active = c; selectedMode = reviewMode = false; Search.Clear();
            restoring = true; categories.Items.Clear(); restoring = false;
            RefreshCategories(); categories.ScrollIntoView(categories.SelectedItem); RefreshDrugs(); UpdateLibraryInfo(); ScheduleSave(); status.Text = "已添加模块：" + c.Title; return c;
        }
        public Drug AddCustomDrug(Category category, string name, string usage, string note, string pinyin)
        {
            if (!customWritable) throw new InvalidOperationException("自定义药库无法读取，暂不能覆盖保存。");
            Drug d = CustomLibrary.AddDrug(Library, category, name, usage, note, pinyin);
            try { CustomLibrary.Save(Library, customPath); } catch { category.Drugs.Remove(d); throw; }
            active = category; selectedMode = reviewMode = false; Search.Clear();
            restoring = true; categories.Items.Clear(); restoring = false;
            RefreshCategories(); categories.ScrollIntoView(categories.SelectedItem); RefreshDrugs(); UpdateLibraryInfo(); ScheduleSave(); status.Text = "已添加药物：" + d.Name; return d;
        }
        public Window CreateCustomWindow()
        {
            Window w = new Window { Title = "自定义添加药物与模块", Owner = Window, Width = 590, Height = 690, MinWidth = 470, MinHeight = 510, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Window.FontFamily, FontSize = 14, Background = Brush("#F3F6F8") };
            TabControl tabs = new TabControl { Margin = new Thickness(18) };
            StackPanel drug = new StackPanel { Margin = new Thickness(18) };
            ComboBox category = new ComboBox { ItemsSource = Library.Categories, DisplayMemberPath = "Title", SelectedItem = active, Margin = new Thickness(0, 5, 0, 10), Padding = new Thickness(8) };
            drug.Children.Add(new TextBlock { Text = "所属模块（可选择原有或自定义模块）" }); drug.Children.Add(category);
            TextBox name = Field(drug, "药物名称 *", false);
            TextBox usage = Field(drug, "用法正文 *（直接填写，生成时自动加“用法：”）", true);
            TextBox note = Field(drug, "药物专属注意事项（选填）", true);
            TextBox py = Field(drug, "拼音（选填，用空格分隔每字，可修正多音字）", false);
            drug.Children.Add(new TextBlock { Text = "拼音留空时自动生成全拼与首字母索引。\n保存后可勾选药物，按模块内末尾顺序生成。", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#728994"), Margin = new Thickness(0, 0, 0, 12) });
            Button saveDrug = new Button { Content = "保存药物", Background = Brush("#10837D"), Foreground = System.Windows.Media.Brushes.White };
            saveDrug.Click += (s, e) => { try { AddCustomDrug((Category)category.SelectedItem, name.Text, usage.Text, note.Text, py.Text); w.Close(); } catch (Exception ex) { MessageBox.Show(w, ex.Message, "未保存"); } };
            drug.Children.Add(saveDrug);
            StackPanel module = new StackPanel { Margin = new Thickness(18) };
            TextBox moduleName = Field(module, "模块名称 *（即左侧药物分类）", false);
            TextBox moduleNote = Field(module, "模块共用注意事项（选填）", true);
            module.Children.Add(new TextBlock { Text = "新模块排在原有分类后，可添加多种药物。\n共用注意事项仅在选择该模块药物后生成一次。\n空模块不出现在生成文本中。", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#728994"), Margin = new Thickness(0, 0, 0, 14) });
            Button saveModule = new Button { Content = "保存模块", Background = Brush("#10837D"), Foreground = System.Windows.Media.Brushes.White };
            saveModule.Click += (s, e) => { try { Category c = AddCustomCategory(moduleName.Text, moduleNote.Text); category.ItemsSource = null; category.ItemsSource = Library.Categories; category.SelectedItem = c; tabs.SelectedIndex = 0; moduleName.Clear(); moduleNote.Clear(); } catch (Exception ex) { MessageBox.Show(w, ex.Message, "未保存"); } };
            module.Children.Add(saveModule);
            tabs.Items.Add(new TabItem { Header = "添加药物", Content = new ScrollViewer { Content = drug, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            tabs.Items.Add(new TabItem { Header = "添加药物模块", Content = new ScrollViewer { Content = module, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            w.Content = tabs; return w;
        }
        private static TextBox Field(StackPanel panel, string label, bool multiline)
        {
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            TextBox box = new TextBox { Margin = new Thickness(0, 5, 0, 12), AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, Height = multiline ? 90 : 40, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
            panel.Children.Add(box); return box;
        }
        private void ShowCustom() { CreateCustomWindow().ShowDialog(); }
    }
}
