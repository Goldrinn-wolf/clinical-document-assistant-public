using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace InstructionsAssistant
{
    public class Entry
    {
        public string Id, Title, Text, Pinyin, Initials, Origin, Source, Warning;
        public int Step, Order;
        public bool Custom { get { return Id.StartsWith("custom-", StringComparison.Ordinal); } }
    }
    public class Library
    {
        public static readonly string[] StepNames = { "基础疾病", "服药说明", "复诊安排", "股动脉造影", "其他病症" };
        public List<Entry> Entries = new List<Entry>();
        public static Library Load(Stream stream)
        {
            Library library = new Library();
            XDocument x = XDocument.Load(stream);
            if (x.Root == null || x.Root.Name != "Library") throw new InvalidDataException("医嘱库格式无效。");
            foreach (XElement e in x.Root.Elements("Entry")) library.Entries.Add(new Entry {
                Id = (string)e.Attribute("id"), Step = (int)e.Attribute("step"), Order = (int)e.Attribute("order"), Title = (string)e.Attribute("title"),
                Pinyin = (string)e.Attribute("pinyin") ?? "", Initials = (string)e.Attribute("initials") ?? "", Origin = (string)e.Attribute("origin") ?? "自定义",
                Text = (string)e.Element("Text"), Source = (string)e.Element("Source") ?? "", Warning = (string)e.Element("Warning") ?? ""
            });
            if (library.Entries.Any(e => String.IsNullOrWhiteSpace(e.Id) || String.IsNullOrWhiteSpace(e.Title) || String.IsNullOrWhiteSpace(e.Text) || e.Step < 1 || e.Step > 5) || library.Entries.Select(e => e.Id).Distinct().Count() != library.Entries.Count) throw new InvalidDataException("医嘱条目缺失字段、步骤无效或ID重复。");
            library.Entries = library.Entries.OrderBy(e => e.Step).ThenBy(e => e.Order).ToList();
            return library;
        }
        public static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
        public static string Key(string s) { return new string((s ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray()); }
        public bool Matches(Entry e, string search)
        {
            return (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).All(q => Key(e.Title + e.Text).Contains(Key(q)) || Key(e.Pinyin).Contains(Key(q)) || Key(e.Initials).Contains(Key(q)));
        }
        public List<Entry> SelectedInStep(State state, int step) { return Entries.Where(e => e.Step == step && state.Selected.Contains(e.Id)).OrderBy(e => e.Order).ToList(); }
        public string Validate(State state)
        {
            if (state.Selected.Any(id => !Entries.Any(e => e.Id == id))) return "选择中存在无法识别的医嘱，请重新选择。";
            if (SelectedInStep(state, 1).Count == 0) return "步骤1：请至少选择一种基础疾病。";
            if (SelectedInStep(state, 2).Count != 1) return "步骤2：请选择一条服药说明。";
            if (SelectedInStep(state, 3).Count != 1) return "步骤3：请选择一条复诊安排。";
            if (state.Puncture < 0) return "步骤4：请明确选择是否做过股动脉造影。";
            if (state.Puncture > 0 && SelectedInStep(state, 4).Count != 1) return "步骤4：已做造影，请选择一条适用的穿刺后医嘱。";
            return null;
        }
        public string Generate(State state)
        {
            string error = Validate(state); if (error != null) throw new InvalidOperationException(error);
            List<string> lines = new List<string>();
            for (int step = 1; step <= 5; step++)
            {
                if (step == 4 && state.Puncture == 0) continue;
                List<Entry> selected = SelectedInStep(state, step);
                if (selected.Count == 0) continue;
                string body = String.Join("；", selected.Select(e => Normalize(e.Text).Trim().TrimEnd('。', '；', ';', '，', ',', '\r', '\n')));
                lines.Add((lines.Count + 1) + "." + body + "。");
            }
            return "出院医嘱：\r\n" + String.Join("\r\n\r\n", lines);
        }
        public string Signature(State state) { return state.Puncture + "|" + String.Join(",", state.Selected.OrderBy(id => id)); }
        public Entry Add(int step, string title, string text, string pinyin)
        {
            if (step < 1 || step > 5 || String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(text)) throw new ArgumentException("请填写步骤、条目名称和医嘱正文。");
            if (Entries.Any(e => e.Step == step && e.Title == title.Trim() && e.Text == Normalize(text.Trim()))) throw new ArgumentException("本步骤已有相同名称和正文的条目。");
            string py = String.IsNullOrWhiteSpace(pinyin) ? PinyinLookup.Convert(title.Trim()) : pinyin.Trim().ToLowerInvariant();
            Entry added = new Entry { Id = "custom-" + Guid.NewGuid().ToString("N"), Step = step, Order = Entries.Count == 0 ? 1 : Entries.Max(e => e.Order) + 1, Title = title.Trim(), Text = Normalize(text.Trim()), Pinyin = Key(py), Initials = Key(String.Concat(py.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Substring(0, 1)))), Origin = "自定义", Source = "", Warning = "" };
            Entries.Add(added); return added;
        }
        public void SaveCustom(string path)
        {
            XElement root = new XElement("Library", Entries.Where(e => e.Custom).Select(e => new XElement("Entry", new XAttribute("id", e.Id), new XAttribute("step", e.Step), new XAttribute("order", e.Order), new XAttribute("title", e.Title), new XAttribute("pinyin", e.Pinyin), new XAttribute("initials", e.Initials), new XAttribute("origin", e.Origin), new XElement("Text", e.Text), new XElement("Source", e.Source), new XElement("Warning", e.Warning))));
            Storage.Save(new XDocument(root), path);
        }
        public void MergeCustom(string path)
        {
            Library extra; using (Stream s = File.OpenRead(path)) extra = Load(s);
            if (extra.Entries.Any(e => !e.Custom || Entries.Any(existing => existing.Id == e.Id))) throw new InvalidDataException("自定义条目ID无效或重复。");
            Entries.AddRange(extra.Entries);
        }
    }
    public class State
    {
        public HashSet<string> Selected = new HashSet<string> { "default-medication", "word-151", "word-152" };
        public int Step = 1, Puncture = -1;
        public string Preview = "", GeneratedSignature = "";
        public bool Edited;
        public double FontSize = 15;
        public static State Load(string path)
        {
            XDocument x = XDocument.Load(path);
            if (x.Root == null || x.Root.Name != "Draft") throw new InvalidDataException("草稿格式无效。");
            return new State { Selected = new HashSet<string>(x.Root.Elements("Selected").Select(e => e.Value)), Step = Math.Max(1, Math.Min(5, (int?)x.Root.Element("Step") ?? 1)), Puncture = Math.Max(-1, Math.Min(1, (int?)x.Root.Element("Puncture") ?? -1)), Preview = (string)x.Root.Element("Preview") ?? "", GeneratedSignature = (string)x.Root.Element("Signature") ?? "", Edited = (bool?)x.Root.Element("Edited") ?? false, FontSize = Math.Max(12, Math.Min(24, (double?)x.Root.Element("FontSize") ?? 15)) };
        }
        public void Save(string path) { Storage.Save(new XDocument(new XElement("Draft", new XElement("Step", Step), new XElement("Puncture", Puncture), new XElement("Preview", Preview), new XElement("Signature", GeneratedSignature), new XElement("Edited", Edited), new XElement("FontSize", FontSize), Selected.OrderBy(id => id).Select(id => new XElement("Selected", id)))), path); }
    }
    public static class Storage
    {
        public static void Save(XDocument doc, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (XmlWriter w = XmlWriter.Create(path + ".tmp", new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize })) doc.Save(w);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak", true); else File.Move(path + ".tmp", path);
        }
    }
    public static class PinyinLookup
    {
        private static Dictionary<char, string> map;
        public static string Convert(string title)
        {
            if (map == null) { map = new Dictionary<char, string>(); using (StreamReader r = new StreamReader(Program.Resource("pinyin.tsv"), Encoding.UTF8)) { string line; while ((line = r.ReadLine()) != null) if (line.Length > 2) map[line[0]] = line.Substring(2); } }
            return String.Join(" ", title.Select(c => map.ContainsKey(c) ? map[c] : c.ToString().ToLowerInvariant()));
        }
    }
}
