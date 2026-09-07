namespace FAAH_Frontend.Models;

// Correspond exactement a ce que renvoie GET /admin/utilisateurs cote FastAPI :
// { "user_id": 1, "username": "...", "role": "admin" | "employe" }
public class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    // ---- Proprietes d'affichage ----

    public bool IsAdminRole => Role == "admin";

    public string RoleDisplay => Role.ToUpperInvariant();
}
