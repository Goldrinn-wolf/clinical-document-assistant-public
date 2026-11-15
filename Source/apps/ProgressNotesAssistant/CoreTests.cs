using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ProgressNotesAssistant
{
    public static class CoreTests
    {
        private static readonly List<string> Results = new List<string>();
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("FAIL " + label); Results.Add("PASS " + label); }
        private static Library ReadLibrary() { using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("library.xml")) { if (stream == null) throw new InvalidOperationException("缺少嵌入的模板库。"); return Library.Load(stream); } }
        private static bool Rejected(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        private static Library ReadXml(string text) { using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(text))) return Library.Load(stream); }
        public static int Run(string directory)
        {
            Directory.CreateDirectory(directory); Results.Clear();
            try
            {
                Library library = ReadLibrary();
                Check(library.Exams.Count == 37 && library.Modules.Count == 8, "37 项检查及 7 个入口中的 8 份记录模板完整载入");
                Check(library.Exams.Sum(e => e.References.Count) == 19, "19 份检查参考正文仅作为手动参考载入");
                Check(library.GetExam("echo").References[0].ToString() == library.GetExam("echo").References[0].Title, "检查参考对象在选择框显示参考名称");
                Check(library.Exams.Count(e => e.Title == "动态心电图") == 1 && library.GetExam("holter").References.Count == 2, "两份动态心电图参考共用一个检查项目");
                Check(library.GetExam("genetic").References.Count(r => r.Group == "clopidogrel") == 2 && library.GetExam("genetic").References.Count(r => r.Group == "warfarin") == 2, "基因检测两种药物各保留两个互斥参考");
                State state = State.New(library);
                Check(state.CurrentModule == "day2" && state.Values.Count == 0 && state.Exams.Count == 0 && state.Previews.Count == 0, "新草稿不载入患者字段、检查示例或预览");
                Check(Generator.Auxiliary(library, state) == "" && Generator.Treatments(library, state) == "", "未选择检查时辅助检查与处理为空");
                string day2 = Generator.Record(library, state, "day2");
                Check(day2.Contains("食纳、二便、夜休等一般状况可") && day2.Contains("体温:36.2℃,脉搏:65次/分,呼吸:15次/分,血压:135/68mmHg"), "用户明确允许的默认一般状况与生命体征按原文回退");
                Check(day2.Contains("辅助检查：\r\n某某主任医师查房看过病人后示：\r\n") && !day2.Contains("主动脉反流"), "空白检查及处理入口不自动载入示例异常");
                Check(day2.EndsWith("余可继续当前调脂固斑、改善循环、活血、营养神经、止晕、降糖、降压等综合治疗，继观。"), "用户固定综合治疗原文保持不变");
                state.Set("day2", "diet", "食纳欠佳"); state.Set("day2", "vitals", "体温36.5℃，血压120/70mmHg");
                Check(Generator.Record(library, state, "day2").Contains("食纳欠佳、二便、夜休") && Generator.Record(library, state, "day2").Contains("查体：体温36.5℃，血压120/70mmHg。"), "标记字段仅替换对应输入位置");
                Check(Generator.Record(library, state, "daily").Contains("食纳、二便、夜休") && state.Get("daily", "vitals") == "", "不同记录模块的本例可编辑字段相互独立");
                state.Set("day2", "diet", "  "); Check(Generator.Record(library, state, "day2").Contains("食纳、二便、夜休"), "清空可回退字段恢复用户指定初始模板");
                Core.UpdateResult(state, "urine", true, ""); Core.UpdateResult(state, "stool", true, " \r\n ");
                Check(Generator.Auxiliary(library, state) == "粪便常规、尿常规无明显异常。", "勾选空白项目按库内顺序用顿号合并且统一正常结尾");
                Core.UpdateResult(state, "echo", true, "本例异常结果\n第二行");
                string auxiliary = Generator.Auxiliary(library, state);
                Check(auxiliary == "心脏彩超：本例异常结果\r\n第二行\r\n粪便常规、尿常规无明显异常。", "非空结果保留前缀和换行，正常空白项目置于末尾");
                Check(!auxiliary.Contains("心内结构未见明显异常"), "手工结果不会混入库内原例事实");
                Core.UpdateResult(state, "coagulation", false, "未选中的结果"); Check(!Generator.Auxiliary(library, state).Contains("未选中的结果"), "未勾选即使已保存正文也不输出检查事实");
                Check(Generator.Treatments(library, state).Contains("针对患者心脏彩超异常（本例异常结果\r\n第二行），予以[待填写：心脏彩超处理]处理。"), "非空检查建立处理项并保留完整原结果，空方案明确待填写");
                Check(!Generator.Treatments(library, state).Contains("尿常规") && !Generator.Treatments(library, state).Contains("凝血"), "正常空白项目与未选项目不建立处理正文");
                Core.UpdateTreatment(state, "echo", "临床人员手动填写的方案", true);
                Check(!Core.TreatmentNeedsReview(state.Exam("echo")) && Generator.Treatments(library, state).Contains("予以临床人员手动填写的方案处理。"), "处理方案只来源于手动输入并关联当前结果快照");
                Check(Generator.Record(library, state, "day2").Contains("临床人员手动填写的方案") && Generator.Record(library, state, "daily").Contains("临床人员手动填写的方案") && Generator.Record(library, state, "chief").Contains("临床人员手动填写的方案"), "同患者主任及日常记录共享检查结果与处理池");
                Core.UpdateResult(state, "echo", false, state.Exam("echo").Result);
                Check(!Generator.Auxiliary(library, state).Contains("心脏彩超") && !Generator.Treatments(library, state).Contains("心脏彩超") && state.Exam("echo").Treatment == "临床人员手动填写的方案", "取消检查停止输出并保留结果及人工处理");
                Core.UpdateResult(state, "echo", true, state.Exam("echo").Result);
                Check(Generator.Treatments(library, state).Contains("临床人员手动填写的方案") && !Core.TreatmentNeedsReview(state.Exam("echo")), "重新勾选同一结果恢复人工处理无需重复输入");
                Core.UpdateResult(state, "echo", true, "本例更新后的结果");
                Check(state.Exam("echo").Treatment == "临床人员手动填写的方案" && Core.TreatmentNeedsReview(state.Exam("echo")), "结果改变保留旧处理但标记需要核对");
                Check(Generator.Treatments(library, state).Contains("[待核对：结果变化后原处理需重新核对]") && !Generator.Treatments(library, state).Contains("临床人员手动填写的方案"), "待核对处理不作为已确认方案输出");
                Core.ConfirmTreatment(state, "echo"); Check(!Core.TreatmentNeedsReview(state.Exam("echo")) && Generator.Treatments(library, state).Contains("临床人员手动填写的方案"), "明确核对后可以复用原处理");
                Core.UpdateResult(state, "echo", true, "第三个结果"); Core.UpdateTreatment(state, "echo", "本次改写方案", true);
                Check(!Core.TreatmentNeedsReview(state.Exam("echo")) && state.Exam("echo").TreatmentForResult == "第三个结果", "改写处理时更新对应结果快照");
                Core.UpdateTreatment(state, "echo", "本次改写方案", false);
                Check(Generator.Treatments(library, state) == "" && Generator.Auxiliary(library, state).Contains("第三个结果"), "单独取消处理不取消检查结果，保存原处理文字");
                Core.UpdateTreatment(state, "echo", "本次改写方案", true);
                Core.UpdateResult(state, "bubble", true, "阴性");
                Check(Generator.Treatments(library, state).Contains("发泡实验异常（阴性）"), "异常处理池严格按用户非空规则，不擅自医学判断阴性结果");
                Core.UpdateTreatment(state, "bubble", "", false);
                Check(!Generator.Treatments(library, state).Contains("发泡实验"), "临床人员可关闭已记录文字结果的处理项");
                ExamDefinition gene = library.GetExam("genetic");
                Check(Generator.Pending(Generator.GeneReference(gene, "", "")), "基因参考未手选时不默认代谢型及用药建议");
                string geneA = Generator.GeneReference(gene, "genetic-r1", "");
                Check(geneA.Contains("氯吡格雷") && !geneA.Contains("华法林") && !geneA.Contains("中间代谢型") && !Generator.Pending(geneA), "只选氯吡格雷一种参考时不纳入另一种或另一药物");
                string geneB = Generator.GeneReference(gene, "", "genetic-r4");
                Check(geneB.Contains("华法林") && !geneB.Contains("氯吡格雷") && !geneB.Contains("快代谢 敏感型"), "只选华法林一种参考时不纳入另一类型");
                string both = Generator.GeneReference(gene, "genetic-r2", "genetic-r3");
                Check(both.Contains("中间代谢型") && both.Contains("快代谢 敏感型") && !both.Contains("正常代谢型") && !both.Contains("/中间敏感型"), "两药分别手选后仅合并选定类型正文");
                Check(Generator.Pending(Generator.GeneReference(gene, "genetic-r3", "genetic-r1")) && Generator.Pending(Generator.GeneReference(gene, "genetic-r1", "genetic-r2")), "错误药物组及同药两类型的选择被拒绝");
                Check(Generator.Pending(Generator.GeneReference(gene, "unknown", "")), "不存在的基因参考 ID 不载入任何结果");
                Check(library.Matches(library.GetExam("holter"), "动态 心电") && library.Matches(library.GetExam("holter"), "dongtaixindiantu") && library.Matches(library.GetExam("holter"), "dtxdt"), "检查支持中文、多关键词、全拼与首字母搜索");
                Check(library.Matches(library.GetExam("echo"), "主动脉反流") && !library.Matches(library.GetExam("echo"), "完全不存在的搜索"), "检查示例正文可搜索且无关查询不匹配");
                Check(library.Matches(library.GetModule("lp"), "yzcc") && Library.Key(" ＴＣＣＤ： ") == "tccd", "模块支持首字母搜索及全角英文规范化");
                State fresh = State.New(library);
                Check(Generator.Pending(Generator.Record(library, fresh, "postop")) && Generator.Record(library, fresh, "postop").Contains("[待填写：脑血管造影结果]"), "术后要求的症状、患者描述及造影结果不套用旧患者事实");
                fresh.Set("postop", "symptom", "头晕"); fresh.Set("postop", "patienttype", "本例描述"); fresh.Set("postop", "angio", "本例造影报告\n第二行");
                string postop = Generator.Record(library, fresh, "postop");
                Check(!Generator.Pending(postop) && postop.Contains("患者头晕症状较前好转") && postop.Contains("此次脑血管造影示本例造影报告\r\n第二行"), "术后输入框完整替换并保留造影报告换行");
                Check(postop.Contains("右侧腹股沟穿刺点敷料干燥，无渗血渗液") && postop.Contains("继续完善基底动脉高分辨、高分辨增强以及BPAS检查"), "术后用户未标记的固定观察与安排保持原文");
                Check(Generator.Pending(Generator.Record(library, fresh, "discharge")), "出院未填写好转症状时标记待填写");
                fresh.Set("discharge", "symptom", "眩晕"); fresh.Set("discharge", "day", "明日");
                Check(Generator.Record(library, fresh, "discharge").Contains("眩晕症状较前好转") && Generator.Record(library, fresh, "discharge").Contains("拟于明日带方案"), "出院症状与今日明日按实际输入生成");
                Check(Generator.Pending(Generator.Record(library, fresh, "preop")) && !Generator.Record(library, fresh, "preop").Contains("2024年"), "未整理的术前小结明确待完善，不套用源文示例患者日期");
                fresh.Set("lp", "pressure", "180"); fresh.Set("lp", "glucose", "5.6"); string lp = Generator.Record(library, fresh, "lp");
                Check(lp.Contains("测压力180mmH2O") && lp.Contains("术后即刻血糖： 5.6mmol/L") && lp.Contains("第4/5腰椎棘突间隙") && lp.Contains("去枕平卧6小时"), "腰穿仅修改两个标记输入项，部位术程及术后原文固定");
                fresh.Set("thrombolysis", "bp-before", "180/90"); fresh.Set("thrombolysis", "bp-after", "145/80"); string thrombolysis = Generator.Record(library, fresh, "thrombolysis");
                Check(!Generator.Pending(thrombolysis) && thrombolysis.Contains("15:10分开始") && thrombolysis.Contains("16:00停止") && thrombolysis.Contains("5mg阿替普酶") && thrombolysis.Contains("余45mg") && thrombolysis.Contains("NIHSS评分2分"), "溶栓输入血压与默认时间，剂量评分事件原文不改变");
                fresh.Set("thrombolysis", "start", "17:10"); fresh.Set("thrombolysis", "stop", "18:00"); fresh.Set("thrombolysis", "after30", "35"); fresh.Set("thrombolysis", "after50", "45");
                thrombolysis = Generator.Record(library, fresh, "thrombolysis");
                Check(!Generator.Pending(thrombolysis) && thrombolysis.Contains("17:10分开始") && thrombolysis.Contains("18:00停止") && thrombolysis.Contains("溶栓35分钟") && thrombolysis.Contains("溶栓后45分钟"), "溶栓绝对时间及有效事件相对分钟可改，文本忠实输入");
                fresh.Set("thrombolysis", "after30", "-1"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("非负的有效分钟数"), "症状发作相对分钟为负值时提示核对");
                fresh.Set("thrombolysis", "after30", "abc"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("非负的有效分钟数"), "症状发作相对分钟不是数字时提示核对");
                fresh.Set("thrombolysis", "after30", "35"); fresh.Set("thrombolysis", "after50", "-3"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("非负的有效分钟数"), "牙龈渗血相对分钟为负值时提示核对");
                fresh.Set("thrombolysis", "after50", "NaN"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("非负的有效分钟数"), "牙龈渗血相对分钟无效时提示核对");
                fresh.Set("thrombolysis", "after30", "46"); fresh.Set("thrombolysis", "after50", "45"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("症状再次发作时间不得晚于牙龈渗血时间"), "相对事件顺序颠倒时提示核对");
                fresh.Set("thrombolysis", "after30", "35"); fresh.Set("thrombolysis", "after50", "55"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("牙龈渗血相对分钟超过溶栓起止时长"), "牙龈渗血发生在停止溶栓后时提示核对且不自动改写输入");
                fresh.Set("thrombolysis", "after30", "35.5"); fresh.Set("thrombolysis", "after50", "50"); Check(!Generator.Pending(Generator.Record(library, fresh, "thrombolysis")), "有效非负小数分钟与正好停止时刻的事件允许输出");
                fresh.Set("thrombolysis", "after30", "35"); fresh.Set("thrombolysis", "after50", "45");
                fresh.Set("thrombolysis", "start", "25:10"); Check(Generator.Pending(Generator.Record(library, fresh, "thrombolysis")), "无效时钟格式提示待核对");
                fresh.Set("thrombolysis", "start", "23:40"); fresh.Set("thrombolysis", "stop", "00:30"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("是否跨日"), "纯时分停止早于开始不自动推断跨日");
                fresh.Set("thrombolysis", "start", "2026-10-14 23:40"); fresh.Set("thrombolysis", "stop", "2026-10-15 00:30"); Check(!Generator.Pending(Generator.Record(library, fresh, "thrombolysis")), "完整日期时间跨日且相对事件在起止时长内时允许输出");
                fresh.Set("thrombolysis", "after50", "51"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("牙龈渗血相对分钟超过溶栓起止时长"), "跨日起止时间也校验事件未超过实际时长"); fresh.Set("thrombolysis", "after50", "45");
                fresh.Set("thrombolysis", "stop", "2026-10-14 23:30"); Check(Generator.Pending(Generator.Record(library, fresh, "thrombolysis")), "完整日期时间停止早于开始时提示核对");
                fresh.Set("thrombolysis", "stop", "00:30"); Check(Generator.Record(library, fresh, "thrombolysis").Contains("统一填写"), "起止时间混用纯时分和日期时间时提示统一格式");
                fresh.Set("thrombolysis", "start", "2026/10/14 23:40"); fresh.Set("thrombolysis", "stop", "2026/10/15 0:30"); Check(!Generator.Pending(Generator.Record(library, fresh, "thrombolysis")), "日期斜杠及一位小时也可合法解析");
                state.CurrentModule = "chief"; state.FontSize = 19; state.Set("daily", "vitals", "中文<&>\r\n第二行\t制表符"); state.Previews["day2"] = "人工编辑正文\r\n尾行"; state.Generated["day2"] = day2; state.Edited.Add("day2"); state.Exam("genetic").ChoiceA = "genetic-r2"; state.Exam("genetic").ChoiceB = "genetic-r4";
                string draft = Path.Combine(directory, "core-draft.xml"); state.Save(draft); State loaded = State.Load(draft);
                Check(loaded.CurrentModule == "chief" && loaded.FontSize == 19 && loaded.Get("daily", "vitals") == state.Get("daily", "vitals"), "草稿恢复当前模块、字号及中文 XML 字符与换行输入");
                Check(loaded.Exam("echo").Result == "第三个结果" && loaded.Exam("echo").Treatment == "本次改写方案" && loaded.Exam("echo").TreatmentForResult == "第三个结果" && loaded.Exam("coagulation").Result == "未选中的结果" && !loaded.Exam("coagulation").Selected, "草稿保留结果、方案快照及撤勾暂存内容");
                Check(!loaded.Exam("bubble").TreatmentEnabled && loaded.Exam("genetic").ChoiceA == "genetic-r2" && loaded.Exam("genetic").ChoiceB == "genetic-r4", "草稿保留处理开关与两药参考选择");
                Check(loaded.Previews["day2"] == state.Previews["day2"] && loaded.Generated["day2"] == day2 && loaded.Edited.SetEquals(state.Edited), "草稿恢复人工预览及生成快照，不覆盖人工修改");
                state.Previews["day2"] = "第二次保存"; state.Save(draft); Check(State.Load(draft + ".bak").Previews["day2"] == loaded.Previews["day2"] && State.Load(draft).Previews["day2"] == "第二次保存", "原子替换后备份保留上一版且当前草稿为新版本");
                byte[] before = File.ReadAllBytes(draft); bool failed = Rejected(delegate { Storage.Save(new XDocument(new XElement("Draft", "不合法\0字符")), draft); });
                Check(failed && before.SequenceEqual(File.ReadAllBytes(draft)) && Directory.GetFiles(directory, "core-draft.xml.*.tmp").Length == 0, "保存失败不覆盖可用草稿且临时文件清理");
                Check(Rejected(delegate { ReadXml("<Library><Modules><Module id='x' title='a'/><Module id='x' title='b'/></Modules></Library>"); }), "重复模块 ID 被拒绝");
                Check(Rejected(delegate { ReadXml("<Library><Exams><Exam id='x' title='a'/><Exam id='x' title='b'/></Exams><Modules><Module id='m' title='m'/></Modules></Library>"); }), "重复检查 ID 被拒绝");
                Check(Rejected(delegate { ReadXml("<!DOCTYPE Library [<!ENTITY e SYSTEM 'file:///unused'>]><Library><Modules><Module id='m' title='m'><Body>&e;</Body></Module></Modules></Library>"); }), "库 XML 禁止外部实体读取");
                string duplicate = Path.Combine(directory, "invalid-draft.xml"); File.WriteAllText(duplicate, "<Draft><Value key='x'>a</Value><Value key='x'>b</Value></Draft>"); Check(Rejected(delegate { State.Load(duplicate); }), "草稿重复键拒绝而不静默覆盖");
                State nextPatient = State.New(library);
                Check(nextPatient.Values.Count == 0 && nextPatient.Exams.Count == 0 && nextPatient.Previews.Count == 0 && nextPatient.Generated.Count == 0 && nextPatient.Edited.Count == 0 && nextPatient.Exam("echo").Result == "" && nextPatient.Exam("echo").Treatment == "", "新患者清空全部共享事实、处理、临时字段及人工预览");
                Check(library.GetExam("echo").References[0].Text.Contains("主动脉反流") && library.GetModule("day2").Body.Contains("{{diet}}"), "新患者及本例编辑不修改通用参考库");
                string export = Path.Combine(directory, "core-export.txt"); File.WriteAllText(export, Generator.Record(library, loaded, "daily"), new UTF8Encoding(true)); Check(File.ReadAllText(export) == Generator.Record(library, loaded, "daily") && File.ReadAllBytes(export).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM 的 TXT 保留中文及完整生成正文");
                Check(Generator.Pending("[待填写：字段]") && Generator.Pending("[待核对：结果]") && Generator.Pending("[待完善：模板]") && !Generator.Pending("已完成本例人工输入"), "三类待处理标记均可供界面阻止未完成文本输出");
                Results.Add("TOTAL " + Results.Count + " core checks passed");
                File.WriteAllLines(Path.Combine(directory, "core-tests.txt"), Results, new UTF8Encoding(true)); return 0;
            }
            catch (Exception error) { Results.Add(error.ToString()); File.WriteAllLines(Path.Combine(directory, "core-tests.txt"), Results, new UTF8Encoding(true)); return 1; }
        }
    }
}
