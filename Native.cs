using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FastSwitcher {
internal static class Native {
    public static readonly IntPtr OwnInputMarker=new IntPtr(0x46535754);
    public const int WH_KEYBOARD_LL=13, WH_MOUSE_LL=14, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105, WM_HOTKEY=0x312;
    public const int WM_INPUTLANGCHANGEREQUEST=0x50;
    public const int VK_SHIFT=0x10, VK_CONTROL=0x11, VK_MENU=0x12, VK_PAUSE=0x13;
    public const int VK_BACK=0x08, VK_SPACE=0x20, VK_RETURN=0x0D, VK_TAB=0x09;
    public const int VK_LEFT=0x25, VK_RIGHT=0x27, VK_UP=0x26, VK_DOWN=0x28;
    public const int MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8, MOD_NOREPEAT=0x4000;
    public const int KEYEVENTF_KEYUP=2, KEYEVENTF_UNICODE=4;
    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct Kbd { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO {
        public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,flags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint msg; public ushort low,high; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wp,IntPtr lp);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int count,[Out] IntPtr[] list);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern int ToUnicodeEx(uint vk,uint scan,byte[] state,StringBuilder buffer,int count,uint flags,IntPtr layout);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp,uint flags,uint milliseconds,out UIntPtr result);
    public static void RequestLayout(IntPtr focus,IntPtr window,IntPtr layout){
        UIntPtr result;
        if(SendMessageTimeout(focus,WM_INPUTLANGCHANGEREQUEST,IntPtr.Zero,layout,0x22,150,out result)==IntPtr.Zero)
            PostMessage(window,WM_INPUTLANGCHANGEREQUEST,IntPtr.Zero,layout);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd,StringBuilder name,int max);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string id,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr ActivateKeyboardLayout(IntPtr layout,uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,ref int start,ref int end);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr capacity,StringBuilder text);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr wp,string text);

    public static bool Down(int vk) { return (GetKeyState(vk)&0x8000)!=0; }
    public static bool AsyncDown(int vk) { return (GetAsyncKeyState(vk)&0x8000)!=0; }
    public static INPUT Key(ushort vk,bool up) { return new INPUT { type=1,u=new InputUnion {ki=new KEYBDINPUT {vk=vk,flags=(uint)(up?KEYEVENTF_KEYUP:0),extra=OwnInputMarker} } }; }
    public static INPUT Unicode(char c,bool up) { return new INPUT { type=1,u=new InputUnion {ki=new KEYBDINPUT {scan=c,flags=(uint)(KEYEVENTF_UNICODE|(up?KEYEVENTF_KEYUP:0)),extra=OwnInputMarker} } }; }
    public static bool Type(string value) {
        var inputs=new INPUT[value.Length*2];
        for(int i=0;i<value.Length;i++){ inputs[i*2]=Unicode(value[i],false); inputs[i*2+1]=Unicode(value[i],true); }
        return inputs.Length==0 || SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))==inputs.Length;
    }
    public static bool SelectPrevious(int characters){
        if(characters<1 || characters>4096 || AsyncDown(VK_SHIFT) || AsyncDown(VK_CONTROL) || AsyncDown(VK_MENU))return false;
        var inputs=new INPUT[characters*2+2];int at=0;inputs[at++]=Key(VK_SHIFT,false);
        for(int i=0;i<characters;i++){inputs[at++]=Key(VK_LEFT,false);inputs[at++]=Key(VK_LEFT,true);}
        inputs[at++]=Key(VK_SHIFT,true);
        return SendInput((uint)at,inputs,Marshal.SizeOf(typeof(INPUT)))==at;
    }
    public static string Class(IntPtr h) { var b=new StringBuilder(256); GetClassName(h,b,b.Capacity); return b.ToString(); }
    public struct EditSnapshot {public string Text;public int Start,End;}
    public static bool TryGetEditSnapshot(IntPtr hwnd,out EditSnapshot snapshot) {
        snapshot=new EditSnapshot();
        if(Class(hwnd).IndexOf("edit",StringComparison.OrdinalIgnoreCase)<0)return false;
        int start=0,end=0;SendMessage(hwnd,0xB0,ref start,ref end); // EM_GETSEL
        if(start<0 || end<start || end-start>4096)return false;
        int length=SendMessage(hwnd,0xE,IntPtr.Zero,IntPtr.Zero).ToInt32(); // WM_GETTEXTLENGTH
        if(length<end || length>1048576)return false;
        var buffer=new StringBuilder(length+1);
        SendMessage(hwnd,0xD,(IntPtr)(length+1),buffer); // WM_GETTEXT
        if(buffer.Length<end)return false;
        snapshot=new EditSnapshot{Text=buffer.ToString(),Start=start,End=end};return true;
    }
    public static string SelectedEditText(IntPtr hwnd) {
        EditSnapshot s;return TryGetEditSnapshot(hwnd,out s) && s.End>s.Start?s.Text.Substring(s.Start,s.End-s.Start):"";
    }
    public static bool ReplaceEditRange(IntPtr hwnd,int start,int end,string expected,string replacement,bool reselect){
        EditSnapshot before;if(!TryGetEditSnapshot(hwnd,out before) || start<0 || end<start || end>before.Text.Length || end-start>4096)return false;
        if(before.Text.Substring(start,end-start)!=expected)return false;
        if(before.Start!=start || before.End!=end)SendMessage(hwnd,0xB1,(IntPtr)start,(IntPtr)end); // EM_SETSEL
        SendMessage(hwnd,0xC2,(IntPtr)1,replacement); // EM_REPLACESEL; one undoable edit
        EditSnapshot after;if(!TryGetEditSnapshot(hwnd,out after))return false;
        string wanted=before.Text.Substring(0,start)+replacement+before.Text.Substring(end);
        if(after.Text!=wanted)return false;
        if(reselect)SendMessage(hwnd,0xB1,(IntPtr)start,(IntPtr)(start+replacement.Length));
        else SendMessage(hwnd,0xB1,(IntPtr)(start+replacement.Length),(IntPtr)(start+replacement.Length));
        return true;
    }
    public static bool ReplaceEditSelection(IntPtr hwnd,string expected,string replacement,bool reselect){
        EditSnapshot s;if(!TryGetEditSnapshot(hwnd,out s) || s.End<=s.Start)return false;
        return ReplaceEditRange(hwnd,s.Start,s.End,expected,replacement,reselect);
    }
    public static bool ReplaceEditTail(IntPtr hwnd,string expected,string replacement){
        EditSnapshot s;if(!TryGetEditSnapshot(hwnd,out s) || s.End!=s.Start || s.Start<expected.Length)return false;
        return ReplaceEditRange(hwnd,s.Start-expected.Length,s.Start,expected,replacement,false);
    }
    public static string Typed(uint vk,uint scan,IntPtr layout,bool shift) {
        // The low-level hook runs before the target thread's keyboard state changes.
        // Build the state explicitly instead of reading this process's stale state.
        int language=(int)((long)layout&0xffff);
        if(vk>=0x41 && vk<=0x5A && (language==0x0409 || language==0x0419)){
            const string ruByVk="фисвуапршолдьтщзйкыегмцчня";
            char c=language==0x0419?ruByVk[(int)(vk-0x41)]:(char)('a'+vk-0x41);
            bool caps=(GetKeyState(0x14)&1)!=0;
            return (shift^caps?char.ToUpperInvariant(c):c).ToString();
        }
        if(vk==VK_SPACE)return " ";
        var state=new byte[256];
        if(shift){state[VK_SHIFT]=0x80;state[0xA0]=0x80;state[0xA1]=0x80;}
        if((GetKeyState(0x14)&1)!=0)state[0x14]=1;
        var b=new StringBuilder(8);int n=ToUnicodeEx(vk,scan,state,b,b.Capacity,4,layout);
        return n==1 ? b.ToString() : "";
    }
}
}
