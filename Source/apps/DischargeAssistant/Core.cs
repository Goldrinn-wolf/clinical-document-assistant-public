using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml;

namespace DischargeAssistant
{
    public class Drug
    {
        public string Id, Name, Text, Pinyin, Initials, Section;
        public int Order;
        public Category Category;
        public List<string> Notes = new List<string>();
        public List<string> Warnings = new List<string>();
    }
    public class Category
    {
        public string Id;
        public string Title { get; set; }
        public int Order;
        public List<string> Notes = new List<string>();
        public List<Drug> Drugs = new List<Drug>();
    }
    public class Library
    {
        public string Intro;
        public List<Category> Categories = new List<Category>();
        public List<Drug> Drugs { get { return Categories.SelectMany(c => c.Drugs).ToList(); } }
        public static Library Load(Stream stream, bool allowEmpty = false)
        {
            XDocument x = XDocument.Load(stream);
            Library lib = new Library { Intro = (string)x.Root.Element("Intro") };
            HashSet<string> ids = new HashSet<string>();
            foreach (XElement element in x.Root.Element("Categories").Elements("Category"))
            {
                Category c = new Category { Id = (string)element.Attribute("id"), Title = (string)element.Attribute("title"), Order = (int)element.Attribute("order") };
                if (String.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) throw new InvalidDataException("分类ID缺失或重复");
                c.Notes = element.Elements("Note").Select(n => n.Value).ToList();
                foreach (XElement d in element.Elements("Drug"))
                {
                    Drug drug = new Drug { Id = (string)d.Attribute("id"), Name = (string)d.Attribute("name"), Order = (int)d.Attribute("order"), Text = (string)d.Element("Text"), Pinyin = (string)d.Attribute("pinyin"), Initials = (string)d.Attribute("initials"), Section = (string)d.Attribute("section"), Category = c };
                    if (String.IsNullOrWhiteSpace(drug.Id) || !ids.Add(drug.Id) || String.IsNullOrWhiteSpace(drug.Text)) throw new InvalidDataException("药物ID重复或原文缺失");
                    drug.Notes = d.Elements("Note").Select(n => n.Value).ToList();
                    drug.Warnings = d.Elements("Warning").Select(n => n.Value).ToList();
                    c.Drugs.Add(drug);
                }
                c.Drugs = c.Drugs.OrderBy(d => d.Order).ToList();
                lib.Categories.Add(c);
            }
            lib.Categories = lib.Categories.OrderBy(c => c.Order).ToList();
            if (String.IsNullOrWhiteSpace(lib.Intro) || (!allowEmpty && lib.Drugs.Count == 0)) throw new InvalidDataException("药库为空或复诊说明缺失");
            return lib;
        }
        public static string SearchKey(string text)
        {
            return new string((text ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
        public bool Matches(Drug d, string query)
        {
            if (String.IsNullOrWhiteSpace(query)) return true;
            return query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).All(q => {
                string key = SearchKey(q);
                return SearchKey(d.Name + d.Text + d.Category.Title).Contains(key) || SearchKey(d.Pinyin).Contains(key) || SearchKey(d.Initials).Contains(key);
            });
        }
        public string Generate(IEnumerable<string> selected)
        {
            HashSet<string> ids = new HashSet<string>(selected);
            List<string> blocks = new List<string> { Intro.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n") };
            int number = 0;
            foreach (Category c in Categories)
            {
                List<Drug> drugs = c.Drugs.Where(d => ids.Contains(d.Id)).ToList();
                if (drugs.Count == 0) continue;
                List<string> lines = new List<string> { (++number) + "." + c.Title + (c.Title.Contains("：") ? "" : "：") };
                foreach (Drug d in drugs)
                {
                    lines.Add(d.Text);
                    lines.AddRange(d.Notes.Distinct());
                }
                lines.AddRange(c.Notes.Distinct());
                blocks.Add(String.Join("\r\n", lines));
            }
            return String.Join("\r\n\r\n", blocks);
        }
    }
    public class Draft
    {
        public HashSet<string> Selected = new HashSet<string>();
        public string Preview = "", Intro = "", CategoryId = "";
        public double FontSize = 15;
        public bool Stale = false, Edited = false;
        public string GeneratedIds = "";
        public static Draft Load(string path)
        {
            XDocument x = XDocument.Load(path);
            return new Draft {
                Selected = new HashSet<string>(x.Root.Elements("Selected").Select(e => e.Value)),
                Preview = (string)x.Root.Element("Preview") ?? "", Intro = (string)x.Root.Element("Intro") ?? "",
                CategoryId = (string)x.Root.Element("Category") ?? "",
                FontSize = Math.Max(12, Math.Min(24, (double?)x.Root.Element("FontSize") ?? 15)),
                Stale = (bool?)x.Root.Element("Stale") ?? false, Edited = (bool?)x.Root.Element("Edited") ?? false,
                GeneratedIds = (string)x.Root.Element("GeneratedIds") ?? ""
            };
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XDocument x = new XDocument(new XElement("Draft", new XAttribute("version", "1"),
                new XElement("Preview", Preview), new XElement("Intro", Intro), new XElement("Category", CategoryId),
                new XElement("FontSize", FontSize), new XElement("Stale", Stale), new XElement("Edited", Edited), new XElement("GeneratedIds", GeneratedIds),
                Selected.OrderBy(id => id).Select(id => new XElement("Selected", id))));
            string temp = path + ".tmp";
            using (XmlWriter writer = XmlWriter.Create(temp, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize })) x.Save(writer);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
            else File.Move(temp, path);
        }
    }
}
