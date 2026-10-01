using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FastSwitcher {
static class Program {
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0&&args[0]=="--self-test")return SelfTest.Run();
        if(args.Length>0&&args[0]=="--smoke-test")return SmokeTest.Run();
#if INPUT_TEST
        if(args.Length>0&&args[0]=="--input-test")return InputIntegrationTests.Run();
        if(args.Length>0&&args[0]=="--layout-test")return InputIntegrationTests.Run(true);
        if(args.Length>1&&args[0]=="--input-target")return InputIntegrationTests.Target(args[1]);
#endif
        if(args.Length>0&&(args[0]=="--uninstall"||args[0]=="--uninstall-silent")){Uninstall(args[0].EndsWith("silent"));return 0;}
        if((args.Length>0&&(args[0]=="--setup"||args[0]=="--setup-silent"))||Path.GetFileNameWithoutExtension(Application.ExecutablePath).EndsWith("Setup",StringComparison.OrdinalIgnoreCase)){Install(args.Length>0&&args[0]=="--setup-silent");return 0;}
        bool created;using(var mutex=new Mutex(true,@"Local\FastSwitcher.Desktop.Singleton",out created)){
            if(!created){MessageBox.Show("Fast Switcher уже запущено. Откройте значок в системном трее.","Fast Switcher");return 0;}
            Application.Run(new MainForm(args.Length>0&&args[0]=="--background"));
        }
        return 0;
    }
    static string InstallDir(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","FastSwitcher");}
    static string StartMenu(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Fast Switcher.lnk");}
    static void Shortcut(string link,string target){
        var type=Type.GetTypeFromProgID("WScript.Shell");var shell=Activator.CreateInstance(type);
        var shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
        var t=shortcut.GetType();t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});
        t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(target)});
        t.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"Fast Switcher — локальный переключатель раскладки"});
        t.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,new object[0]);
    }
    static void Install(bool silent){
        try{
            string dir=InstallDir(),target=Path.Combine(dir,"FastSwitcher.exe");
            MigrateLegacySettings();StopInstalledCopy(target);Directory.CreateDirectory(dir);
            if(!string.Equals(Application.ExecutablePath,target,StringComparison.OrdinalIgnoreCase))File.Copy(Application.ExecutablePath,target,true);
            Shortcut(StartMenu(),target);
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FastSwitcher")){
                key.SetValue("DisplayName","Fast Switcher");key.SetValue("DisplayVersion","1.4.2");key.SetValue("Publisher","Личный проект");
                key.SetValue("InstallLocation",dir);key.SetValue("DisplayIcon",target);
                key.SetValue("UninstallString","\""+target+"\" --uninstall");key.SetValue("NoModify",1,RegistryValueKind.DWord);
            }
            RemoveLegacyInstall();
            using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){
                if(run!=null){var settings=new SettingsStore().Current;
                    if(settings.StartWithWindows)run.SetValue("FastSwitcher","\""+target+"\" --background");
                    else run.DeleteValue("FastSwitcher",false);
                }
            }
            if(!silent){MessageBox.Show("Fast Switcher установлено. Программа появится в меню «Пуск» и разделе «Приложения» Windows.","Установка завершена");Process.Start(target);}
        }catch(Exception e){if(silent)throw;MessageBox.Show("Установка не завершена: "+e.Message,"Fast Switcher",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    static void StopInstalledCopy(string executable){
        foreach(var p in Process.GetProcessesByName("FastSwitcher"))try{
            if(p.Id!=Process.GetCurrentProcess().Id && string.Equals(p.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase)){p.Kill();p.WaitForExit(3000);}
        }finally{p.Dispose();}
    }
    static bool TestOnlySettings(string path){
        try{
            var s=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));string value;
            return s!=null && s.Learned!=null && s.Learned.Count==1 && s.Learned.TryGetValue("teh",out value) && value=="teh"
                && s.RuWords!=null && s.RuWords.Count==0 && s.EnWords!=null && s.EnWords.Count==0
                && s.ExcludedWords!=null && s.ExcludedWords.Count==0;
        }catch{return false;}
    }
    static void MigrateLegacySettings(){
        string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lado","settings.json");
        string current=SettingsStore.PathName;
        if(!File.Exists(legacy))return;
        if(File.Exists(current) && !TestOnlySettings(current))return;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        if(File.Exists(current))File.Copy(current,current+".pre-migration",true);
        File.Copy(legacy,current,true);
    }
    static void RemoveLegacyInstall(){
        string legacyDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Lado");
        string legacyExe=Path.Combine(legacyDir,"Lado.exe");
        foreach(var p in Process.GetProcessesByName("Lado"))try{
            if(string.Equals(p.MainModule.FileName,legacyExe,StringComparison.OrdinalIgnoreCase)){p.Kill();p.WaitForExit(3000);}
        }finally{p.Dispose();}
        if(File.Exists(legacyExe))File.Delete(legacyExe);
        if(Directory.Exists(legacyDir) && Directory.GetFileSystemEntries(legacyDir).Length==0)Directory.Delete(legacyDir,false);
        string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Ладо.lnk");
        if(File.Exists(link))File.Delete(link);
        using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true))if(run!=null)run.DeleteValue("Lado",false);
        Registry.CurrentUser.DeleteSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lado",false);
    }
    static void Uninstall(bool silent){
        try{
            using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true))if(run!=null)run.DeleteValue("FastSwitcher",false);
            Registry.CurrentUser.DeleteSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FastSwitcher",false);
            if(File.Exists(StartMenu()))File.Delete(StartMenu());
            string dir=Path.GetFullPath(InstallDir()),exe=Path.Combine(dir,"FastSwitcher.exe");StopInstalledCopy(exe);
            string script=Path.Combine(Path.GetTempPath(),"FastSwitcher-uninstall-"+Guid.NewGuid().ToString("N")+".ps1");
            // Delete only this app's exact files, in one shell with literal paths.
            string cleanup="$fastSwitcherDir = '"+dir.Replace("'","''")+"'\r\n"+
                "for ($attempt = 0; $attempt -lt 30; $attempt++) {\r\nStart-Sleep -Seconds 1\r\n"+
                "if (!(Test-Path -LiteralPath $fastSwitcherDir)) { break }\r\n"+
                "$fastSwitcherFiles = @(Get-ChildItem -LiteralPath $fastSwitcherDir -File | Where-Object { $_.Name -match '^(FastSwitcher\\.exe|FastSwitcher\\.Layout\\.[A-F0-9]{64}\\.dll)$' })\r\n"+
                "foreach ($fastSwitcherFile in $fastSwitcherFiles) { if ([IO.Path]::GetFullPath($fastSwitcherFile.DirectoryName) -eq $fastSwitcherDir) { Remove-Item -LiteralPath $fastSwitcherFile.FullName -Force -ErrorAction SilentlyContinue } }\r\n"+
                "if (@(Get-ChildItem -LiteralPath $fastSwitcherDir -Force).Count -eq 0) { Remove-Item -LiteralPath $fastSwitcherDir -ErrorAction SilentlyContinue; break }\r\n"+
                "}\r\nRemove-Item -LiteralPath $PSCommandPath -Force\r\n";
            File.WriteAllText(script,cleanup,System.Text.Encoding.UTF8);
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            Process.Start(new ProcessStartInfo(powershell,"-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""){CreateNoWindow=true,UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden});
            if(!silent)MessageBox.Show("Fast Switcher удалено. Личные словари и настройки оставлены в %LOCALAPPDATA%\\FastSwitcher.","Удаление завершено");
        }catch(Exception e){if(silent)throw;MessageBox.Show("Не удалось удалить программу: "+e.Message,"Fast Switcher",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
static class SelfTest {
    static int count,failed;
    static void Check(string name,string got,string expected){count++;if(got!=expected){failed++;Console.WriteLine("FAIL "+name+": "+got+" != "+expected);}else Console.WriteLine("OK "+name);}
    public static int Run(){
        var e=new LanguageEngine();var s=new Settings();
        Console.WriteLine(e.DictionaryStatus);
        Check("unknown English unchanged",e.Decide("qzxqzxqzx","",s).Text,"qzxqzxqzx");
        Check("unknown Russian unchanged",e.Decide("щжщжщжщж","",s).Text,"щжщжщжщж");
        using(var spelling=new WindowsSpelling()){
            bool known;
            if(spelling.EnglishAvailable){
                Check("Windows English word",(spelling.TryIsWord(true,"astronomy",out known)&&known).ToString(),"True");
                Check("Windows English nonsense",(spelling.TryIsWord(true,"qzxqzxqzx",out known)&&!known).ToString(),"True");
                Check("extended EN layout",e.Decide(e.Convert("astronomy"),"",s).Text,"astronomy");
            }
            if(spelling.RussianAvailable){
                Check("Windows Russian word",(spelling.TryIsWord(false,"библиотекарь",out known)&&known).ToString(),"True");
                Check("Windows Russian nonsense",(spelling.TryIsWord(false,"щжщжщжщж",out known)&&!known).ToString(),"True");
                Check("extended RU layout",e.Decide(e.Convert("астрономия"),"",s).Text,"астрономия");
            }
        }
        Check("RU layout",e.Decide("ghbdtn","",s).Text,"привет");
        Check("EN layout",e.Decide("руддщ","",s).Text,"hello");
        Check("government complete",e.Decide("пщмуктьуте","",s).Text,"government");
        Check("government early prefix",e.DecidePrefix("пщмук","",s).Text,"gover");
        Check("no short early prefix",e.DecidePrefix("пщму","",s).Text,"пщму");
        Check("short ambiguity",e.Decide("rj","",s).Text,"rj");
        Check("Russian typo",e.Decide("превет","",s).Text,"привет");
        Check("English typo",e.Decide("teh","",s).Text,"the");
        Check("case",e.Decide("ПРивет","",s).Text,"Привет");
        Check("yo",e.Decide("елка","",s).Text,"ёлка");
        Check("ambiguous yo",e.Decide("все","",s).Text,"все");
        Check("mixed",e.Decide("heпривет","",s).Text,"heпривет");
        Check("acronym",e.Decide("NASA","",s).Text,"NASA");
        Check("digits",e.Decide("test123","",s).Text,"test123");
        Check("link",e.Decide("ghbdtn","https://",s).Text,"ghbdtn");
        Check("email",e.Decide("ghbdtn","me@",s).Text,"ghbdtn");
        Check("digit prefix",e.Decide("ghbdtn","abc123",s).Text,"ghbdtn");
        Check("domain",e.Decide("ghbdtn","www.",s).Text,"ghbdtn");
        Check("name",e.Decide("Ivan","",s).Text,"Ivan");
        Check("upper acronym",e.Decide("GHBDTN","",s).Text,"GHBDTN");
        Check("repeated conversion",e.Decide("привет","",s).Text,"привет");
        Check("manual mapping",e.Convert("Ghbdtn"),"Привет");
        Check("app exclusion",e.Decide("ghbdtn","",s,new AppRule{Process="notepad"}).Text,"ghbdtn");
        s.ExcludedWords.Add("ghbdtn");Check("exclusion",e.Decide("ghbdtn","",s).Text,"ghbdtn");
        s.ExcludedWords.Clear();s.Learned["ghbdtn"]="ghbdtn";Check("learned undo",e.Decide("ghbdtn","",s).Text,"ghbdtn");
        Check("x64 SendInput layout",System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)).ToString(),"40");
        e.Dispose();Console.WriteLine(count+" tests, "+failed+" failed");return failed==0?0:1;
    }
}
static class SmokeTest {
    public static int Run(){
        bool ready=false;var form=new MainForm(true);
        form.Shown+=delegate{var t=new System.Windows.Forms.Timer{Interval=500};t.Tick+=delegate{t.Stop();t.Dispose();ready=form.HookReady&&form.LayoutValid&&form.SmokePages();
            if(ready){form.Show();form.Activate();Application.DoEvents();using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"ui-preview.png"));}}
            form.QuitForTests();};t.Start();};
        Application.Run(form);
        Console.WriteLine(ready?"OK six UI pages, layout and keyboard hook":"FAIL UI pages, layout or keyboard hook");return ready?0:1;
    }
}
}
