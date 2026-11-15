using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ProgressNotesAssistant
{
    public class ReferenceText
    {
        public string Id = "", Title = "", Group = "", Text = "";
        public override string ToString() { return Title; }
    }
    public class ExamDefinition
    {
        public string Id = "", Title = "", Prefix = "", Kind = "standard", Category = "other";
        public bool DefaultSelected;
        public List<ReferenceText> References = new List<ReferenceText>();
    }
    public class FieldDefinition
    {
        public string Key = "", Label = "", Default = "", Edit = "input";
        public int Size = 40;
        public bool Required;
        public string[] Options = new string[0];
    }
    public class ModuleDefinition
    {
        public string Id = "", Title = "", Body = "";
        public bool Pending;
        public List<FieldDefinition> Fields = new List<FieldDefinition>();
    }
    public class Library
    {
        public List<ExamDefinition> Exams = new List<ExamDefinition>();
        public List<ModuleDefinition> Modules = new List<ModuleDefinition>();

        public static Library Load(Stream stream)
        {
            XDocument doc = Storage.Read(stream);
            if (doc.Root == null || doc.Root.Name != "Library") throw new InvalidDataException("模板库格式无效。");
            Library library = new Library();
            XElement exams = doc.Root.Element("Exams"), modules = doc.Root.Element("Modules");
            if (exams != null) foreach (XElement item in exams.Elements("Exam"))
            {
                ExamDefinition exam = new ExamDefinition { Id = Attr(item, "id"), Title = Attr(item, "title"), Prefix = Attr(item, "prefix"), Kind = Attr(item, "kind", "standard"), Category = Attr(item, "category", "other"), DefaultSelected = Bool(item, "defaultSelected", false) };
                foreach (XElement reference in item.Elements("Reference")) exam.References.Add(new ReferenceText { Id = Attr(reference, "id"), Title = Attr(reference, "title"), Group = Attr(reference, "group"), Text = Generator.Normalize((string)reference.Element("Text") ?? "") });
                if (String.IsNullOrWhiteSpace(exam.Id) || String.IsNullOrWhiteSpace(exam.Title) || !new[] { "standard", "gene" }.Contains(exam.Kind) || exam.References.Any(r => String.IsNullOrWhiteSpace(r.Id) || String.IsNullOrWhiteSpace(r.Title)) || exam.References.Select(r => r.Id).Distinct().Count() != exam.References.Count) throw new InvalidDataException("检查模板字段无效或参考 ID 重复。");
                library.Exams.Add(exam);
            }
            if (modules != null) foreach (XElement item in modules.Elements("Module"))
            {
                ModuleDefinition module = new ModuleDefinition { Id = Attr(item, "id"), Title = Attr(item, "title"), Body = Generator.Normalize((string)item.Element("Body") ?? ""), Pending = Bool(item, "pending", false) };
                XElement fields = item.Element("Fields");
                if (fields != null) foreach (XElement field in fields.Elements("Field"))
                {
                    int size; if (!Int32.TryParse(Attr(field, "size", "40"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 20 || size > 1000) throw new InvalidDataException("输入框大小无效。");
                    FieldDefinition definition = new FieldDefinition { Key = Attr(field, "key"), Label = Attr(field, "label"), Default = Generator.Normalize(Attr(field, "default")), Size = size, Required = Bool(field, "required", false), Edit = Attr(field, "edit", "input"), Options = Attr(field, "options").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries) };
                    if (String.IsNullOrWhiteSpace(definition.Key) || String.IsNullOrWhiteSpace(definition.Label) || !new[] { "double", "input", "large", "time" }.Contains(definition.Edit)) throw new InvalidDataException("文书字段无效。");
                    module.Fields.Add(definition);
                }
                if (String.IsNullOrWhiteSpace(module.Id) || String.IsNullOrWhiteSpace(module.Title) || module.Fields.Select(f => f.Key).Distinct().Count() != module.Fields.Count) throw new InvalidDataException("模块字段无效或字段重复。");
                library.Modules.Add(module);
            }
            if (library.Modules.Count == 0 || library.Modules.Select(m => m.Id).Distinct().Count() != library.Modules.Count || library.Exams.Select(e => e.Id).Distinct().Count() != library.Exams.Count) throw new InvalidDataException("模块为空或模板 ID 重复。");
            return library;
        }
        private static string Attr(XElement item, string name, string fallback = "") { return (string)item.Attribute(name) ?? fallback; }
        private static bool Bool(XElement item, string name, bool fallback) { string value = (string)item.Attribute(name); if (value == null) return fallback; bool parsed; if (!Boolean.TryParse(value, out parsed)) throw new InvalidDataException("布尔字段无效：" + name); return parsed; }
        public ExamDefinition GetExam(string id) { return Exams.First(e => e.Id == id); }
        public ModuleDefinition GetModule(string id) { return Modules.First(m => m.Id == id); }
        public static string Key(string text) { return new string((text ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray()); }
        public bool Matches(ExamDefinition exam, string query) { return MatchesText(exam.Title + " " + exam.Prefix + " " + String.Join(" ", exam.References.Select(r => r.Title + " " + r.Text)), query); }
        public bool Matches(ModuleDefinition module, string query) { return MatchesText(module.Title + " " + module.Body, query); }
        public static bool MatchesText(string text, string query)
        {
            string plain = Key(text), pinyin = PinyinLookup.Convert(text), full = Key(pinyin), initials = Key(String.Concat(pinyin.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Substring(0, 1))));
            return (query ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).All(q => plain.Contains(Key(q)) || full.Contains(Key(q)) || initials.Contains(Key(q)));
        }
    }
    public class ExamState
    {
        public bool Selected, TreatmentEnabled = true;
        public string Result = "", Treatment = "", TreatmentForResult = "", ChoiceA = "", ChoiceB = "";
    }
    public class State
    {
        public string CurrentModule = "day2";
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public Dictionary<string, ExamState> Exams = new Dictionary<string, ExamState>();
        public Dictionary<string, string> Previews = new Dictionary<string, string>();
        public Dictionary<string, string> Generated = new Dictionary<string, string>();
        public HashSet<string> Edited = new HashSet<string>();
        public HashSet<string> ExamCategories = new HashSet<string>();
        public double FontSize = 15;
        public static State New(Library library) { State state = new State(); if (!library.Modules.Any(m => m.Id == state.CurrentModule)) state.CurrentModule = library.Modules[0].Id; return state; }
        public string Get(string module, string key) { string value; return Values.TryGetValue(module + "." + key, out value) ? value : ""; }
        public void Set(string module, string key, string value) { Values[module + "." + key] = value ?? ""; }
        public ExamState Exam(string id) { ExamState value; if (!Exams.TryGetValue(id, out value)) { value = new ExamState(); Exams.Add(id, value); } return value; }
        public static State Load(string path)
        {
            XDocument doc; using (Stream stream = File.OpenRead(path)) doc = Storage.Read(stream);
            if (doc.Root == null || doc.Root.Name != "Draft") throw new InvalidDataException("草稿格式无效。");
            State state = new State();
            state.CurrentModule = (string)doc.Root.Element("CurrentModule") ?? "day2";
            double fontSize; if (Double.TryParse((string)doc.Root.Element("FontSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize) && !Double.IsNaN(fontSize) && !Double.IsInfinity(fontSize)) state.FontSize = Math.Max(12, Math.Min(24, fontSize));
            ReadMap(doc.Root, "Value", "key", state.Values);
            ReadMap(doc.Root, "Preview", "module", state.Previews);
            ReadMap(doc.Root, "Generated", "module", state.Generated);
            foreach (XElement item in doc.Root.Elements("Edited")) state.Edited.Add(item.Value);
            foreach (XElement item in doc.Root.Elements("ExamCategory")) state.ExamCategories.Add(item.Value);
            foreach (XElement item in doc.Root.Elements("Exam"))
            {
                string id = (string)item.Attribute("id");
                if (String.IsNullOrWhiteSpace(id) || state.Exams.ContainsKey(id)) throw new InvalidDataException("草稿检查 ID 为空或重复。");
                state.Exams.Add(id, new ExamState { Selected = ReadBool(item, "selected", false), TreatmentEnabled = ReadBool(item, "treatmentEnabled", true), Result = (string)item.Element("Result") ?? "", Treatment = (string)item.Element("Treatment") ?? "", TreatmentForResult = (string)item.Element("TreatmentForResult") ?? "", ChoiceA = (string)item.Element("ChoiceA") ?? "", ChoiceB = (string)item.Element("ChoiceB") ?? "" });
            }
            return state;
        }
        private static bool ReadBool(XElement item, string name, bool fallback) { string text = (string)item.Attribute(name); if (text == null) return fallback; bool parsed; if (!Boolean.TryParse(text, out parsed)) throw new InvalidDataException("草稿开关无效。"); return parsed; }
        private static void ReadMap(XElement root, string element, string keyName, Dictionary<string, string> map)
        {
            foreach (XElement item in root.Elements(element)) { string key = (string)item.Attribute(keyName); if (String.IsNullOrWhiteSpace(key) || map.ContainsKey(key)) throw new InvalidDataException("草稿键为空或重复。"); map.Add(key, item.Value); }
        }
        public void Save(string path)
        {
            XElement root = new XElement("Draft", new XAttribute("version", "1"), new XElement("CurrentModule", CurrentModule ?? "day2"), new XElement("FontSize", FontSize.ToString("R", CultureInfo.InvariantCulture)));
            foreach (KeyValuePair<string, string> item in Values.OrderBy(v => v.Key)) root.Add(new XElement("Value", new XAttribute("key", item.Key), item.Value ?? ""));
            foreach (KeyValuePair<string, ExamState> item in Exams.OrderBy(e => e.Key))
            {
                ExamState exam = item.Value;
                root.Add(new XElement("Exam", new XAttribute("id", item.Key), new XAttribute("selected", exam.Selected), new XAttribute("treatmentEnabled", exam.TreatmentEnabled), new XElement("Result", exam.Result ?? ""), new XElement("Treatment", exam.Treatment ?? ""), new XElement("TreatmentForResult", exam.TreatmentForResult ?? ""), new XElement("ChoiceA", exam.ChoiceA ?? ""), new XElement("ChoiceB", exam.ChoiceB ?? "")));
            }
            foreach (KeyValuePair<string, string> item in Previews.OrderBy(v => v.Key)) root.Add(new XElement("Preview", new XAttribute("module", item.Key), item.Value ?? ""));
            foreach (KeyValuePair<string, string> item in Generated.OrderBy(v => v.Key)) root.Add(new XElement("Generated", new XAttribute("module", item.Key), item.Value ?? ""));
            foreach (string module in Edited.OrderBy(v => v)) root.Add(new XElement("Edited", module));
            foreach (string category in ExamCategories.OrderBy(v => v)) root.Add(new XElement("ExamCategory", category));
            Storage.Save(new XDocument(root), path);
        }
    }
    public static class Core
    {
        public static void UpdateResult(State state, string id, bool selected, string result) { ExamState exam = state.Exam(id); exam.Selected = selected; exam.Result = result ?? ""; }
        public static void UpdateTreatment(State state, string id, string text, bool enabled) { ExamState exam = state.Exam(id); exam.Treatment = text ?? ""; exam.TreatmentEnabled = enabled; exam.TreatmentForResult = exam.Result ?? ""; }
        public static void ConfirmTreatment(State state, string id) { ExamState exam = state.Exam(id); exam.TreatmentForResult = exam.Result ?? ""; }
        public static bool TreatmentNeedsReview(ExamState exam) { return !String.IsNullOrWhiteSpace(exam.Treatment) && !String.Equals(Generator.Normalize(exam.TreatmentForResult).Trim(), Generator.Normalize(exam.Result).Trim(), StringComparison.Ordinal); }
    }
    public static class Generator
    {
        private static readonly Regex Placeholder = new Regex(@"\{\{([A-Za-z0-9_.-]+)\}\}", RegexOptions.Compiled);
        public static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
        public static string Auxiliary(Library library, State state)
        {
            List<string> blocks = new List<string>(), normal = new List<string>();
            foreach (ExamDefinition definition in library.Exams)
            {
                ExamState exam = state.Exam(definition.Id); if (!exam.Selected) continue;
                if (String.IsNullOrWhiteSpace(exam.Result)) normal.Add(definition.Title);
                else { string prefix = definition.Prefix ?? ""; if (String.IsNullOrWhiteSpace(prefix) || !prefix.Contains(definition.Title)) prefix = definition.Title + "："; blocks.Add(prefix + Normalize(exam.Result.Trim())); }
            }
            if (normal.Count > 0) blocks.Add(String.Join("、", normal) + "无明显异常。");
            return String.Join("\r\n", blocks);
        }
        public static string Treatments(Library library, State state)
        {
            List<string> blocks = new List<string>();
            foreach (ExamDefinition definition in library.Exams)
            {
                ExamState exam = state.Exam(definition.Id);
                if (!exam.Selected || String.IsNullOrWhiteSpace(exam.Result) || !exam.TreatmentEnabled) continue;
                string treatment = String.IsNullOrWhiteSpace(exam.Treatment) ? "[待填写：" + definition.Title + "处理]" : Core.TreatmentNeedsReview(exam) ? "[待核对：结果变化后原处理需重新核对]" : Normalize(exam.Treatment.Trim());
                blocks.Add("针对患者" + definition.Title + "异常（" + Normalize(exam.Result.Trim()) + "），予以" + treatment + "处理。");
            }
            return String.Join("\r\n", blocks);
        }
        public static string GeneReference(ExamDefinition definition, string choiceA, string choiceB)
        {
            if (definition.Kind != "gene") throw new ArgumentException("该检查不是基因检测参考。");
            List<ReferenceText> selected = new List<ReferenceText>();
            string[] choices = new[] { choiceA, choiceB }, groups = new[] { "clopidogrel", "warfarin" };
            for (int index = 0; index < choices.Length; index++)
            {
                string id = choices[index]; if (String.IsNullOrWhiteSpace(id)) continue;
                ReferenceText reference = definition.References.FirstOrDefault(r => r.Id == id);
                if (reference == null || reference.Group != groups[index]) return "[待填写：所属药物组的有效基因检测参考选择]";
                if (selected.Any(r => r.Id == reference.Id || (!String.IsNullOrWhiteSpace(reference.Group) && r.Group == reference.Group))) return "[待核对：同一种药物只能选择一种基因检测参考]";
                selected.Add(reference);
            }
            if (selected.Count == 0) return "[待填写：手动选择基因检测参考]";
            return String.Join("\r\n", selected.Select(r => Normalize(r.Text)));
        }
        public static string FieldValue(State state, string module, FieldDefinition field)
        {
            string value = state.Get(module, field.Key);
            if (String.IsNullOrWhiteSpace(value)) value = field.Default;
            return String.IsNullOrWhiteSpace(value) && field.Required ? "[待填写：" + field.Label + "]" : Normalize(value ?? "");
        }
        public static string Record(Library library, State state, string moduleId)
        {
            ModuleDefinition module = library.GetModule(moduleId);
            if (module.Pending) return "[待完善：" + module.Title + "模板，尚未提供完整格式]";
            string record = Placeholder.Replace(module.Body, delegate(Match match)
            {
                string key = match.Groups[1].Value;
                if (key == "EXAMS") return Auxiliary(library, state);
                if (key == "HANDLING") return Treatments(library, state);
                FieldDefinition field = module.Fields.FirstOrDefault(f => f.Key == key);
                return field == null ? "[待填写：" + key + "]" : FieldValue(state, moduleId, field);
            });
            List<string> warnings = TimeWarnings(module, state);
            if (warnings.Count > 0) record += "\r\n" + String.Join("\r\n", warnings);
            return Normalize(record);
        }
        public static List<string> TimeWarnings(ModuleDefinition module, State state)
        {
            List<string> warnings = new List<string>(); Dictionary<string, DateTime> values = new Dictionary<string, DateTime>(); Dictionary<string, bool> dated = new Dictionary<string, bool>();
            foreach (FieldDefinition field in module.Fields.Where(f => f.Edit == "time" || (module.Id == "thrombolysis" && (f.Key == "start" || f.Key == "stop"))))
            {
                string text = FieldValue(state, module.Id, field).Trim(); if (String.IsNullOrWhiteSpace(text) || Pending(text)) continue;
                text = text.Replace('：', ':'); DateTime parsed;
                bool withDate = DateTime.TryParseExact(text, new[] { "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm", "yyyy/MM/dd H:mm", "yyyy/MM/dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
                bool valid = withDate || DateTime.TryParseExact(text, new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out parsed);
                if (!valid) warnings.Add("[待核对：" + field.Label + "应填写有效的 HH:mm 或 yyyy-MM-dd HH:mm 时间]");
                else { values[field.Key] = parsed; dated[field.Key] = withDate; }
            }
            DateTime start, stop;
            if (values.TryGetValue("start", out start) && values.TryGetValue("stop", out stop))
            {
                if (dated["start"] != dated["stop"]) warnings.Add("[待核对：溶栓起止时间请统一填写时分或完整日期时间]");
                else if (stop < start) warnings.Add("[待核对：溶栓停止时间早于开始时间，请核对起止时间及是否跨日]");
            }
            if (module.Id == "thrombolysis")
            {
                Dictionary<string, decimal> relative = new Dictionary<string, decimal>();
                foreach (FieldDefinition field in module.Fields.Where(f => f.Key == "after30" || f.Key == "after50"))
                {
                    string text = FieldValue(state, module.Id, field).Trim(); if (Pending(text)) continue;
                    decimal minutes;
                    if (!Decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out minutes) || minutes < 0) warnings.Add("[待核对：" + field.Label + "应填写非负的有效分钟数]");
                    else relative[field.Key] = minutes;
                }
                decimal after30, after50;
                if (relative.TryGetValue("after30", out after30) && relative.TryGetValue("after50", out after50) && after30 > after50) warnings.Add("[待核对：症状再次发作时间不得晚于牙龈渗血时间]");
                if (relative.TryGetValue("after50", out after50) && values.TryGetValue("start", out start) && values.TryGetValue("stop", out stop) && dated["start"] == dated["stop"] && stop >= start && after50 > (decimal)(stop - start).TotalMinutes) warnings.Add("[待核对：牙龈渗血相对分钟超过溶栓起止时长]");
            }
            return warnings;
        }
        public static bool Pending(string text) { return (text ?? "").Contains("[待填写") || (text ?? "").Contains("[待核对") || (text ?? "").Contains("[待完善"); }
    }
    public static class Storage
    {
        public static XDocument Read(Stream stream)
        {
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000000, CloseInput = false };
            using (XmlReader reader = XmlReader.Create(stream, settings)) return XDocument.Load(reader, LoadOptions.None);
        }
        public static void Save(XDocument document, string path)
        {
            string fullPath = Path.GetFullPath(path), directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize, CloseOutput = false })) document.Save(writer);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, fullPath + ".bak", true);
                else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static class PinyinLookup
    {
        private static readonly object Sync = new object();
        private static Dictionary<char, string> map;
        public static string Convert(string text)
        {
            if (map == null) lock (Sync) if (map == null)
            {
                Dictionary<char, string> loaded = new Dictionary<char, string>();
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("pinyin.tsv"))
                    if (stream != null) using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line; while ((line = reader.ReadLine()) != null) if (line.Length > 2) loaded[line[0]] = line.Substring(2);
                    }
                // In the supplied clinical vocabulary 椎 refers to spinal vertebrae,
                // rather than the dictionary's first pronunciation (chui).
                loaded['椎'] = "zhui";
                map = loaded;
            }
            return String.Join(" ", (text ?? "").Select(c => map.ContainsKey(c) ? map[c] : c.ToString().ToLowerInvariant()));
        }
    }
}
