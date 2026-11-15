"""Build the v1.4.1-public de-identified environment-complete distribution from an installer and .NET 4.8."""
from pathlib import Path,PurePosixPath
import argparse,hashlib,json,os,re,shutil,subprocess,xml.etree.ElementTree as ET,zipfile

def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()

def main():
    parser=argparse.ArgumentParser()
    source=parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--installer-zip',type=Path);source.add_argument('--installer',type=Path)
    parser.add_argument('--framework',type=Path,required=True)
    parser.add_argument('--output-dir',type=Path,required=True)
    args=parser.parse_args()
    if os.name!='nt':raise RuntimeError('Windows is required to verify the Microsoft installer signature')
    env=dict(os.environ,CLINICAL_VERIFY_FILE=str(args.framework.resolve()))
    env.pop('PSModulePath',None)
    command="$s=Get-AuthenticodeSignature -LiteralPath $env:CLINICAL_VERIFY_FILE; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notlike '*CN=Microsoft Corporation*'){exit 1}; $v=(Get-Item -LiteralPath $env:CLINICAL_VERIFY_FILE).VersionInfo.FileVersion; if($v -notlike '4.8.*'){exit 2}"
    shell=shutil.which('pwsh.exe') or shutil.which('powershell.exe')
    subprocess.run([shell,'-NoProfile','-NonInteractive','-Command',command],env=env,check=True)
    target=args.output_dir.resolve()/'临床文书助手四合一_v1.4.1-public_公开脱敏版'
    app=target/'01_直接运行/临床文书助手四合一'
    original=target/'02_公开版安装程序';component=target/'03_环境组件';report=target/'04_核验与来源'
    for folder in (app,original,component,report):folder.mkdir(parents=True,exist_ok=True)
    if args.installer_zip:
        with zipfile.ZipFile(args.installer_zip) as archive:
            installers=[item for item in archive.infolist() if item.filename.endswith('.exe')]
            if len(installers)!=1:raise RuntimeError('Expected one original suite installer in the ZIP')
            item=installers[0]
            installer=original/PurePosixPath(item.filename).name
            installer.write_bytes(archive.read(item))
            (original/(installer.name+'.config')).write_bytes(archive.read(item.filename+'.config'))
    else:
        installer=original/args.installer.name
        shutil.copyfile(args.installer,installer)
        shutil.copyfile(Path(str(args.installer)+'.config'),Path(str(installer)+'.config'))
    binary=installer.read_bytes();found=re.search(rb'<Files>.*?</Files>',binary,re.S)
    if not found:raise RuntimeError('Installer does not contain the expected integrity manifest')
    expected={entry.attrib['path'].replace('\\','/'):entry.attrib['sha256'] for entry in ET.fromstring(found.group().decode('utf8'))}
    records=[]
    with zipfile.ZipFile(installer) as payload:
        names={item.filename.replace('\\','/') for item in payload.infolist() if not item.is_dir()}
        if names!=set(expected):raise RuntimeError('Embedded payload and integrity manifest differ')
        for item in payload.infolist():
            if item.is_dir():continue
            name=item.filename.replace('\\','/');relative=PurePosixPath(name)
            if relative.is_absolute() or '..' in relative.parts or ':' in name:raise RuntimeError('Unsafe payload path')
            data=payload.read(item);sha=hashlib.sha256(data).hexdigest()
            if sha!=expected[name]:raise RuntimeError('Original integrity check failed: '+name)
            dest=app/relative;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
            records.append({'path':name,'sha256':sha,'original_manifest_match':True})
    here=Path(__file__).resolve().parent
    cmd=(here/'launch.cmd').read_text('utf8').replace('\r\n','\n')
    (target/'先点这里_检测并启动.cmd').write_bytes(cmd.replace('\n','\r\n').encode('gbk'))
    shutil.copyfile(here/'使用说明.txt',target/'使用说明_先看这里.txt')
    shutil.copyfile(args.framework,component/'NET-Framework-4.8-离线安装.exe')
    (report/'原程序逐文件校验.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf8')
    (report/'原安装载荷清单.xml').write_bytes(found.group())
    provenance={'installer_sha256':digest(installer),'framework_sha256':digest(args.framework),'framework_download':'https://dotnet.microsoft.com/download/dotnet-framework/net48','framework_signature':'Microsoft Corporation; Valid','payload_files_verified':len(records)}
    (report/'文件来源.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf8')
    archive_path=args.output_dir.resolve()/(target.name+'.zip')
    with zipfile.ZipFile(archive_path,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as archive:
        for path in sorted(target.rglob('*')):
            if path.is_file():archive.write(path,Path(target.name)/path.relative_to(target))
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip():raise RuntimeError('ZIP CRC check failed')
    archive_path.with_suffix('.zip.sha256.txt').write_text(digest(archive_path)+'  '+archive_path.name+'\n',encoding='utf8')
    print(json.dumps({'archive':str(archive_path),'verified_payload_files':len(records),'sha256':digest(archive_path)},ensure_ascii=False))

if __name__=='__main__':main()
