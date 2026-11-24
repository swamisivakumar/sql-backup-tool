using System.Security.Cryptography;
using System.Text;

namespace SqlBackupTool.Core;

/// <summary>
/// Encrypts connection strings with Windows DPAPI.
/// User scope: only the same Windows user on the same machine can decrypt.
/// Machine scope: any user on the same machine can decrypt (needed when a scheduled task runs as another account).
/// </summary>
public static class SecretProtector
{
    private const string UserPrefix = "dpapi:user:";
    private const string MachinePrefix = "dpapi:machine:";
    private static readonly byte[] Entropy = "SqlBackupTool.v1"u8.ToArray();

    public static bool IsProtected(string? value) =>
        value is not null && (value.StartsWith(UserPrefix, StringComparison.Ordinal) || value.StartsWith(MachinePrefix, StringComparison.Ordinal));

    public static bool IsMachineScope(string? value) => value?.StartsWith(MachinePrefix, StringComparison.Ordinal) == true;

    public static string Protect(string plain, bool machineScope)
    {
        var scope = machineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, scope);
        return (machineScope ? MachinePrefix : UserPrefix) + Convert.ToBase64String(data);
    }

    public static string Unprotect(string value)
    {
        if (!IsProtected(value)) return value;
        var machine = IsMachineScope(value);
        var payload = value[(machine ? MachinePrefix : UserPrefix).Length..];
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(payload), Entropy,
                machine ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidOperationException(
                "The encrypted connection string cannot be decrypted. It was encrypted by a different Windows " +
                (machine ? "machine" : "user or machine") + ". Re-enter it in Settings.", ex);
        }
    }
}
