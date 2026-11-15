using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfflineLab
{
    public class FormatOptions
    {
        public bool RenameLiver = true, IncludeTime;
        public string CombinedName = "肝肾功血糖血脂心肌酶+HYC";
    }
    public class LabRecord
    {
        public string RawTime { get; set; }
        public string SourceTitle { get; set; }
        public string Title { get; set; }
        public string Result { get; set; }
        public int Line { get; set; }
        public bool Empty { get { return String.IsNullOrWhiteSpace(Result); } }
        public string Status { get { return Empty ? "空结果 → 末尾汇总" : "有文字 → 前部保留"; } }
    }
    public class FormatResult
    {
        public List<LabRecord> Records = new List<LabRecord>();
        public List<string> Issues = new List<string>();
        public string Output = "";
        public int ItemCount;
        public bool Ready { get { return Records.Count > 0 && Issues.Count == 0; } }
    }
    public static class Formatter
    {
        private static readonly Regex Prefix = new Regex(@"^(?<stamp>\d[\d:：./\-Tt年月日时分秒]*(?:[ \t]+\d[\d:：./\-Tt年月日时分秒]*)*)[ \t]+(?<rest>.+)$");
        private static readonly Regex JoinedPrefix = new Regex(@"^(?<stamp>\d[\d:：./\-Tt年月日时分秒]*[\d日时分秒])(?<rest>[\p{L}].*)$");
        private static readonly Regex NumericOnly = new Regex(@"^\d[\d \t:：./\-Tt年月日时分秒]+$");
        private static readonly Regex ReportName = new Regex(@"血细胞|血常规|尿常规|粪便|大便|血糖|血脂|肝功|肾功|凝血|甲功|甲状腺|电解质|同型|(?i:HCY|HYC|HIV|ANCA|BNP|CRP)|抗体|血沉|风湿|糖化|心肌|肌钙|二聚体|铁蛋白|贫血|肝炎|乙肝|梅毒|尿酸|肌红|降钙素|C反应|氯吡格雷|华法林");
        private static readonly Regex Analyte = new Regex(@"^(白细胞|红细胞|粒细胞|中性粒|淋巴|单核|嗜酸|嗜碱|血红蛋白|血小板|总胆红素|直接胆红素|间接胆红素|丙氨酸|天门冬|谷丙|谷草|参考|备注|说明|提示|结果|比值|单位)");
        private static readonly Regex Administrative = new Regex(@"^(姓名|性别|年龄|患者|病区|床号|住院号|就诊号|报告时间|检验日期|申请时间|审核人|检验人|条码号|备注|说明|参考范围|参考值|标本|报告人|检验项目|结果|提示)$");
        public static string OneLine(string text)
        {
            return Regex.Replace((text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' ').Replace('\u2029', ' '), @"\s+", " ").Trim();
        }
        public static bool IsLiver(string title)
        {
            string key = Regex.Replace(title ?? "", @"[\s（）()\[\]【】]", "");
            return Regex.IsMatch(key, @"^(肝功能|肝功)(检查|常规)?(\d+项)?$");
        }
        private static string Body(string value) { return (value ?? "").Trim().Trim('，', ',', ';', '；', '|', '。').Trim(); }
        private static bool ValidTitle(string title) { return title.Length <= 120 && Regex.IsMatch(title, @"\p{L}") && !Regex.IsMatch(title, @"[↑↓<>＝=]"); }
        private static bool SplitHeader(string line, out string stamp, out string title, out string result)
        {
            stamp = ""; title = ""; result = ""; string rest = line.Trim();
            Match prefix = Prefix.Match(rest); if (!prefix.Success) prefix = JoinedPrefix.Match(rest);
            if (prefix.Success && prefix.Groups["stamp"].Value.Count(Char.IsDigit) >= 6) { stamp = OneLine(prefix.Groups["stamp"].Value); rest = prefix.Groups["rest"].Value.Trim(); }
            int colon = rest.IndexOfAny(new[] { ':', '：' }); if (colon < 1) return false;
            title = OneLine(rest.Substring(0, colon)); result = Body(rest.Substring(colon + 1));
            return ValidTitle(title);
        }
        public static FormatResult Parse(string input, FormatOptions options)
        {
            FormatResult parsed = new FormatResult();
            if (options == null) options = new FormatOptions();
            if (options.RenameLiver && (String.IsNullOrWhiteSpace(options.CombinedName) || options.CombinedName.IndexOfAny(new[] { '\r', '\n', '：', ':' }) >= 0)) parsed.Issues.Add("组合表头请填写单行项目名称，不含冒号。");
            string text = (input ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n').Replace('\u00a0', ' ').Replace("\u200b", "");
            string[] lines = text.Split('\n'); LabRecord current = null; bool separated = true; string pendingTime = ""; int pendingLine = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim(); if (line.Length == 0) { separated = true; continue; }
                string stamp, title, result;
                if (SplitHeader(line, out stamp, out title, out result))
                {
                    if (Administrative.IsMatch(title) && (current == null || separated || stamp.Length > 0)) { parsed.Issues.Add("第" + (i + 1) + "行是人员/报告信息，请移除后整理：" + line); separated = true; continue; }
                    bool header = stamp.Length > 0 || pendingTime.Length > 0 || current == null || separated || ReportName.IsMatch(title);
                    if (!header)
                    {
                        current.Result = Body(current.Result + (current.Result.Length > 0 ? "，" : "") + line);
                        if (!Analyte.IsMatch(title)) parsed.Issues.Add("第" + (i + 1) + "行不能确定是新项目还是结果续行，请补时间或用空行分开：" + line);
                        continue;
                    }
                    if (stamp.Length == 0 && pendingTime.Length > 0) stamp = pendingTime;
                    else if (pendingTime.Length > 0) parsed.Issues.Add("第" + pendingLine + "行时间没有对应项目：" + pendingTime);
                    pendingTime = "";
                    current = new LabRecord { RawTime = stamp, SourceTitle = title, Title = options.RenameLiver && IsLiver(title) ? OneLine(options.CombinedName) : title, Result = result, Line = i + 1 };
                    parsed.Records.Add(current); separated = false; continue;
                }
                if (NumericOnly.IsMatch(line) && line.Count(Char.IsDigit) >= 6 && (current == null || separated))
                {
                    if (pendingTime.Length > 0) parsed.Issues.Add("第" + pendingLine + "行时间没有对应项目：" + pendingTime);
                    pendingTime = OneLine(line); pendingLine = i + 1; continue;
                }
                bool looksLikeNew = (line.Count(Char.IsDigit) >= 6 && Char.IsDigit(line[0]) && Regex.IsMatch(line, @"\p{L}")) || (line.Length <= 80 && ReportName.IsMatch(line) && !Regex.IsMatch(line, @"[↑↓：:，,。0-9]"));
                if (current == null || separated || looksLikeNew || pendingTime.Length > 0) parsed.Issues.Add("第" + (i + 1) + "行没有识别到完整项目名和冒号：" + line);
                else current.Result = Body(current.Result + (current.Result.Length > 0 ? "，" : "") + line);
                separated = false;
            }
            if (pendingTime.Length > 0) parsed.Issues.Add("第" + pendingLine + "行时间没有对应项目：" + pendingTime);
            if (parsed.Records.Count == 0 && parsed.Issues.Count == 0) parsed.Issues.Add("请粘贴包含“项目名：结果”的检验文本。");
            foreach (var group in parsed.Records.GroupBy(r => r.Title))
            {
                if (!group.Any(r => r.Empty) || !group.Any(r => !r.Empty)) continue;
                if (!options.IncludeTime) parsed.Issues.Add("“" + group.Key + "”同时存在空结果和有字结果，请勾选保留时间并核对是否为不同次检查。");
                else if (group.Any(r => r.RawTime.Length == 0) || group.GroupBy(r => r.RawTime).Any(g => g.Any(r => r.Empty) && g.Any(r => !r.Empty))) parsed.Issues.Add("“" + group.Key + "”同一时间或缺少时间时出现空结果与有字结果并存，请核对原文。");
            }
            List<string> blocks = new List<string>();
            foreach (LabRecord record in parsed.Records.Where(r => !r.Empty)) blocks.Add(Label(record, options) + "：" + OneLine(record.Result));
            if (options.IncludeTime)
            {
                foreach (var group in parsed.Records.Where(r => r.Empty).GroupBy(r => r.RawTime))
                    blocks.Add((group.Key.Length > 0 ? group.Key + " " : "") + String.Join("、", StableDistinct(group.Select(r => r.Title))) + "无明显异常");
            }
            else
            {
                var normal = StableDistinct(parsed.Records.Where(r => r.Empty).Select(r => r.Title));
                if (normal.Count > 0) blocks.Add(String.Join("、", normal) + "无明显异常");
            }
            parsed.ItemCount = blocks.Count;
            foreach (string issue in parsed.Issues) blocks.Add("[待核对：" + OneLine(issue) + "]");
            parsed.Output = blocks.Count == 0 ? "" : String.Join("；", blocks.Select((b, i) => (i + 1) + "、" + b)) + "。";
            return parsed;
        }
        private static string Label(LabRecord record, FormatOptions options) { return options.IncludeTime && record.RawTime.Length > 0 ? record.RawTime + " " + record.Title : record.Title; }
        private static List<string> StableDistinct(IEnumerable<string> source) { List<string> result = new List<string>(); HashSet<string> seen = new HashSet<string>(); foreach (string value in source) if (seen.Add(value)) result.Add(value); return result; }
    }
}
