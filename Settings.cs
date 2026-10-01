using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FastSwitcher {
public sealed class AppRule {
    public string Process {get;set;}
    public bool Layout {get;set;}
    public bool Typos {get;set;}
    public bool Yo {get;set;}
    public AppRule() { Process=""; Layout=false; Typos=false; Yo=false; }
}
public sealed class Settings {
    public bool Enabled {get;set;}
    public bool Layout {get;set;}
    public bool Typos {get;set;}
    public bool Yo {get;set;}
    public bool StartWithWindows {get;set;}
    public bool DarkTheme {get;set;}
    public bool SoundLayout {get;set;}
    public bool SoundTypos {get;set;}
    public string LayoutSoundFile {get;set;}
    public string TypoSoundFile {get;set;}
    public bool DoubleShift {get;set;}
    public bool SingleShift {get;set;}
    public bool Pause {get;set;}
    public string SelectedHotkey {get;set;}
    public string WordHotkey {get;set;}
    public string UndoHotkey {get;set;}
    public string ToggleHotkey {get;set;}
    public List<AppRule> Apps {get;set;}
    public List<string> ExcludedWords {get;set;}
    public Dictionary<string,string> Learned {get;set;}
    public List<string> RuWords {get;set;}
    public List<string> EnWords {get;set;}
    public Settings() {
        Enabled=true; Layout=true; Typos=true; Yo=true; DoubleShift=true; Pause=true;
        SelectedHotkey="Ctrl+Alt+F12"; WordHotkey="Ctrl+Alt+F9"; UndoHotkey="Ctrl+Alt+F11"; ToggleHotkey="Ctrl+Alt+F10";
        LayoutSoundFile=""; TypoSoundFile="";
        Apps=new List<AppRule>(); ExcludedWords=new List<string>(); Learned=new Dictionary<string,string>();
        RuWords=new List<string>(); EnWords=new List<string>();
        foreach(var p in new[]{"devenv","code","rider64","idea64","pycharm64","windowsterminal","wt","cmd","powershell","pwsh","conhost","mstsc"})
            Apps.Add(new AppRule{Process=p});
    }
}
public sealed class SettingsStore {
#if INPUT_TEST
    internal static string TestDirectory;
#endif
    public static string DirectoryPath {get{
#if INPUT_TEST
        if(!string.IsNullOrEmpty(TestDirectory))return TestDirectory;
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FastSwitcher");
    }}
    public static string PathName {get{return Path.Combine(DirectoryPath,"settings.json");}}
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=10000000};
    public Settings Current {get;private set;}
    public SettingsStore(){ Current=Load(PathName); }
    public Settings Load(string path) {
        try { return ReadStrict(path); }
        catch { if(File.Exists(path))try{File.Copy(path,path+".corrupt",true);}catch{} return new Settings(); }
    }
    Settings ReadStrict(string path){
        var s=json.Deserialize<Settings>(File.ReadAllText(path));
        if(s==null || s.Apps==null || s.ExcludedWords==null || s.Learned==null || s.RuWords==null || s.EnWords==null)
            throw new InvalidDataException("Некорректный файл настроек.");
        return Normalize(s);
    }
    static Settings Normalize(Settings s) {
        if(s==null) return new Settings();
        if(s.Apps==null)s.Apps=new List<AppRule>(); if(s.ExcludedWords==null)s.ExcludedWords=new List<string>();
        if(s.Learned==null)s.Learned=new Dictionary<string,string>(); if(s.RuWords==null)s.RuWords=new List<string>();
        if(s.EnWords==null)s.EnWords=new List<string>();
        if(string.IsNullOrWhiteSpace(s.WordHotkey))s.WordHotkey="Ctrl+Alt+F9";
        return s;
    }
    public void Save(){
        Directory.CreateDirectory(DirectoryPath);
        string temporary=PathName+".tmp";File.WriteAllText(temporary,json.Serialize(Current));
        if(File.Exists(PathName))File.Replace(temporary,PathName,null);else File.Move(temporary,PathName);
        SyncStartup();
    }
    public void Export(string path){ File.WriteAllText(path,json.Serialize(Current)); }
    public void Import(string path){ Current=ReadStrict(path); Save(); }
    public void SyncStartup() {
        using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) {
            if(k==null)return;
            if(Current.StartWithWindows)k.SetValue("FastSwitcher", "\""+System.Windows.Forms.Application.ExecutablePath+"\" --background");
            else k.DeleteValue("FastSwitcher",false);
        }
    }
}
}
