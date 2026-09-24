using FAAH_Frontend.ViewModels;
using System.Text.Json.Serialization;
namespace FAAH_Frontend.Models;
public class User : ViewModelBase
{
    public int UserId { get; set; }
    public decimal? Balance { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    private string _role = "";
    private bool? _active;
    public string Role { get=>_role; set { if(SetField(ref _role,value)) { OnPropertyChanged(nameof(IsAdminRole)); OnPropertyChanged(nameof(RoleDisplay)); OnPropertyChanged(nameof(RoleAction)); } } }
    public bool? IsActive { get=>_active; set { if(SetField(ref _active,value)) { OnPropertyChanged(nameof(StatusDisplay)); OnPropertyChanged(nameof(StatusAction)); OnPropertyChanged(nameof(IsEnabled)); OnPropertyChanged(nameof(IsDisabled)); } } }
    [JsonIgnore] public bool IsEnabled => IsActive == true;
    [JsonIgnore] public bool IsDisabled => IsActive == false;
    public bool IsAdminRole => Role == "admin";
    public string RoleDisplay => Role == "employe" ? "EMPLOYEE" : Role.ToUpperInvariant();
    [JsonIgnore] public string RoleAction => IsAdminRole ? "Make employee" : "Make admin";
    [JsonIgnore] public string StatusDisplay => IsActive switch { true=>"ACTIVE", false=>"DISABLED", _=>"UNKNOWN" };
    [JsonIgnore] public string StatusAction => IsActive switch { true=>"Deactivate", false=>"Activate", _=>"Unavailable" };
}
