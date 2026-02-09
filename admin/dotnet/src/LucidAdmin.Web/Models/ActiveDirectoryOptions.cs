namespace LucidAdmin.Web.Models;

public class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    public bool Enabled { get; set; } = false;
    public string Domain { get; set; } = "";
    public string LdapServer { get; set; } = "";
    public int LdapPort { get; set; } = 389;
    public bool UseLdaps { get; set; } = false;
    public string SearchBase { get; set; } = "";
    public string BindUserDn { get; set; } = "";
    public string BindPasswordEnvVar { get; set; } = "LUCID_AD_BIND_PASSWORD";
    public RoleMappingOptions RoleMapping { get; set; } = new();
    public string DefaultRole { get; set; } = "Viewer";
    public bool RequireRoleGroup { get; set; } = false;
}

public class RoleMappingOptions
{
    public string AdminGroup { get; set; } = "LucidAdmin-Admins";
    public string OperatorGroup { get; set; } = "LucidAdmin-Operators";
    public string ViewerGroup { get; set; } = "LucidAdmin-Viewers";
}
