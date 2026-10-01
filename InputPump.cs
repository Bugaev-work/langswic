using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FastSwitcher {
// This thread only copies key metadata. It never reads text or calls UI Automation.
internal sealed class InputPump : IDisposable {
    internal sealed class KeyEvent {
        public Native.Kbd Key; public bool Down,Up; public IntPtr Window,Focus,Layout; public long Generation;
    }
    readonly Action<KeyEvent> deliver;readonly Action mouse;readonly Func<bool> capturePause;
    Thread thread;uint threadId;IntPtr keyboardHook,mouseHook;
    Native.HookProc keyboardCallback,mouseCallback;
    long generation,layoutIntentGeneration;readonly ManualResetEvent ready=new ManualResetEvent(false);
    bool control,alt;
    public bool Active {get{return keyboardHook!=IntPtr.Zero;}}
    public long Generation {get{return Interlocked.Read(ref generation);}}
    public long LayoutIntentGeneration {get{return Interlocked.Read(ref layoutIntentGeneration);}}
    public void Invalidate(){Interlocked.Increment(ref generation);}
    public InputPump(Action<KeyEvent> deliver,Action mouse,Func<bool> capturePause){this.deliver=deliver;this.mouse=mouse;this.capturePause=capturePause;}
    public void Start(){
        if(thread!=null)return;
        ready.Reset();thread=new Thread(Run){IsBackground=true,Name="Fast Switcher keyboard hook"};thread.Start();ready.WaitOne(1500);
    }
    void Run(){
        threadId=GetCurrentThreadId();MSG msg;PeekMessage(out msg,IntPtr.Zero,0,0,0);
        keyboardCallback=OnKey;mouseCallback=OnMouse;
        keyboardHook=Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL,keyboardCallback,Native.GetModuleHandle(null),0);
        mouseHook=Native.SetWindowsHookEx(Native.WH_MOUSE_LL,mouseCallback,Native.GetModuleHandle(null),0);ready.Set();
        try{while(GetMessage(out msg,IntPtr.Zero,0,0)>0){TranslateMessage(ref msg);DispatchMessage(ref msg);}}
        finally{
            if(keyboardHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(keyboardHook);
            if(mouseHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(mouseHook);
            keyboardHook=IntPtr.Zero;mouseHook=IntPtr.Zero;keyboardCallback=null;mouseCallback=null;
        }
    }
    IntPtr OnKey(int code,IntPtr wp,IntPtr lp){
        if(code>=0)try{
            bool down=wp==(IntPtr)Native.WM_KEYDOWN || wp==(IntPtr)Native.WM_SYSKEYDOWN;
            bool up=wp==(IntPtr)Native.WM_KEYUP || wp==(IntPtr)Native.WM_SYSKEYUP;
            var key=(Native.Kbd)Marshal.PtrToStructure(lp,typeof(Native.Kbd));
            bool accept=(key.flags&0x10)==0;
#if INPUT_TEST
            accept=accept || InputService.AcceptSyntheticInput;
#endif
            if((down||up) && accept && key.extra!=Native.OwnInputMarker){
                if(key.vk==Native.VK_CONTROL || key.vk==0xA2 || key.vk==0xA3)control=down;
                if(key.vk==Native.VK_MENU || key.vk==0xA4 || key.vk==0xA5)alt=down;
                bool modifier=key.vk==Native.VK_SHIFT || key.vk==0xA0 || key.vk==0xA1 || key.vk==Native.VK_CONTROL || key.vk==0xA2 || key.vk==0xA3 || key.vk==Native.VK_MENU || key.vk==0xA4 || key.vk==0xA5;
                bool command=key.vk==Native.VK_PAUSE || (control && alt && key.vk>=0x70 && key.vk<=0x7B);
                if(down && !modifier && !command)Invalidate();
                // Ordinary letters must not cancel a pending layout change.
                // Explicit layout shortcuts, navigation and focus changes do.
                if(down && (control || alt || key.vk==0x5B || key.vk==0x5C ||
                    key.vk==Native.VK_LEFT || key.vk==Native.VK_RIGHT || key.vk==Native.VK_UP || key.vk==Native.VK_DOWN ||
                    key.vk==Native.VK_TAB || key.vk==Native.VK_RETURN || key.vk==0x24 || key.vk==0x23))
                    Interlocked.Increment(ref layoutIntentGeneration);
                IntPtr window=Native.GetForegroundWindow();uint pid;uint id=Native.GetWindowThreadProcessId(window,out pid);
                var info=new Native.GUITHREADINFO{cbSize=Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
                if(Native.GetGUIThreadInfo(id,ref info)){
                    uint focusPid;uint focusThread=Native.GetWindowThreadProcessId(info.hwndFocus,out focusPid);
                    deliver(new KeyEvent{Key=key,Down=down,Up=up,Window=window,Focus=info.hwndFocus,Layout=Native.GetKeyboardLayout(focusThread==0?id:focusThread),Generation=Generation});
                }
                if(key.vk==Native.VK_PAUSE && capturePause())return (IntPtr)1;
            }
        }catch{}
        return Native.CallNextHookEx(keyboardHook,code,wp,lp);
    }
    IntPtr OnMouse(int code,IntPtr wp,IntPtr lp){
        if(code>=0 && (wp==(IntPtr)0x201 || wp==(IntPtr)0x204 || wp==(IntPtr)0x207)){Interlocked.Increment(ref layoutIntentGeneration);Invalidate();mouse();}
        return Native.CallNextHookEx(mouseHook,code,wp,lp);
    }
    public void Stop(){
        Invalidate();var old=thread;if(old==null)return;
        PostThreadMessage(threadId,0x12,IntPtr.Zero,IntPtr.Zero);old.Join(1500);thread=null;
    }
    public void Dispose(){Stop();ready.Dispose();}
    [StructLayout(LayoutKind.Sequential)] struct MSG {public IntPtr hwnd;public uint message;public IntPtr wParam,lParam;public uint time;public int x,y;public uint privateValue;}
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern bool PeekMessage(out MSG msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")]static extern int GetMessage(out MSG msg,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")]static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")]static extern bool PostThreadMessage(uint id,uint msg,IntPtr wp,IntPtr lp);
}
internal sealed class WorkTimer : IDisposable {
    readonly Action<Action> dispatch;readonly Action<WorkTimer> remove;System.Threading.Timer timer;int pending,disposed;
    public int Interval;public event EventHandler Tick;
    public WorkTimer(Action<Action> dispatch,Action<WorkTimer> remove,int interval){this.dispatch=dispatch;this.remove=remove;Interval=interval;}
    public void Start(){timer=new System.Threading.Timer(delegate{
        if(Interlocked.CompareExchange(ref pending,1,0)!=0)return;
        dispatch(delegate{Interlocked.Exchange(ref pending,0);if(disposed==0 && Tick!=null)Tick(this,EventArgs.Empty);});
    },null,Interval,Interval);}
    public void Stop(){if(timer!=null)timer.Change(Timeout.Infinite,Timeout.Infinite);}
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;if(timer!=null)timer.Dispose();remove(this);}
}
}
