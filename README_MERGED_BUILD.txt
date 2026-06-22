OverWatch ELD + ATS Dispatcher merged package
===========================================

What was merged:
- ATS mod-folder scanning backend into the main OverWatch ELD app
- ATS save-job import backend into the main OverWatch ELD app
- No separate dispatcher bot/project is required in this package
- Existing OverWatch ELD dispatch/bot flow stays primary

Important:
- This package was prepared from the uploaded source/runtime files.
- A final .exe could not be built in this environment because the .NET SDK is not installed here.
- Use build-single-exe.bat on your Windows machine with .NET 8 SDK installed.

What starts automatically:
- OverWatch ELD normal startup
- Integrated ATS mod scan
- ATS profile/save job watcher
- Imported jobs are written into the existing VTC jobs store used by OverWatch ELD

Build:
1) Install .NET 8 SDK
2) Open this folder in PowerShell
3) Run build-single-exe.bat
4) Your output will be under publish-single
Notes:
- The separate ATS.Dispatcher Discord bot/web server files were not used in the merged build path.
- OverWatch ELD remains the single app / single bot path.

Dispather:
Local profiles only

Steam Cloud unsupported

Backup required

Version/mod compatibility not guaranteed