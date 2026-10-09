using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SateAIs.Core.Credentials;

/// <summary>
/// Generic credentials in the Windows Credential Manager, named the way Python's keyring names
/// them (target = service, user name = account) so one stored key serves every SateAIs client.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCredentials
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? Read(string service, string account)
    {
        // keyring stores under the service name, and moves an entry to "account@service" on a collision.
        var primary = ReadTarget(service);
        if (primary is { } found && (found.UserName is null || found.UserName == account))
        {
            return found.Secret;
        }
        return ReadTarget($"{account}@{service}")?.Secret;
    }

    public static void Write(string service, string account, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var credential = new Credential
        {
            Type = CredTypeGeneric,
            TargetName = Marshal.StringToCoTaskMemUni(service),
            UserName = Marshal.StringToCoTaskMemUni(account),
            Comment = Marshal.StringToCoTaskMemUni("Stored by SateAIs for ArcGIS Pro"),
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
            Persist = CredPersistLocalMachine,
        };
        try
        {
            Marshal.Copy(blob, 0, credential.CredentialBlob, blob.Length);
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Could not store the API key (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.UserName);
            Marshal.FreeCoTaskMem(credential.Comment);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
        }
    }

    public static void Delete(string service, string account)
    {
        CredDelete(service, CredTypeGeneric, 0);
        CredDelete($"{account}@{service}", CredTypeGeneric, 0);
    }

    private static (string Secret, string? UserName)? ReadTarget(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var handle))
        {
            return null; // ErrorNotFound, or a store we cannot use; either way there is no key here
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            var bytes = new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            }
            var secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            var userName = credential.UserName == IntPtr.Zero ? null : Marshal.PtrToStringUni(credential.UserName);
            return secret.Length == 0 ? null : (secret, userName);
        }
        finally
        {
            CredFree(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
