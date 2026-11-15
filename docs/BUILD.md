# Build v1.4.1-public

Use Windows with .NET Framework 4.8, its C# compiler, WPF assemblies, and Windows PowerShell 5+ or PowerShell 7.

```powershell
./Source/build.ps1
./Source/build-installer.ps1
```

The first command builds the shared offline laboratory component, all main applications, the standalone examination-result tool and the suite launcher into `Portable/`. The second embeds that folder into the public-edition installer format. These generated files are ignored by Git.

Using Python 3.11+, create the environment-complete archive using the generated installer and its `.config` (or an public-edition installer ZIP), plus the unmodified Microsoft .NET Framework 4.8 offline installer:

```powershell
python environment/build-package.py --installer ./临床文书助手四合一_安装程序.exe --framework <NDP48-x86-x64-AllOS-ENU.exe> --output-dir release-output
```

The builder checks the installer's embedded per-file SHA256 manifest before extraction, keeps the public-edition installer and application payload, writes the startup script in CP936/CRLF for older Chinese Windows consoles, and creates a ZIP and SHA256 manifest. The Microsoft installer is available from [the official .NET 4.8 page](https://dotnet.microsoft.com/download/dotnet-framework/net48); verify its Microsoft Authenticode signature before redistribution.

The `environment/launch.cmd` source file is UTF-8 for code review. Use the packaged CP936 version on an older Chinese Windows console. `--check-only` returns 0 when dependencies and program files are ready, 10 when .NET 4.8 is absent/too old, and 20 when required program files are missing. It does not install components automatically.

Do not commit local patient drafts, user-specific template directories, logs, build products or installation profiles. The published archive is the rebuilt de-identified environment-complete distribution; compiler outputs from a local rebuild may differ at the binary level.

Build the public installer from this source before packaging. Do not substitute an older private-edition installer. The public release includes only the newly built application resources.
