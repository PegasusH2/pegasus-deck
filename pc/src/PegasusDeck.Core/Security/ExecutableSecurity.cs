using System.Security.AccessControl;
using System.Security.Principal;

namespace PegasusDeck.Core.Security;

/// <summary>
/// ¿Puede un usuario sin privilegios sustituir este ejecutable? Si el archivo o su carpeta
/// son modificables por alguien que no sea SYSTEM, Administradores o TrustedInstaller,
/// lanzarlo desde Pegasus Deck ejecutaría lo que ese usuario haya puesto allí.
/// </summary>
public static class ExecutableSecurity
{
    private static readonly SecurityIdentifier[] Trusted =
    [
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // NT SERVICE\TrustedInstaller
    ];

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>null si solo administradores pueden modificarlo; si no, el motivo.</summary>
    public static string? Problem(string exePath)
    {
        try
        {
            var file = new FileInfo(exePath);
            if (!file.Exists) return "el ejecutable no existe";
            return Check(file.GetAccessControl(), "el ejecutable") ?? Check(file.Directory!.GetAccessControl(), "su carpeta");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return "no se pueden leer sus permisos";
        }
    }

    public static string? Check(FileSystemSecurity acl, string what)
    {
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !Trusted.Contains(owner)) return $"{what} pertenece a {Describe(owner)}";
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & WriteRights) == 0) continue;
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue; // no se aplica a este objeto
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (Trusted.Contains(sid)) continue;
            return $"{Describe(sid)} puede modificar {what}";
        }
        return null;
    }

    private static string Describe(SecurityIdentifier? sid)
    {
        if (sid is null) return "desconocido";
        try { return sid.Translate(typeof(NTAccount)).Value; }
        catch (IdentityNotMappedException) { return sid.Value; }
    }
}
