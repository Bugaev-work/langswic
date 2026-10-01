using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FastSwitcher {
internal sealed class LayoutService : IDisposable {
    IntPtr module,callback;
    public int LastError {get;private set;}
    public bool Activate(IntPtr focus,IntPtr window,IntPtr layout){
        if(Native.GetForegroundWindow()!=window)return false;
        uint pid;uint thread=Native.GetWindowThreadProcessId(focus,out pid);
        if(thread==0)return false;
        IntPtr hook=IntPtr.Zero;
        try{
            EnsureModule();
            if(module==IntPtr.Zero || callback==IntPtr.Zero)return false;
            hook=SetHook(4,callback,module,thread);
            if(hook==IntPtr.Zero){LastError=Marshal.GetLastWin32Error();return false;}
            if(Native.GetForegroundWindow()!=window)return false;
            uint command=RegisterWindowMessage("FastSwitcher.ActivateInstalledLayout.v1");UIntPtr result;
            if(command==0 || SendMessageTimeout(focus,command,window,layout,2,100,out result)==IntPtr.Zero){LastError=Marshal.GetLastWin32Error();return false;}
            LastError=0;
            return Native.GetKeyboardLayout(thread)==layout;
        }catch(IOException){LastError=5;return false;}
        catch(UnauthorizedAccessException){LastError=5;return false;}
        finally{if(hook!=IntPtr.Zero)Native.UnhookWindowsHookEx(hook);}
    }
    void EnsureModule(){
        if(module!=IntPtr.Zero)return;
        byte[] bytes;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FastSwitcher.NativeLayout64")){
            if(stream==null){LastError=2;return;}
            using(var buffer=new MemoryStream()){stream.CopyTo(buffer);bytes=buffer.ToArray();}
        }
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FastSwitcher.Layout."+hash+".dll");
        if(File.Exists(path)){
            using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","")!=hash){LastError=13;return;}
        }else{
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))file.Write(bytes,0,bytes.Length);
        }
        module=LoadLibraryEx(path,IntPtr.Zero,0x100|0x800);
        if(module==IntPtr.Zero){LastError=Marshal.GetLastWin32Error();return;}
        callback=GetProcAddress(module,"LayoutHook");
        if(callback==IntPtr.Zero)LastError=Marshal.GetLastWin32Error();
    }
    public void Dispose(){if(module!=IntPtr.Zero)FreeLibrary(module);module=callback=IntPtr.Zero;}
    [DllImport("user32.dll",EntryPoint="SetWindowsHookExW",SetLastError=true)]static extern IntPtr SetHook(int id,IntPtr callback,IntPtr module,uint thread);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp,uint flags,uint milliseconds,out UIntPtr result);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)]static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
}
}
