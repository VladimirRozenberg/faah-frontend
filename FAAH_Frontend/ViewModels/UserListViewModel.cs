using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class UserListViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public UserListViewModel(ShellViewModel shell)
    {
        _shell = shell;
        NewUserCommand = new RelayCommand(shell.ShowUserCreate);
    }

    public ICommand NewUserCommand { get; }

    // Vide au depart : rempli par ChargerUtilisateursAsync() depuis l'API.
    public ObservableCollection<User> Users { get; } = new();

    public string PageLabel => "Page 1 of 1";

    /// <summary>Charge la vraie liste depuis GET /admin/utilisateurs (reserve aux admins).</summary>
    public async Task ChargerUtilisateursAsync()
    {
        try
        {
            var response = await _shell.Http.GetAsync("/admin/utilisateurs");
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ERREUR CHARGEMENT USERS : GET /admin/utilisateurs a repondu {(int)response.StatusCode}");
                return;
            }

            var utilisateurs = await response.Content.ReadFromJsonAsync<List<User>>(ShellViewModel.JsonOptions);

            Users.Clear();
            if (utilisateurs is not null)
            {
                foreach (var utilisateur in utilisateurs)
                    Users.Add(utilisateur);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERREUR CHARGEMENT USERS : {ex}");
        }
    }
}
