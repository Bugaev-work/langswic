using System;
using System.Runtime.InteropServices;

namespace FastSwitcher {
// Windows' installed spelling providers. Calls belong on the owning UI thread,
// outside WH_KEYBOARD_LL callbacks. No words are added to the system dictionary.
public sealed class WindowsSpelling : IDisposable {
    [ComImport,Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFactory {
        void SupportedLanguages(out IntPtr languages);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string tag,[MarshalAs(UnmanagedType.Bool)] out bool supported);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string tag,out IChecker checker);
    }
    [ComImport,Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IChecker {
        void LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string tag);
        void Check([MarshalAs(UnmanagedType.LPWStr)] string word,out IErrors errors);
    }
    [ComImport,Guid("803E3BD4-2828-4410-8290-418D1D73C762"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IErrors { [PreserveSig] int Next(out IntPtr error); }
    IChecker russian,english;
    readonly int thread=System.Threading.Thread.CurrentThread.ManagedThreadId;
    public bool RussianAvailable {get{return russian!=null;}}
    public bool EnglishAvailable {get{return english!=null;}}
    public string Status {get{return "Словари Windows: RU "+(RussianAvailable?"доступен":"не установлен")+", EN "+(EnglishAvailable?"доступен":"не установлен");}}
    public WindowsSpelling(){
        IFactory factory=null;
        try {
            factory=(IFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")));
            russian=Create(factory,"ru-RU");english=Create(factory,"en-US");
        }catch(COMException){}catch(PlatformNotSupportedException){}catch(System.IO.IOException){}
        finally{Release(factory);}
    }
    static IChecker Create(IFactory factory,string tag){
        try{bool supported;factory.IsSupported(tag,out supported);if(!supported)return null;
            IChecker checker;factory.CreateSpellChecker(tag,out checker);return checker;
        }catch(COMException){return null;}catch(System.IO.IOException){return null;}
    }
    public bool TryIsWord(bool latin,string word,out bool known){
        known=false;var checker=latin?english:russian;
        if(checker==null || thread!=System.Threading.Thread.CurrentThread.ManagedThreadId || string.IsNullOrEmpty(word))return false;
        IErrors errors=null;IntPtr error=IntPtr.Zero;
        try{
            checker.Check(word.ToLowerInvariant(),out errors);
            if(errors==null)return false;
            int result=errors.Next(out error);
            if(result<0)return false;
            known=result==1 && error==IntPtr.Zero;return true;
        }catch(COMException){return false;}catch(System.IO.IOException){return false;}
        finally{if(error!=IntPtr.Zero)Marshal.Release(error);Release(errors);}
    }
    static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    public void Dispose(){Release(russian);Release(english);russian=null;english=null;}
}
}
