using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ValcoSetup
{
    /// <summary>Creates .lnk shortcuts through the shell's IShellLink.</summary>
    public static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, int flags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid clsid);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        // Just enough PROPVARIANT for a VT_LPWSTR (24 bytes on x64).
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

        // PKEY_AppUserModel_ID
        static PropertyKey AppUserModelIdKey = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };

        public static void Create(string lnkPath, string target, string args, string workDir, string description, string icon, string appUserModelId = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                var link = (IShellLinkW)new CShellLink();
                link.SetPath(target);
                link.SetArguments(args ?? "");
                link.SetWorkingDirectory(workDir ?? Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                if (!string.IsNullOrEmpty(icon)) link.SetIconLocation(icon, 0);
                if (!string.IsNullOrEmpty(appUserModelId))
                {
                    var pv = new PropVariant { Type = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(appUserModelId) };
                    try
                    {
                        var store = (IPropertyStore)link;
                        store.SetValue(ref AppUserModelIdKey, ref pv);
                        store.Commit();
                    }
                    catch { }
                    finally { Marshal.FreeCoTaskMem(pv.Pointer); }
                }
                ((IPersistFile)link).Save(lnkPath, true);
                Marshal.FinalReleaseComObject(link);
            }
            catch { }
        }
    }
}
