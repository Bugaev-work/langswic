using System;
using System.Runtime.InteropServices;
namespace FastSwitcher {
internal static class TaskbarIdentity {
    const string AppId="Langswic.Desktop";
    public static void Initialize(){
#if INPUT_TEST
        SetCurrentProcessExplicitAppUserModelID("Langswic.InputTests");
#else
        SetCurrentProcessExplicitAppUserModelID(AppId);
#endif
    }
    public static void RefreshIcons(){SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);}
    public static IntPtr WindowIcon(IntPtr window){return SendMessage(window,0x7f,(IntPtr)1,IntPtr.Zero);}
    public static void SetShortcutId(string path){
        object shellLink=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        IntPtr value=IntPtr.Zero;
        try{
            var file=(System.Runtime.InteropServices.ComTypes.IPersistFile)shellLink;file.Load(path,2);
            var store=(IPropertyStore)shellLink;var key=new PropertyKey{Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};
            value=Marshal.StringToCoTaskMemUni(AppId);var variant=new PropVariant{Type=31,Pointer=value};
            Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref variant));Marshal.ThrowExceptionForHR(store.Commit());file.Save(path,true);
        }finally{if(value!=IntPtr.Zero)Marshal.FreeCoTaskMem(value);Marshal.FinalReleaseComObject(shellLink);}
    }
    public static string ShortcutId(string path){
        object shellLink=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        var variant=new PropVariant();
        try{((System.Runtime.InteropServices.ComTypes.IPersistFile)shellLink).Load(path,0);var key=new PropertyKey{Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};Marshal.ThrowExceptionForHR(((IPropertyStore)shellLink).GetValue(ref key,out variant));return variant.Type==31?Marshal.PtrToStringUni(variant.Pointer):"";}
        finally{PropVariantClear(ref variant);Marshal.FinalReleaseComObject(shellLink);}
    }
    [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid Format;public uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore{
        [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);[PreserveSig]int Commit();
    }
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")]static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
}
}
