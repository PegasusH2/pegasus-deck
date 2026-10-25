using System.Security.AccessControl;
using System.Security.Principal;

namespace PegasusDeck.Service;

/// <summary>
/// La carpeta de datos (dispositivos, código de emparejamiento, certificados, y en la
/// fase 3 la lista blanca de apps) solo pueden modificarla SYSTEM y Administradores.
/// %ProgramData% deja crear archivos a cualquier usuario: sin esto, un usuario sin
/// privilegios podría emparejar dispositivos o plantar un pairing.json permanente.
/// </summary>
internal static class DataDirSecurity
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>ACL protegida (sin herencia): SYSTEM y Administradores con control total; nadie más.</summary>
    public static DirectorySecurity ProtectedAcl()
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.SetOwner(Admins);
        foreach (var sid in new[] { System, Admins })
        {
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        return acl;
    }

    /// <summary>Crea la carpeta protegida si no existe y comprueba que lo está. Devuelve el problema o null.</summary>
    public static string? EnsureProtected(string path)
    {
        try
        {
            if (!Directory.Exists(path)) new DirectoryInfo(path).Create(ProtectedAcl());
            return Check(new DirectoryInfo(path).GetAccessControl());
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            // Sin elevación no se puede crear la carpeta con Administradores como propietario: es lo esperado.
            return $"Sin permiso para crear o proteger {path}. Ejecuta como administrador (o como el servicio).";
        }
    }

    /// <summary>Comprobación pura sobre una ACL (para poder probarla sin privilegios).</summary>
    public static string? Check(DirectorySecurity acl)
    {
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !(owner.Equals(System) || owner.Equals(Admins)))
        {
            return $"El propietario de la carpeta de datos es {Describe(owner)}; debe ser SYSTEM o Administradores.";
        }
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & WriteRights) == 0) continue;
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid.Equals(System) || sid.Equals(Admins)) continue;
            return $"{Describe(sid)} puede modificar la carpeta de datos; solo deben poder SYSTEM y Administradores.";
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
