using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using OpenOverlay.Core.Abstractions;

namespace OpenOverlay.Infrastructure.Security;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;
    private const string Prefix = "OpenOverlay/";

    public string? Read(string name)
    {
        if (!CredRead(Prefix + name, Generic, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error, "Could not read a credential from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return string.Empty;
            var characterCount = checked((int)credential.CredentialBlobSize / sizeof(char));
            return Marshal.PtrToStringUni(credential.CredentialBlob, characterCount)?.TrimEnd('\0');
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Write(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = checked((uint)(value.Length * sizeof(char)));
        if (bytes > 5120) throw new ArgumentOutOfRangeException(nameof(value), "Credential values must not exceed 5120 bytes.");

        var blob = Marshal.StringToHGlobalUni(value);
        try
        {
            var credential = new NativeCredential
            {
                Type = Generic,
                TargetName = Prefix + name,
                CredentialBlobSize = bytes,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save a credential in Windows Credential Manager.");
        }
        finally
        {
            for (var offset = 0; offset < value.Length * sizeof(char); offset += sizeof(char)) Marshal.WriteInt16(blob, offset, 0);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string name)
    {
        if (CredDelete(Prefix + name, Generic, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != 1168) throw new Win32Exception(error, "Could not delete a credential from Windows Credential Manager.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
