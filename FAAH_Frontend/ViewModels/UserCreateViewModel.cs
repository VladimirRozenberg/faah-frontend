using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Json;
using System.Windows.Input;

namespace FAAH_Frontend.ViewModels;

public class UserCreateViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _temporaryPassword = string.Empty;
    private string? _role;
    private string? _errorMessage;
    private bool _isBusy;

    public UserCreateViewModel(ShellViewModel shell)
    {
        _shell = shell;
        CreateCommand = new RelayCommand(Create, _ => !IsBusy);
        CancelCommand = new RelayCommand(shell.ShowUsers);
    }

    // Les seuls champs que le backend accepte (POST /admin/utilisateurs).
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string Email { get => _email; set => SetField(ref _email, value); }
    public string TemporaryPassword { get => _temporaryPassword; set => SetField(ref _temporaryPassword, value); }
    public string? Role { get => _role; set => SetField(ref _role, value); }

    // Doivent correspondre exactement aux valeurs acceptees par le backend.
    public ObservableCollection<string> Roles { get; } = new() { "admin", "employe" };

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>Vrai pendant l'appel reseau : desactive le bouton Create User.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand CreateCommand { get; }
    public ICommand CancelCommand { get; }

    private async void Create(object? parameter)
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(TemporaryPassword) ||
            string.IsNullOrWhiteSpace(Role))
        {
            ErrorMessage = "Remplis username, email, mot de passe et role.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            // Reserve aux admins cote backend (meme regle que la liste des utilisateurs).
            var response = await _shell.Http.PostAsJsonAsync(
                "/admin/utilisateurs",
                new { username = Username, email = Email, password = TemporaryPassword, role = Role },
                ShellViewModel.JsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = response.StatusCode == HttpStatusCode.Conflict
                    ? "Ce nom d'utilisateur ou cet email est deja pris."
                    : response.StatusCode == HttpStatusCode.Forbidden
                        ? "Reserve aux administrateurs."
                        : $"Erreur ({(int)response.StatusCode}) lors de la creation.";
                return;
            }

            // Cree avec succes : retour a la liste (qui se recharge depuis l'API).
            _shell.ShowUsers();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERREUR CREATE USER : {ex}");
            ErrorMessage = "Impossible de contacter le serveur FAAH. Verifie qu'il est demarre.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
