#if INPUT_TEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;

namespace FastSwitcher {
internal static class InputIntegrationTests {
    static Native.INPUT Raw(ushort vk,bool up){
        return new Native.INPUT{type=1,u=new Native.InputUnion{ki=new Native.KEYBDINPUT{vk=vk,flags=(uint)(up?Native.KEYEVENTF_KEYUP:0)}}};
    }
    static void Keys(params ushort[] codes){
        var events=new List<Native.INPUT>();
        foreach(ushort code in codes){events.Add(Raw(code,false));events.Add(Raw(code,true));}
        uint sent=Native.SendInput((uint)events.Count,events.ToArray(),Marshal.SizeOf(typeof(Native.INPUT)));
        if(sent!=events.Count)throw new InvalidOperationException("Test SendInput failed: "+sent);
    }
    static void SelectAll(){
        Native.SendInput(4,new[]{Raw(Native.VK_CONTROL,false),Raw(0x41,false),Raw(0x41,true),Raw(Native.VK_CONTROL,true)},Marshal.SizeOf(typeof(Native.INPUT)));
    }
    static void DoubleShift(){Keys(Native.VK_SHIFT,Native.VK_SHIFT);}
    static void Shifted(ushort vk){Native.SendInput(4,new[]{Raw(Native.VK_SHIFT,false),Raw(vk,false),Raw(vk,true),Raw(Native.VK_SHIFT,true)},Marshal.SizeOf(typeof(Native.INPUT)));}
    static string Read(string path){try{if(!File.Exists(path))return "";using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}catch(IOException){return "";}}
    static void Write(string path,string value){using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite))using(var writer=new StreamWriter(stream))writer.Write(value);}
    static async Task<bool> WaitFor(string path,string expected){
        for(int i=0;i<40;i++){if(Read(path)==expected)return true;await Task.Delay(100);}
        return false;
    }
    static async Task<bool> WaitForVerified(string path,string expected,Func<bool> completed){
        for(int i=0;i<40;i++){if(Read(path)==expected && completed())return true;await Task.Delay(100);}
        return false;
    }
    static void English(IntPtr target){Native.PostMessage(target,0x8001,IntPtr.Zero,IntPtr.Zero);}
    static void Russian(IntPtr target){Native.PostMessage(target,0x8002,IntPtr.Zero,IntPtr.Zero);}
    static bool TargetLanguage(IntPtr target,int language){uint pid;return ((long)Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(target,out pid))&0xffff)==language;}
    static string LayoutInfo(IntPtr target){uint pid;uint thread=Native.GetWindowThreadProcessId(target,out pid);return "thread="+thread+" hkl="+Native.GetKeyboardLayout(thread).ToInt64().ToString("X");}
    static async Task Focus(IntPtr target){
        for(int i=0;i<15;i++){
            Native.PostMessage(target,0x8003,IntPtr.Zero,IntPtr.Zero);
            Native.SetForegroundWindow(target);
            await Task.Delay(100);
            if(Native.GetForegroundWindow()==target)return;
        }
        throw new InvalidOperationException("Test target could not get foreground focus");
    }
    static async Task FocusWpf(IntPtr target){
        for(int i=0;i<15;i++){
            Native.PostMessage(target,0x8012,IntPtr.Zero,IntPtr.Zero);
            Native.SetForegroundWindow(target);
            await Task.Delay(100);
            if(Native.GetForegroundWindow()==target)return;
        }
        throw new InvalidOperationException("WPF test target could not get foreground focus");
    }
    static async Task FocusRich(IntPtr target){
        for(int i=0;i<15;i++){
            Native.PostMessage(target,0x8022,IntPtr.Zero,IntPtr.Zero);Native.SetForegroundWindow(target);await Task.Delay(100);
            if(Native.GetForegroundWindow()==target)return;
        }
        throw new InvalidOperationException("Rich text test target could not get focus");
    }
    public static int Run(bool layoutOnly=false){
        string path=Path.Combine(Path.GetTempPath(),"fast-switcher-input-test-"+Guid.NewGuid().ToString("N")+".txt");
        string dataDir=path+".data";SettingsStore.TestDirectory=dataDir;
        Process child=null;var host=new MainForm(true);int failed=0;
        InputService.AcceptSyntheticInput=true;
        host.Shown+=async delegate{
            try{
                child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--input-target \""+path+"\""){UseShellExecute=false});
                for(int i=0;i<50&&!File.Exists(path+".ready");i++)await Task.Delay(100);
                if(!File.Exists(path+".ready"))throw new InvalidOperationException("Test target did not start");
                IntPtr target=new IntPtr(long.Parse(File.ReadAllText(path+".ready")));
                English(target);await Focus(target);await Task.Delay(120);
                Keys(0x48,0x45,0x4C,0x4C,0x4F,Native.VK_SPACE);
                if(await WaitFor(path,"hello "))Console.WriteLine("OK initial English word stays English");
                else{Console.WriteLine("FAIL initial English input: ["+Read(path)+"]");failed++;}
                Keys(0x47,0x48,0x42,0x44,0x54,0x4E,Native.VK_SPACE);
                if(await WaitFor(path,"hello привет "))Console.WriteLine("OK automatic RU word after English text");
                else{Console.WriteLine("FAIL automatic layout: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Task.Delay(180);
                if(TargetLanguage(target,0x0419))Console.WriteLine("OK RU persists when both child and top window ignore ordinary layout requests");
                else{Console.WriteLine("FAIL layout persistence after RU correction: "+LayoutInfo(target)+" "+host.InputDiagnostic+" requests="+Read(path+".layout-msgs"));failed++;}
                Keys(0x51,0x5A,0x58);
                if(await WaitFor(path,"hello привет йяч"))Console.WriteLine("OK next raw keys use RU without word conversion");
                else{Console.WriteLine("FAIL subsequent RU typing: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Task.Delay(220);Keys(0x51);
                if(await WaitFor(path,"hello привет йячq") && TargetLanguage(target,0x0409))Console.WriteLine("OK explicit later language change is respected");
                else{Console.WriteLine("FAIL explicit layout change: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);
                Keys(0x48,0x45,0x4C,0x4C,0x4F,Native.VK_SPACE);
                if(await WaitFor(path,"hello "))Console.WriteLine("OK reverse automatic layout in editable field");
                else{Console.WriteLine("FAIL reverse automatic layout: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Keys(0x51,0x5A,0x58);
                if(await WaitFor(path,"hello qzx") && TargetLanguage(target,0x0409))Console.WriteLine("OK reverse conversion retains EN for next raw keys");
                else{Console.WriteLine("FAIL subsequent EN typing: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);
                Keys(0x47,0x4F,0x56,0x45,0x52);
                if(await WaitFor(path,"gover"))Console.WriteLine("OK government corrected before word end");
                else{Console.WriteLine("FAIL early government: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                if(TargetLanguage(target,0x0409))Console.WriteLine("OK early correction leaves EN active");
                else{Console.WriteLine("FAIL early correction layout: "+LayoutInfo(target)+" "+host.InputDiagnostic+" requests="+Read(path+".layout-msgs"));failed++;}
                Keys(0x4E,0x4D,0x45,0x4E,0x54);
                if(await WaitFor(path,"government"))Console.WriteLine("OK government remainder after layout change");
                else{Console.WriteLine("FAIL government remainder: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"пщмуктьуте"))Console.WriteLine("OK early conversion undo after word completion");
                else{Console.WriteLine("FAIL early conversion undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                host.ForgetRuleForTest("пщмуктьуте");
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);
                Native.PostMessage(target,0x800B,IntPtr.Zero,IntPtr.Zero);await Task.Delay(100);
                Keys(0x47,0x4F,0x56,0x45,0x52);
                if(await WaitFor(path,"gover") && TargetLanguage(target,0x0409))Console.WriteLine("OK prefix correction waits for delayed target character processing");
                else{Console.WriteLine("FAIL delayed prefix: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Native.PostMessage(target,0x800C,IntPtr.Zero,IntPtr.Zero);await Task.Delay(100);
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);Keys(0x30,0xBC,0x35);
                if(await WaitFor(path,"0,5") && TargetLanguage(target,0x0419))Console.WriteLine("OK decimal comma corrected immediately without changing language");
                else{Console.WriteLine("FAIL decimal comma: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Keys(0x30,Native.VK_SPACE);await WaitFor(path,"0,50 ");DoubleShift();
                if(await WaitFor(path,"0б50 "))Console.WriteLine("OK numeric correction undo includes continued digits");
                else{Console.WriteLine("FAIL numeric undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);Keys(0x31,0x32,0xBE,0x33,0x34,0x35,Native.VK_SPACE);
                if(await WaitFor(path,"12.345 "))Console.WriteLine("OK decimal point conversion");
                else{Console.WriteLine("FAIL decimal point: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);Shifted(0xDB);Keys(0x48,0x54,0x59,Native.VK_SPACE);
                if(await WaitFor(path,"Хрен ") && TargetLanguage(target,0x0419))Console.WriteLine("OK shifted bracket is converted with the whole capitalized word");
                else{Console.WriteLine("FAIL capitalized bracket word: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                DoubleShift();if(await WaitFor(path,"{hty "))Console.WriteLine("OK bracket word conversion undo");else{Console.WriteLine("FAIL bracket undo: ["+Read(path)+"]");failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);Keys(0x4A,0xBC,0xDD,0x54,0x52,0x4E,Native.VK_SPACE);
                if(await WaitFor(path,"объект "))Console.WriteLine("OK comma and right bracket inside a word");else{Console.WriteLine("FAIL internal punctuation: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);Shifted(0xC0);Keys(0x4B,0x52,0x46,Native.VK_SPACE);
                if(await WaitFor(path,"Ёлка "))Console.WriteLine("OK shifted tilde retains capitalized yo");else{Console.WriteLine("FAIL capitalized yo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                if(layoutOnly)return;
                Native.PostMessage(target,0x8004,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"привет how are");
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"привет how фку"))Console.WriteLine("OK Double Shift changes only caret word in mixed phrase");
                else{Console.WriteLine("FAIL mixed caret word: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"привет how are"))Console.WriteLine("OK mixed caret word undo");
                else{Console.WriteLine("FAIL mixed caret word undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);
                Native.PostMessage(target,0x8007,IntPtr.Zero,IntPtr.Zero);
                if(await WaitFor(path,"ghbdtn"))Console.WriteLine("OK selected word setup");
                else{Console.WriteLine("FAIL word entry: ["+Read(path)+"]");failed++;}
                await Focus(target);SelectAll();await Task.Delay(150);DoubleShift();
                if(await WaitFor(path,"привет"))Console.WriteLine("OK Double Shift selection conversion");
                else{Console.WriteLine("FAIL Double Shift selection: ["+Read(path)+"]");failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);
                Keys(0x54,0x45,0x48,Native.VK_SPACE);
                if(await WaitFor(path,"the "))Console.WriteLine("OK automatic typo in editable field");
                else{Console.WriteLine("FAIL automatic typo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"teh "))Console.WriteLine("OK Double Shift undo");
                else{Console.WriteLine("FAIL Double Shift undo: ["+Read(path)+"]");failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);
                Keys(0x54,0x45,0x48);
                if(!await WaitFor(path,"teh")){Console.WriteLine("FAIL stale automatic setup: ["+Read(path)+"]");failed++;}
                Native.PostMessage(target,0x8006,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"abc");
                await Focus(target);Keys(Native.VK_SPACE);
                if(await WaitFor(path,"abc "))Console.WriteLine("OK stale automatic buffer preserves preceding text");
                else{Console.WriteLine("FAIL stale automatic buffer: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Native.PostMessage(target,0x8004,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"привет how are");
                await Focus(target);SelectAll();await Task.Delay(150);DoubleShift();
                if(await WaitFor(path,"ghbdtn рщц фку"))Console.WriteLine("OK mixed selection conversion");
                else{Console.WriteLine("FAIL mixed selection conversion: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"привет how are"))Console.WriteLine("OK mixed selection undo");
                else{Console.WriteLine("FAIL mixed selection undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Native.PostMessage(target,0x8005,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"123");
                await Focus(target);SelectAll();await Task.Delay(150);DoubleShift();await Task.Delay(300);
                if(Read(path)=="123")Console.WriteLine("OK unchanged selection does not convert stale word");
                else{Console.WriteLine("FAIL unchanged selection: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);
                Native.PostMessage(target,0x8007,IntPtr.Zero,IntPtr.Zero);
                if(!await WaitFor(path,"ghbdtn")){Console.WriteLine("FAIL manual word setup: ["+Read(path)+"]");failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"привет"))Console.WriteLine("OK Double Shift word without selection from actual caret");
                else{Console.WriteLine("FAIL verified last-word conversion: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Native.PostMessage(target,0x8006,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"abc");
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"фис"))Console.WriteLine("OK stale action falls back to actual caret word");
                else{Console.WriteLine("FAIL stale action fallback: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);DoubleShift();
                if(await WaitFor(path,"abc"))Console.WriteLine("OK actual caret word undo preserves text");
                else{Console.WriteLine("FAIL actual caret word undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await Focus(target);Keys(Native.VK_LEFT);
                Native.PostMessage(target,0x8008,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"ghbdtn");
                await Focus(target);DoubleShift();await Task.Delay(300);
                if(Read(path)=="ghbdtn")Console.WriteLine("OK caret inside word preserves text");
                else{Console.WriteLine("FAIL inside-word caret: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                int valueReplacements=host.InputVerifiedValueReplacements;
                English(target);Native.PostMessage(target,0x8010,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");
                await FocusWpf(target);Keys(0x52,0x45,0x43,0x49,0x45,0x56,0x45,Native.VK_SPACE);
                if(await WaitForVerified(path,"receive ",()=>host.InputVerifiedValueReplacements>valueReplacements))Console.WriteLine("OK verified automatic typo in UI Automation text field");
                else{Console.WriteLine("FAIL UIA automatic typo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await FocusWpf(target);Keys(Native.VK_LEFT);
                if(!await WaitFor(path+".caret","7,0"))throw new InvalidOperationException("Test arrow did not reach the old WPF field before setup");
                Native.PostMessage(target,0x8011,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"ghbdtn");
                if(!await WaitFor(path+".caret","6,0"))throw new InvalidOperationException("Test WPF caret setup did not complete");
                await FocusWpf(target);DoubleShift();
                if(await WaitFor(path,"привет"))Console.WriteLine("OK Double Shift caret word in UI Automation text field");
                else{Console.WriteLine("FAIL UIA caret word: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Native.PostMessage(target,0x8013,IntPtr.Zero,IntPtr.Zero);await Task.Delay(200);DoubleShift();
                if(await WaitFor(path,"ghbdtn рщц фку"))Console.WriteLine("OK selected mixed phrase in UI Automation text field");
                else{Console.WriteLine("FAIL UIA mixed selection: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await FocusWpf(target);DoubleShift();
                if(await WaitFor(path,"привет how are"))Console.WriteLine("OK UI Automation mixed selection undo");
                else{Console.WriteLine("FAIL UIA mixed selection undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                int selectionReplacements=host.InputVerifiedValueReplacements;
                Native.PostMessage(target,0x8014,IntPtr.Zero,IntPtr.Zero);await Task.Delay(200);DoubleShift();
                if(await WaitForVerified(path,"привет рщц are",()=>host.InputVerifiedValueReplacements>selectionReplacements))Console.WriteLine("OK verified partial selection preserves first letters in UI Automation text field");
                else{Console.WriteLine("FAIL UIA partial selection: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                await FocusWpf(target);Keys(Native.VK_LEFT);
                if(!await WaitFor(path+".caret","9,0"))throw new InvalidOperationException("Test arrow did not reach the previous selection result");
                Native.PostMessage(target,0x8015,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"ghbdtn ghbdtn");
                if(!await WaitFor(path+".caret","6,0"))throw new InvalidOperationException("Test repeated-word caret setup did not complete");
                await FocusWpf(target);DoubleShift();
                if(await WaitFor(path,"привет ghbdtn"))Console.WriteLine("OK repeated words convert at caret, preserve trailing word");
                else{Console.WriteLine("FAIL repeated caret word: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);SelectAll();await Task.Delay(150);
                Keys(0x51,0x5A,0x58,0x51,0x5A,0x58,0x51,0x5A,0x58,Native.VK_SPACE);
                if(await WaitFor(path,"qzxqzxqzx ")){await Task.Delay(400);
                    if(Read(path)=="qzxqzxqzx ")Console.WriteLine("OK unknown word preserved after automatic checks");
                    else{Console.WriteLine("FAIL unknown changed: ["+Read(path)+"]");failed++;}
                }else{Console.WriteLine("FAIL unknown input: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                using(var spelling=new WindowsSpelling())if(spelling.EnglishAvailable){
                    Russian(target);await Focus(target);SelectAll();await Task.Delay(150);
                    Keys(0x41,0x53,0x54,0x52,0x4F,0x4E,0x4F,0x4D,0x59,Native.VK_SPACE);
                    if(await WaitFor(path,"astronomy "))Console.WriteLine("OK Windows dictionary conversion outside bundled words");
                    else{Console.WriteLine("FAIL extended dictionary input: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                }
                Russian(target);await Focus(target);SelectAll();await Task.Delay(150);
                await host.DelayInputWorkerForTest(1600);
                Keys(0x57,0x4F,0x52,0x4C,0x44,Native.VK_SPACE);
                if(await WaitFor(path,"world ") && host.HookReady)Console.WriteLine("OK input hook survives a slow text provider");
                else{Console.WriteLine("FAIL slow provider: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);await Focus(target);Native.PostMessage(target,0x8009,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");
                Keys(0x47,0x48,0x42,0x44,0x54,0x4E,Native.VK_SPACE);
                if(await WaitFor(path,"ghbdtn ")){DoubleShift();await Task.Delay(500);
                    if(Read(path)=="ghbdtn ")Console.WriteLine("OK password field skips automatic and manual conversion");
                    else{Console.WriteLine("FAIL password conversion");failed++;}
                }else{Console.WriteLine("FAIL password field setup");failed++;}
                Native.PostMessage(target,0x800A,IntPtr.Zero,IntPtr.Zero);
                Russian(target);Native.PostMessage(target,0x8010,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");await FocusWpf(target);
                Keys(0x30,0xBC,0x35);
                if(await WaitFor(path,"0,5"))Console.WriteLine("OK numeric correction through UI Automation");
                else{Console.WriteLine("FAIL UIA decimal: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Keys(0x30,Native.VK_SPACE);await WaitFor(path,"0,50 ");DoubleShift();
                if(await WaitFor(path,"0б50 "))Console.WriteLine("OK UIA numeric continuation undo");else{Console.WriteLine("FAIL UIA numeric undo: ["+Read(path)+"]");failed++;}
                English(target);Native.PostMessage(target,0x8010,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");await FocusWpf(target);Shifted(0xDB);Keys(0x48,0x54,0x59,Native.VK_SPACE);
                if(await WaitFor(path,"Хрен ") && TargetLanguage(target,0x0419))Console.WriteLine("OK shifted punctuation word through UI Automation with retained language");
                else{Console.WriteLine("FAIL UIA capitalized word: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);Native.PostMessage(target,0x8020,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");await FocusRich(target);
                Keys(0x52,0x45,0x43,0x49,0x45,0x56,0x45,Native.VK_SPACE);
                if(await WaitFor(path,"receive "))Console.WriteLine("OK automatic conversion in editable Document without ValuePattern");
                else{Console.WriteLine("FAIL rich document automatic: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                int richCorrections=host.InputCorrections;
                Native.PostMessage(target,0x8021,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"KEEP ghbdtn tail");await FocusRich(target);DoubleShift();
                if(await WaitForVerified(path,"KEEP привет tail",()=>host.InputCorrections>richCorrections) && Read(path+".format")=="bold")Console.WriteLine("OK rich document caret conversion preserves adjacent text and formatting");
                else{Console.WriteLine("FAIL rich document caret: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                English(target);Native.PostMessage(target,0x8030,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");await Task.Delay(150);
                Keys(0x52,0x45,0x43,0x49,0x45,0x56,0x45,Native.VK_SPACE);
                if(await WaitFor(path,"receive "))Console.WriteLine("OK editable Custom role with verified TextPattern");
                else{Console.WriteLine("FAIL custom editor: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                if(Read(path+".uia-selects")=="0")Console.WriteLine("OK broken UIA Select provider cannot move the correction caret");
                else{Console.WriteLine("FAIL correction invoked broken UIA Select");failed++;}
                Native.PostMessage(target,0x8031,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"KEEP ghbdtn tail");await Task.Delay(180);
                int atomicCorrections=host.InputCorrections;DoubleShift();
                if(await WaitForVerified(path,"KEEP привет tail",()=>host.InputCorrections>atomicCorrections) && Read(path+".uia-selects")=="0")Console.WriteLine("OK caret conversion succeeds with a provider whose Select collapses to document start");
                else{Console.WriteLine("FAIL hostile UIA caret conversion: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                Keys(0x46);
                if(await WaitFor(path,"KEEP привета tail"))Console.WriteLine("OK next letter follows converted word in retained RU layout");
                else{Console.WriteLine("FAIL typing after hostile UIA conversion: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                host.ForgetRuleForTest("пщмуктьуте");
                Russian(target);Native.PostMessage(target,0x8010,IntPtr.Zero,IntPtr.Zero);await WaitFor(path,"");await FocusWpf(target);
                TextAccess.TestPostSendDelay=500;int fastCorrections=host.InputCorrections;
                Keys(0x47,0x4F,0x56,0x45,0x52);
                if(!await WaitForVerified(path,"gover",()=>host.InputCorrections>fastCorrections))throw new InvalidOperationException("Fast typing prefix setup failed: "+host.InputDiagnostic);
                Keys(0x4E,0x4D,0x45,0x4E,0x54,Native.VK_SPACE);
                if(await WaitFor(path,"government "))Console.WriteLine("OK continued typing while replacement verification is delayed");
                else{Console.WriteLine("FAIL continued typing: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
                TextAccess.TestPostSendDelay=0;DoubleShift();
                if(await WaitFor(path,"пщмуктьуте "))Console.WriteLine("OK complete undo after continued typing during verification");
                else{Console.WriteLine("FAIL continued typing undo: ["+Read(path)+"] "+host.InputDiagnostic);failed++;}
            }catch(Exception e){Console.WriteLine("FAIL input test: "+e);failed++;}
            finally{host.QuitForTests();}
        };
        try{Application.Run(host);}finally{
            if(child!=null && !child.HasExited){child.Kill();child.WaitForExit(3000);}if(child!=null)child.Dispose();
            if(File.Exists(path))File.Delete(path);if(File.Exists(path+".ready"))File.Delete(path+".ready");
            if(File.Exists(path+".format"))File.Delete(path+".format");
            if(File.Exists(path+".caret"))File.Delete(path+".caret");
            if(File.Exists(path+".uia-selects"))File.Delete(path+".uia-selects");
            if(File.Exists(path+".layout-msgs"))File.Delete(path+".layout-msgs");
            foreach(string name in new[]{"settings.json","settings.json.tmp","settings.json.corrupt"}){
                string file=Path.Combine(dataDir,name);if(File.Exists(file))File.Delete(file);
            }
            if(Directory.Exists(dataDir))Directory.Delete(dataDir,false);
            SettingsStore.TestDirectory=null;
            InputService.AcceptSyntheticInput=false;
            TextAccess.TestPostSendDelay=0;
        }
        Console.WriteLine(failed==0?"OK input integration":"FAIL input integration: "+failed);
        return failed==0?0:1;
    }
    sealed class TargetForm : Form {
        public string LayoutReport;
        public TextBox Box;
        public System.Windows.Controls.TextBox WpfBox;
        public System.Windows.Controls.RichTextBox RichBox;
        public CustomEditor CustomBox;
        protected override void WndProc(ref Message m){
            if(m.Msg==Native.WM_INPUTLANGCHANGEREQUEST){
                // Both window and child decline normal requests. The fixture
                // never cooperates with the switcher's native language command.
                long requested=m.LParam.ToInt64();m.Result=IntPtr.Zero;
                Write(LayoutReport,"request="+requested.ToString("X")+" after="+Native.GetKeyboardLayout(0).ToInt64().ToString("X"));return;
            }
            if(m.Msg==0x8001){var h=Native.LoadKeyboardLayout("00000409",1);if(h!=IntPtr.Zero)Native.ActivateKeyboardLayout(h,0);}
            if(m.Msg==0x8002){var h=Native.LoadKeyboardLayout("00000419",1);if(h!=IntPtr.Zero)Native.ActivateKeyboardLayout(h,0);}
            if(m.Msg==0x8003){Activate();if(Box!=null)Box.Focus();}
            if(m.Msg==0x800B){((IgnoringLayoutTextBox)Box).DelayCharacters=true;}
            if(m.Msg==0x800C){((IgnoringLayoutTextBox)Box).DelayCharacters=false;}
            if(m.Msg==0x8004 && Box!=null){Box.Text="привет how are";Box.SelectionStart=Box.TextLength;Box.SelectionLength=0;}
            if(m.Msg==0x8005 && Box!=null){Box.Text="123";Box.SelectionStart=Box.TextLength;Box.SelectionLength=0;}
            if(m.Msg==0x8006 && Box!=null){Box.Text="abc";Box.SelectionStart=Box.TextLength;Box.SelectionLength=0;}
            if(m.Msg==0x8007 && Box!=null){Box.Text="ghbdtn";Box.SelectionStart=Box.TextLength;Box.SelectionLength=0;}
            if(m.Msg==0x8008 && Box!=null){Box.Text="ghbdtn";Box.SelectionStart=3;Box.SelectionLength=0;}
            if(m.Msg==0x8009 && Box!=null){Box.UseSystemPasswordChar=true;Box.Clear();Box.Focus();}
            if(m.Msg==0x800A && Box!=null)Box.UseSystemPasswordChar=false;
            if(m.Msg==0x8010 && WpfBox!=null){WpfBox.Text="";WpfBox.CaretIndex=0;WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);}
            if(m.Msg==0x8011 && WpfBox!=null){WpfBox.Text="ghbdtn";WpfBox.CaretIndex=WpfBox.Text.Length;WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);}
            if(m.Msg==0x8012 && WpfBox!=null){Activate();WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);}
            if(m.Msg==0x8013 && WpfBox!=null){WpfBox.Text="привет how are";WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);WpfBox.SelectAll();}
            if(m.Msg==0x8014 && WpfBox!=null){WpfBox.Text="привет how are";WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);WpfBox.Select(7,3);}
            if(m.Msg==0x8015 && WpfBox!=null){WpfBox.Text="ghbdtn ghbdtn";WpfBox.Focus();System.Windows.Input.Keyboard.Focus(WpfBox);WpfBox.Select(6,0);}
            if(m.Msg==0x8020 && RichBox!=null){RichBox.Document.Blocks.Clear();RichBox.Document.Blocks.Add(new System.Windows.Documents.Paragraph());RichBox.Focus();System.Windows.Input.Keyboard.Focus(RichBox);}
            if(m.Msg==0x8021 && RichBox!=null){
                var first=new System.Windows.Documents.Run("KEEP "){FontWeight=System.Windows.FontWeights.Bold};
                var text=new System.Windows.Documents.Run("ghbdtn tail");var para=new System.Windows.Documents.Paragraph();para.Inlines.Add(first);para.Inlines.Add(text);
                RichBox.Document.Blocks.Clear();RichBox.Document.Blocks.Add(para);RichBox.Focus();System.Windows.Input.Keyboard.Focus(RichBox);RichBox.CaretPosition=text.ContentStart.GetPositionAtOffset(6);
            }
            if(m.Msg==0x8022 && RichBox!=null){Activate();RichBox.Focus();System.Windows.Input.Keyboard.Focus(RichBox);}
            if(m.Msg==0x8030 && CustomBox!=null){Activate();CustomBox.Document.Blocks.Clear();CustomBox.Document.Blocks.Add(new System.Windows.Documents.Paragraph());CustomBox.Focus();System.Windows.Input.Keyboard.Focus(CustomBox);}
            if(m.Msg==0x8031 && CustomBox!=null){
                CustomBox.Document.Blocks.Clear();var run=new System.Windows.Documents.Run("KEEP ghbdtn tail");
                CustomBox.Document.Blocks.Add(new System.Windows.Documents.Paragraph(run));CustomBox.Focus();System.Windows.Input.Keyboard.Focus(CustomBox);
                CustomBox.CaretPosition=run.ContentStart.GetPositionAtOffset(11);
            }
            base.WndProc(ref m);
        }
    }
    sealed class CustomEditor : System.Windows.Controls.RichTextBox {
        public string SelectReport;public int SelectCalls;
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer(){return new CustomPeer(this);}
    }
    sealed class CustomPeer : System.Windows.Automation.Peers.RichTextBoxAutomationPeer {
        readonly CustomEditor editor;
        public CustomPeer(CustomEditor owner):base(owner){editor=owner;}
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore(){return System.Windows.Automation.Peers.AutomationControlType.Custom;}
        public override object GetPattern(System.Windows.Automation.Peers.PatternInterface kind){
            var pattern=base.GetPattern(kind);
            return kind==System.Windows.Automation.Peers.PatternInterface.Text && pattern is ITextProvider?new BrokenSelectionProvider((ITextProvider)pattern,editor):pattern;
        }
    }
    // Real UIA provider with normal text/caret reads, but deliberately broken Select().
    // It reproduces editors that collapse a selected range to the document start.
    sealed class BrokenSelectionProvider : ITextProvider {
        readonly ITextProvider inner;readonly CustomEditor owner;
        public BrokenSelectionProvider(ITextProvider inner,CustomEditor owner){this.inner=inner;this.owner=owner;}
        ITextRangeProvider[] Wrap(ITextRangeProvider[] ranges){return Array.ConvertAll(ranges,r=>new BrokenSelectionRange(r,owner) as ITextRangeProvider);}
        public ITextRangeProvider DocumentRange{get{return new BrokenSelectionRange(inner.DocumentRange,owner);}}
        public SupportedTextSelection SupportedTextSelection{get{return inner.SupportedTextSelection;}}
        public ITextRangeProvider[] GetSelection(){return Wrap(inner.GetSelection());}
        public ITextRangeProvider[] GetVisibleRanges(){return Wrap(inner.GetVisibleRanges());}
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple child){return new BrokenSelectionRange(inner.RangeFromChild(child),owner);}
        public ITextRangeProvider RangeFromPoint(System.Windows.Point point){return new BrokenSelectionRange(inner.RangeFromPoint(point),owner);}
    }
    sealed class BrokenSelectionRange : ITextRangeProvider {
        readonly ITextRangeProvider inner;readonly CustomEditor owner;
        public BrokenSelectionRange(ITextRangeProvider inner,CustomEditor owner){this.inner=inner;this.owner=owner;}
        static ITextRangeProvider Unwrap(ITextRangeProvider range){var wrapped=range as BrokenSelectionRange;return wrapped==null?range:wrapped.inner;}
        ITextRangeProvider Wrap(ITextRangeProvider range){return range==null?null:new BrokenSelectionRange(range,owner);}
        public ITextRangeProvider Clone(){return Wrap(inner.Clone());}
        public bool Compare(ITextRangeProvider range){return inner.Compare(Unwrap(range));}
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint,ITextRangeProvider range,TextPatternRangeEndpoint other){return inner.CompareEndpoints(endpoint,Unwrap(range),other);}
        public void ExpandToEnclosingUnit(TextUnit unit){inner.ExpandToEnclosingUnit(unit);}
        public ITextRangeProvider FindAttribute(int attribute,object value,bool backward){return Wrap(inner.FindAttribute(attribute,value,backward));}
        public ITextRangeProvider FindText(string text,bool backward,bool ignoreCase){return Wrap(inner.FindText(text,backward,ignoreCase));}
        public object GetAttributeValue(int attribute){return inner.GetAttributeValue(attribute);}
        public double[] GetBoundingRectangles(){return inner.GetBoundingRectangles();}
        public IRawElementProviderSimple[] GetChildren(){return inner.GetChildren();}
        public IRawElementProviderSimple GetEnclosingElement(){return inner.GetEnclosingElement();}
        public string GetText(int max){return inner.GetText(max);}
        public int Move(TextUnit unit,int count){return inner.Move(unit,count);}
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint,ITextRangeProvider range,TextPatternRangeEndpoint other){inner.MoveEndpointByRange(endpoint,Unwrap(range),other);}
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint,TextUnit unit,int count){return inner.MoveEndpointByUnit(endpoint,unit,count);}
        public void Select(){owner.SelectCalls++;Write(owner.SelectReport,owner.SelectCalls.ToString());owner.Selection.Select(owner.Document.ContentStart,owner.Document.ContentStart);}
        public void AddToSelection(){Select();}
        public void RemoveFromSelection(){inner.RemoveFromSelection();}
        public void ScrollIntoView(bool align){inner.ScrollIntoView(align);}
    }
    sealed class IgnoringLayoutTextBox : TextBox {
        public bool DelayCharacters;
        protected override void WndProc(ref Message message){
            if(message.Msg==Native.WM_INPUTLANGCHANGEREQUEST){message.Result=IntPtr.Zero;return;}
            if(message.Msg==0x102 && DelayCharacters)System.Threading.Thread.Sleep(50);
            base.WndProc(ref message);
        }
    }
    public static int Target(string path){
        Native.LoadKeyboardLayout("00000409",1);
        var form=new TargetForm{Text="Fast Switcher input test",Width=430,Height=440,StartPosition=FormStartPosition.CenterScreen,LayoutReport=path+".layout-msgs"};
        var box=new IgnoringLayoutTextBox{Left=18,Top=26,Width=380,Font=new Font("Segoe UI",15)};
        form.Box=box;form.Controls.Add(box);box.TextChanged+=delegate{Write(path,box.Text);};
        var wpfBox=new System.Windows.Controls.TextBox{FontSize=20};
        var wpfHost=new System.Windows.Forms.Integration.ElementHost{Left=18,Top=82,Width=380,Height=38,Child=wpfBox};
        form.WpfBox=wpfBox;form.Controls.Add(wpfHost);wpfBox.TextChanged+=delegate{Write(path,wpfBox.Text);};
        wpfBox.SelectionChanged+=delegate{Write(path+".caret",wpfBox.CaretIndex+","+wpfBox.SelectionLength);};
        var rich=new System.Windows.Controls.RichTextBox{FontSize=18};form.RichBox=rich;
        var richHost=new System.Windows.Forms.Integration.ElementHost{Left=18,Top=135,Width=380,Height=130,Child=rich};form.Controls.Add(richHost);
        rich.TextChanged+=delegate{
            Write(path,new System.Windows.Documents.TextRange(rich.Document.ContentStart,rich.Document.ContentEnd).Text.TrimEnd('\r','\n'));
            var para=rich.Document.Blocks.FirstBlock as System.Windows.Documents.Paragraph;
            var first=para==null?null:para.Inlines.FirstInline;
            Write(path+".format",first!=null && first.FontWeight==System.Windows.FontWeights.Bold?"bold":"plain");
        };
        var custom=new CustomEditor{FontSize=18,SelectReport=path+".uia-selects"};form.CustomBox=custom;Write(custom.SelectReport,"0");
        form.Controls.Add(new System.Windows.Forms.Integration.ElementHost{Left=18,Top=275,Width=380,Height=100,Child=custom});
        custom.TextChanged+=delegate{Write(path,new System.Windows.Documents.TextRange(custom.Document.ContentStart,custom.Document.ContentEnd).Text.TrimEnd('\r','\n'));};
        form.Shown+=delegate{box.Focus();Write(path,"");File.WriteAllText(path+".ready",form.Handle.ToInt64().ToString());};
        Application.Run(form);return 0;
    }
}
}
#endif
