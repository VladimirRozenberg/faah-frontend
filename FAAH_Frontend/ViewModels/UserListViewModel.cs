using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;
namespace FAAH_Frontend.ViewModels;
public sealed class UserListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly int _currentId;
    private readonly CancellationTokenSource _lifetime=new();
    private bool _busy, _disposed;
    private string _message="";
    private List<User> _allUsers = new();
    private int _page = 1;
    private string _pageInput = "1";
    private const int PageSize = 10;
    public int PageCount => Math.Max(1, (_allUsers.Count + PageSize - 1) / PageSize);
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand GoToPageCommand { get; }
    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 1, PageCount);
        Users.Clear();
        var visibleUsers = _allUsers.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        for (var index = 0; index < visibleUsers.Count; index++)
        {
            visibleUsers[index].IsAlternateRow = index % 2 == 1;
            Users.Add(visibleUsers[index]);
        }
        PageInput = _page.ToString();
        UpdatePageNumbers();
        OnPropertyChanged(nameof(PageLabel));
        foreach (var command in new[] { FirstPageCommand, PreviousPageCommand, NextPageCommand, LastPageCommand, GoToPageCommand }) ((RelayCommand)command).RaiseCanExecuteChanged();
    }
    public UserListViewModel(ShellViewModel shell) : this(shell.Http,int.TryParse(shell.ProfileUserId,out var id)?id:0,shell.ShowUserCreate,shell.ShowUserInformation) { }
    public UserListViewModel(HttpClient http,int currentId,Action create,Action<User> open)
    {
        _http=http; _currentId=currentId;
        FirstPageCommand = new RelayCommand(p => ShowPage(1), p => !IsBusy && !_disposed && _page > 1);
        PreviousPageCommand = new RelayCommand(p => ShowPage(_page - 1), p => !IsBusy && !_disposed && _page > 1);
        NextPageCommand = new RelayCommand(p => ShowPage(_page + 1), p => !IsBusy && !_disposed && _page < PageCount);
        LastPageCommand = new RelayCommand(p => ShowPage(PageCount), p => !IsBusy && !_disposed && _page < PageCount);
        GoToPageCommand = new RelayCommand(GoToPage, p => !IsBusy && !_disposed && TryGetPage(p, out var page) && page >= 1 && page <= PageCount && page != _page);
        NewUserCommand=new RelayCommand(p=>create(),p=>!IsBusy && !_disposed);
        OpenUserCommand=new RelayCommand(p=>{if(p is User user) open(user);},p=>!IsBusy && !_disposed);
        RefreshCommand=new RelayCommand(p=>{_ = ChargerUtilisateursAsync();},p=>!IsBusy && !_disposed);
        ChangeRoleCommand=new RelayCommand(p=>{if(p is User user) _=UpdateAsync(user,true);},p=>CanEdit(p) && p is User u && (u.Role=="admin" || u.Role=="employe"));
        ToggleStatusCommand=new RelayCommand(p=>{if(p is User user) _=UpdateAsync(user,false);},p=>CanEdit(p) && p is User u && u.IsActive.HasValue);
    }
    public ObservableCollection<User> Users { get; }=new();
    public ObservableCollection<int> PageNumbers { get; }=new();
    public ICommand NewUserCommand { get; }
    public ICommand OpenUserCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ChangeRoleCommand { get; }
    public ICommand ToggleStatusCommand { get; }
    public bool IsBusy { get=>_busy; private set {SetField(ref _busy,value); foreach(var c in new[]{NewUserCommand,OpenUserCommand,RefreshCommand,ChangeRoleCommand,ToggleStatusCommand,FirstPageCommand,PreviousPageCommand,NextPageCommand,LastPageCommand,GoToPageCommand}) ((RelayCommand)c).RaiseCanExecuteChanged();} }
    public string Message { get=>_message; private set {SetField(ref _message,value);OnPropertyChanged(nameof(HasMessage));} }
    public bool HasMessage=>Message.Length>0;
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel=>$"Page {_page} of {PageCount} · {_allUsers.Count} users";
    private void UpdatePageNumbers()
    {
        const int windowSize = 9;
        var maxStart = Math.Max(1, PageCount - windowSize + 1);
        var start = Math.Clamp(_page - 2, 1, maxStart);
        PageNumbers.Clear();
        for (var page = start; page < start + windowSize && page <= PageCount; page++) PageNumbers.Add(page);
    }
    private void GoToPage(object? parameter)
    {
        if (!TryGetPage(parameter, out var page) || page < 1 || page > PageCount || page == _page) return;
        ShowPage(page);
    }
    private static bool TryGetPage(object? parameter, out int page) => int.TryParse(parameter?.ToString(), out page);
    private bool CanEdit(object? p)=>!IsBusy && !_disposed && _currentId>0 && p is User u && u.UserId!=_currentId && Users.Contains(u);
    public async Task ChargerUtilisateursAsync()
    {
        if(IsBusy || _disposed) return;
        IsBusy=true;Message="";
        try {
            using var response=await _http.GetAsync("admin/utilisateurs",_lifetime.Token); Ensure(response);
            var users=await response.Content.ReadFromJsonAsync<List<User>>(ShellViewModel.JsonOptions,_lifetime.Token) ?? throw new JsonException();
            if(_disposed) return;
            _allUsers = users; ShowPage(_page);
            OnPropertyChanged(nameof(PageLabel));
            if(users.Exists(u=>u.IsActive is null)) Message="Account status is unavailable. Deploy the updated backend to enable activation controls.";
            else if(_currentId==0) Message="Your account identity is unavailable. Please sign in again to edit users.";
        } catch(Exception ex) when(Expected(ex)) {Report(ex);} finally {IsBusy=false;}
    }
    public async Task UpdateAsync(User user,bool role)
    {
        if(!CanEdit(user) || (role ? user.Role!="admin" && user.Role!="employe" : user.IsActive is null)) return;
        IsBusy=true;Message="";
        try {
            object body=role ? new {role=user.IsAdminRole?"employe":"admin"} : new {is_active=!user.IsActive!.Value};
            var suffix=role?"role":"statut";
            using var response=await _http.PutAsJsonAsync($"admin/utilisateurs/{user.UserId}/{suffix}",body,ShellViewModel.JsonOptions,_lifetime.Token); Ensure(response);
            var updated=await response.Content.ReadFromJsonAsync<User>(ShellViewModel.JsonOptions,_lifetime.Token) ?? throw new JsonException();
            if(updated.UserId!=user.UserId) throw new JsonException();
            if(_disposed) return;
            user.Role=updated.Role;user.IsActive=updated.IsActive;user.Email=updated.Email;
            Message=$"Account updated: {user.Username}.";
        } catch(Exception ex) when(Expected(ex)) {Report(ex);} finally {IsBusy=false;}
    }
    private static bool Expected(Exception ex)=>ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException;
    private static void Ensure(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch {
            HttpStatusCode.Unauthorized=>"Session expired. Please sign in again.",
            HttpStatusCode.Forbidden=>"This action requires an administrator account.",
            HttpStatusCode.BadRequest=>"The server refused this change. You cannot deactivate yourself or remove your own admin role.",
            HttpStatusCode.NotFound=>"User or administration service unavailable. Refresh the list.",
            _=>$"Update failed (HTTP {(int)response.StatusCode}). Refresh before retrying."});
    }
    private void Report(Exception ex) {if(!_disposed) Message=ex switch {HttpRequestException=>"Cannot reach the server. Refresh before retrying.",OperationCanceledException=>"Request timed out. Refresh to check the account state.",JsonException=>"Unexpected server response. Refresh to check the account state.",_=>ex.Message};}
    public void Dispose(){_disposed=true;_lifetime.Cancel();}
}
