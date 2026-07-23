namespace FamilyPortal.Domain.Enums;

/// <summary>
/// 使用者角色列舉 — RBAC 權限控制的基礎
/// </summary>
public enum UserRole
{
    /// <summary>一般使用者，僅有瀏覽權限</summary>
    User = 0,

    /// <summary>管理員，擁有完整 CRUD 權限</summary>
    Admin = 1
}
