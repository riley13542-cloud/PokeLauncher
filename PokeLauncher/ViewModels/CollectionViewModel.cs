using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PokeLauncher.ViewModels;

/// <summary>
/// One collection on the Collections page: name, description, and its shared
/// <see cref="GameViewModel"/> members. Created, renamed, re-membered, and deleted
/// through the edit dialog; the view model updates in place so the page stays live.
/// </summary>
public partial class CollectionViewModel : ViewModelBase
{
    private readonly Action<CollectionViewModel> _select;

    public CollectionViewModel(string id, string name, string description, IReadOnlyList<GameViewModel> games, Action<CollectionViewModel> select)
    {
        Id = id;
        _name = name;
        _description = description;
        _games = games.ToList();
        _select = select;
    }

    private readonly List<GameViewModel> _games;

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    public IReadOnlyList<GameViewModel> Games => _games;

    public string CountLabel => $"{Games.Count} {(Games.Count == 1 ? "game" : "games")}";

    /// <summary>Applies a saved edit (name/description/members) so the page reflects the database.</summary>
    public void ApplyUpdate(string name, string description, IReadOnlyList<GameViewModel> games)
    {
        Name = name;
        Description = description;
        _games.Clear();
        _games.AddRange(games);
        OnPropertyChanged(nameof(Games));
        OnPropertyChanged(nameof(CountLabel));
    }

    /// <summary>Adds a game to this collection (details-page chip toggles).</summary>
    public void AddGame(GameViewModel game)
    {
        if (_games.Any(g => g.Id == game.Id))
        {
            return;
        }
        _games.Add(game);
        OnPropertyChanged(nameof(Games));
        OnPropertyChanged(nameof(CountLabel));
    }

    /// <summary>Drops a game that was removed from the library.</summary>
    public void RemoveGame(GameViewModel game)
    {
        _games.Remove(game);
        OnPropertyChanged(nameof(Games));
        OnPropertyChanged(nameof(CountLabel));
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _select(this);
}
