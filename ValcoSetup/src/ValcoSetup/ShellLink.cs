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

        public static void Create(string lnkPath, string target, string args, string workDir, string description, string icon)
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
                ((IPersistFile)link).Save(lnkPath, true);
                Marshal.FinalReleaseComObject(link);
            }
            catch { }
        }
    }
}
