using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class PersonalInformationViewModel
{
    public PersonalInformationViewModel(User user, ShellViewModel shell)
    {
        UserId = user.UserId;
        Username = user.Username;
        Role = user.Role;
        Email = string.IsNullOrWhiteSpace(user.Email) ? "Not provided" : user.Email;
        BackCommand = new RelayCommand(shell.ShowUsers);
    }

    public int UserId { get; }
    public string Username { get; }
    public string Role { get; }
    public string Email { get; }
    public ICommand BackCommand { get; }
}
