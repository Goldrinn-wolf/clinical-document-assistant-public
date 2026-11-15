using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FirstCourseAssistant
{
    public class Template
    {
        public string Id, Kind, Title, Text, Pinyin, Initials;
        public int Order;
        public bool Custom { get { return Id.StartsWith("custom-", StringComparison.Ordinal); } }
    }
    public class Library
    {
        public List<Template> Templates = new List<Template>();
        public Dictionary<string, string> References = new Dictionary<string, string>();
        public static Library Load(Stream s)
        {
            XDocument x = XDocument.Load(s); if (x.Root == null || x.Root.Name != "Library") throw new InvalidDataException("模板库格式无效。");
            Library lib = new Library();
            foreach (XElement e in x.Root.Elements("Template")) lib.Templates.Add(new Template { Id = (string)e.Attribute("id"), Kind = (string)e.Attribute("kind"), Title = (string)e.Attribute("title"), Order = (int)e.Attribute("order"), Text = (string)e.Element("Text"), Pinyin = (string)e.Attribute("pinyin") ?? "", Initials = (string)e.Attribute("initials") ?? "" });
            if (lib.Templates.Any(t => String.IsNullOrWhiteSpace(t.Id) || String.IsNullOrWhiteSpace(t.Title) || String.IsNullOrWhiteSpace(t.Text) || !new[] { "ddx", "plan", "vte", "exam", "vte-preset", "plan-option" }.Contains(t.Kind)) || lib.Templates.Select(t => t.Id).Distinct().Count() != lib.Templates.Count) throw new InvalidDataException("模板字段无效或ID重复。");
            foreach (XElement e in x.Root.Elements("Reference")) lib.References[(string)e.Attribute("id")] = e.Value;
            return lib;
        }
        public Template Get(string id) { return Templates.First(t => t.Id == id); }
        public static string Key(string s) { return new string((s ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray()); }
        public bool Matches(Template t, string query) { return (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).All(q => Key(t.Title + t.Text).Contains(Key(q)) || Key(t.Pinyin).Contains(Key(q)) || Key(t.Initials).Contains(Key(q))); }
        public Template Add(string kind, string title, string text, string py)
        {
            if (!new[] { "ddx", "plan", "exam", "vte-preset" }.Contains(kind) || String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(text)) throw new ArgumentException("请选择模板类型，并填写名称及正文。");
            if (Templates.Any(t => t.Kind == kind && t.Title == title.Trim() && t.Text == Generator.Normalize(text.Trim()))) throw new ArgumentException("已有相同名称和正文的模板。");
            string syllables = String.IsNullOrWhiteSpace(py) ? PinyinLookup.Convert(title.Trim()) : py.Trim().ToLowerInvariant();
            Template added = new Template { Id = "custom-" + Guid.NewGuid().ToString("N"), Kind = kind, Title = title.Trim(), Text = Generator.Normalize(text.Trim()), Order = Templates.Count == 0 ? 1 : Templates.Max(t => t.Order) + 1, Pinyin = Key(syllables), Initials = Key(String.Concat(syllables.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Substring(0, 1)))) };
            Templates.Add(added); return added;
        }
        public void SaveCustom(string path)
        {
            Storage.Save(new XDocument(new XElement("Library", Templates.Where(t => t.Custom).Select(t => new XElement("Template", new XAttribute("id", t.Id), new XAttribute("kind", t.Kind), new XAttribute("title", t.Title), new XAttribute("order", t.Order), new XAttribute("pinyin", t.Pinyin), new XAttribute("initials", t.Initials), new XElement("Text", t.Text))))), path);
        }
        public void MergeCustom(string path)
        {
            Library extra; using (Stream s = File.OpenRead(path)) extra = Load(s);
            if (extra.Templates.Any(t => !t.Custom || t.Kind == "vte" || Templates.Any(existing => existing.Id == t.Id))) throw new InvalidDataException("自定义模板ID无效或重复。");
            Templates.AddRange(extra.Templates);
        }
    }
    public class State
    {
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public Dictionary<string, string> Overrides = new Dictionary<string, string>();
        public HashSet<string> Selected = new HashSet<string>();
        public int Step = 1;
        public string Preview = "", Signature = "";
        public bool Edited;
        public double FontSize = 15;
        public State() { Fields["CompanionOn"] = "true"; Fields["RiskOn"] = "true"; }
        public string Get(string key) { return Fields.ContainsKey(key) ? Fields[key] : ""; }
        public bool Flag(string key) { return Get(key) == "true"; }
        public void Set(string key, string text) { Fields[key] = text ?? ""; }
        public static State New(Library lib) { State s = new State(); s.Set("PlanBody", lib.Get("plan-common").Text); s.Set("PlanTemplateId", "plan-common"); return s; }
        public static State Load(string path)
        {
            XDocument x = XDocument.Load(path); if (x.Root == null || x.Root.Name != "Draft") throw new InvalidDataException("草稿格式无效。");
            State s = new State { Step = Math.Max(1, Math.Min(3, (int?)x.Root.Element("Step") ?? 1)), Preview = (string)x.Root.Element("Preview") ?? "", Signature = (string)x.Root.Element("Signature") ?? "", Edited = (bool?)x.Root.Element("Edited") ?? false, FontSize = Math.Max(12, Math.Min(24, (double?)x.Root.Element("FontSize") ?? 15)) };
            foreach (XElement e in x.Root.Elements("Field")) s.Fields[(string)e.Attribute("key")] = e.Value;
            foreach (XElement e in x.Root.Elements("Override")) s.Overrides[(string)e.Attribute("id")] = e.Value;
            s.Selected = new HashSet<string>(x.Root.Elements("Selected").Select(e => e.Value)); return s;
        }
        public void Save(string path)
        {
            Storage.Save(new XDocument(new XElement("Draft", new XElement("Step", Step), new XElement("Preview", Preview), new XElement("Signature", Signature), new XElement("Edited", Edited), new XElement("FontSize", FontSize), Fields.OrderBy(p => p.Key).Select(p => new XElement("Field", new XAttribute("key", p.Key), p.Value)), Overrides.OrderBy(p => p.Key).Select(p => new XElement("Override", new XAttribute("id", p.Key), p.Value)), Selected.OrderBy(id => id).Select(id => new XElement("Selected", id)))), path);
        }
    }
    public static class Generator
    {
        public static string Normalize(string s) { return (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
        private static string Trim(string s) { return (s ?? "").Trim().TrimEnd('。', '；', ';', '，', ','); }
        private static string Need(State s, string key, string label) { return String.IsNullOrWhiteSpace(s.Get(key)) ? "[待补充：" + label + "]" : Trim(Normalize(s.Get(key))); }
        public static string Diagnosis(State s)
        {
            string first = "患者系" + Need(s, "Age", "年龄组") + Need(s, "Gender", "性别") + "，" + Need(s, "Onset", "起病方式") + "，因" + Need(s, "Chief", "入院原因及病程") + "入院";
            if (!String.IsNullOrWhiteSpace(s.Get("Duration"))) first += "，持续" + Trim(s.Get("Duration"));
            if (s.Flag("CompanionOn")) first += "，伴" + Need(s, "Companion", "伴随症状");
            List<string> lines = new List<string> { first, s.Flag("ExamAsAbove") ? "神经系统查体大致同前" : "神经系统查体：" + Need(s, "ExamSummary", "查体要点") };
            if (s.Flag("RiskOn")) lines.Add("既往合并" + Need(s, "Risks", "既往危险因素") + "危险因素");
            lines.Add(s.Flag("TestsAsAbove") ? "院前辅助检查结果如上" : "院前辅助检查：" + Need(s, "Tests", "辅助检查结果"));
            if (!String.IsNullOrWhiteSpace(s.Get("ExtraEvidence"))) lines.Add(Trim(Normalize(s.Get("ExtraEvidence"))));
            return "诊断依据：\r\n" + String.Join("；", lines.Select((text, i) => (i + 1) + "." + text)) + "。";
        }
        public static string Differential(Library lib, State s)
        {
            List<string> blocks = new List<string>();
            foreach (Template t in lib.Templates.Where(t => t.Kind == "ddx" && s.Selected.Contains(t.Id)).OrderBy(t => t.Order)) blocks.Add(t.Title + "：\r\n" + Normalize(s.Overrides.ContainsKey(t.Id) ? s.Overrides[t.Id] : t.Text));
            if (!String.IsNullOrWhiteSpace(s.Get("DdxExtra"))) blocks.Add(Normalize(s.Get("DdxExtra").Trim()));
            if (blocks.Count == 0) blocks.Add("[待补充：选择鉴别诊断模板或填写补充文字]");
            return "鉴别诊断：\r\n" + String.Join("\r\n\r\n", blocks);
        }
        public static string Vte(Library lib, State s)
        {
            string mode = s.Get("VteMode"); if (mode == "skip") return "";
            if (mode == "custom") return Need(s, "VteCustom", "自定义VTE正文");
            if (mode != "low" && mode != "high") return "[待补充：VTE模板选择或明确不纳入]";
            decimal score; if (!Decimal.TryParse(s.Get("VteScore"), out score) || score < 0) return "[待补充：有效的VTE评分]";
            if ((mode == "low" && score >= 4) || (mode == "high" && score < 4)) return "[待核对：评分与所选原模板分组不一致]";
            string text = lib.Get(mode == "low" ? "vte-low" : "vte-high").Text;
            text = text.Replace("患者VTE评分", "患者VTE" + (String.IsNullOrWhiteSpace(s.Get("VteScale")) ? "" : "（" + s.Get("VteScale").Trim() + "）") + "评分");
            return text.Replace("评分 分", "评分" + s.Get("VteScore").Trim() + "分");
        }
        public static string Plan(Library lib, State s)
        {
            List<string> blocks = new List<string> { String.IsNullOrWhiteSpace(s.Get("PlanBody")) ? "[待补充：诊疗计划正文]" : Normalize(s.Get("PlanBody").Trim()) };
            if (s.Flag("AnalysisOn")) blocks.Add("病情分析与个体化计划：" + (String.IsNullOrWhiteSpace(s.Get("Analysis")) ? "[待补充：初步考虑、依据及本例安排]" : Normalize(s.Get("Analysis").Trim())));
            string additions = PlanComposer.Additions(lib, s); if (additions.Length > 0) blocks.Add(additions);
            string vte = Vte(lib, s); if (vte.Length > 0) blocks.Add(vte);
            return "诊疗计划：\r\n" + String.Join("\r\n", blocks);
        }
        public static string Part(Library lib, State s, int step) { return step == 1 ? Diagnosis(s) : step == 2 ? Differential(lib, s) : Plan(lib, s); }
        public static string All(Library lib, State s) { return String.Join("\r\n\r\n", new[] { Diagnosis(s), Differential(lib, s), Plan(lib, s) }); }
        public static string Signature(Library lib, State s) { return All(lib, s); }
        public static bool Pending(string text) { return text.Contains("[待补充") || text.Contains("[待核对"); }
        public static string AnalysisSkeleton(string kind)
        {
            return kind == "stroke" ? "结合患者[待补充：临床表现及本例检查证据]，暂考虑[待补充：初步诊断]，定位及分型考虑[待补充：定位、分型或尚待明确内容]。目前病情风险为[待补充：实际风险及观察重点]，后续拟完善[待补充：拟行检查及目的]，拟给予[待补充：本例治疗、护理及告知安排]，密切观察病情变化。" : "患者以[待补充：头晕特点及病程]为主要表现，结合[待补充：既往情况、查体及检查证据]，暂考虑[待补充：初步诊断或可能病因]，尚需排除[待补充：需要进一步鉴别的情况]。拟完善[待补充：检查或评估及目的]，拟给予[待补充：本例处理及观察重点]，密切关注病情变化。";
        }
    }
    public static class Storage
    {
        public static void Save(XDocument doc, string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)); using (XmlWriter w = XmlWriter.Create(path + ".tmp", new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize })) doc.Save(w); if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak", true); else File.Move(path + ".tmp", path); }
    }
    public static class PinyinLookup
    {
        private static Dictionary<char, string> map;
        public static string Convert(string text) { if (map == null) { map = new Dictionary<char, string>(); using (StreamReader r = new StreamReader(Program.Resource("pinyin.tsv"), Encoding.UTF8)) { string line; while ((line = r.ReadLine()) != null) if (line.Length > 2) map[line[0]] = line.Substring(2); } } map['椎'] = "zhui"; return String.Join(" ", text.Select(c => map.ContainsKey(c) ? map[c] : c.ToString().ToLowerInvariant())); }
    }
}
