# Third-party components

- Microsoft .NET Framework 4.8 is distributed as its original, unmodified offline redistributable installer. It retains Microsoft's embedded license and installation terms. [Official redistribution guidance](https://learn.microsoft.com/dotnet/framework/install/guide-for-developers).
- The pypinyin-related license files included with the original source and application are retained next to the corresponding resources in `Source/apps/`.
- The application's own source and third-party components remain separate works. Publishing the source does not replace third-party licenses.

The release's `04_核验与来源` directory records the original package and .NET installer SHA256 values. The bundled installer was checked for a valid Microsoft Corporation Authenticode signature before packaging. No WebView2 runtime is required by this suite.
