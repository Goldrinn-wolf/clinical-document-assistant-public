"""Validate generic public-edition defaults without embedding private identifiers in tests."""
from pathlib import Path
import re,xml.etree.ElementTree as ET,json
repo=Path(__file__).resolve().parents[1]
source=repo/'Source'
defaults=ET.parse(source/'apps/DischargeAssistant/followup-defaults.xml').getroot()
templates={element.attrib['name']:element.text for element in defaults}
assert set(templates)=={'模板一','模板二'}
for text in templates.values():
    assert all(part in text for part in ['某某医院','某门诊','某某主任'])
    assert not re.search(r'周[一二三四五六日天]|星期[一二三四五六日天]|下午\d+时',text)
library=ET.parse(source/'apps/DischargeAssistant/library.xml').getroot()
assert library.find('Intro').text==templates['模板一']
progress=ET.parse(source/'apps/ProgressNotesAssistant/library.xml').getroot()
physicians=[node for node in progress.iter('Field') if node.attrib.get('key')=='physician']
assert physicians and all(node.attrib.get('default')=='某某' for node in physicians)
combined='\n'.join(progress.itertext())
assert '某某主任医师查房看过病人后示' in combined
xaml=(source/'apps/DischargeAssistant/MainWindow.xaml').read_text('utf-8-sig')
app=(source/'apps/DischargeAssistant/App.cs').read_text('utf-8-sig')
followups=(source/'apps/DischargeAssistant/Followup.cs').read_text('utf-8-sig')
for button in ['TemplateOneButton','TemplateTwoButton']:
    assert 'x:Name="'+button+'"' in xaml and button in app and button in followups
for key in templates:assert '"'+key+'"' in followups
for path in source.rglob('*.xml'):
    if path.name=='payload.xml':continue
    root=ET.parse(path).getroot()
    assert not any(key in root.attrib for key in ['sourceSha256','catalogSha256','apkSha256'])
print(json.dumps({'public_template_keys':sorted(templates),'generic_appointment_defaults':True,'no_fixed_provider_schedule':True,'physician_placeholder':True,'button_bindings_match':True,'private_template_provenance_removed':True},ensure_ascii=False))
