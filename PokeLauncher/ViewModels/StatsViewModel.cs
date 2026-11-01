using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>One bar in the most-played list; width is relative to the top game.</summary>
public sealed class PlaytimeBarRow
{
    public required string Title { get; init; }

    public required string PlaytimeText { get; init; }

    public required long Sessions { get; init; }

    /// <summary>0..1, relative to the most-played game (bar width).</summary>
    public required double BarPercent { get; init; }
}

/// <summary>One formatted row of the recent-sessions list.</summary>
public sealed record RecentSessionLine(string Title, string Duration, string When);

/// <summary>
/// Playtime dashboard: totals, most-played games, and the recent session history
/// across the whole library. Refreshes on every visit and via the top-bar refresh.
/// </summary>
public sealed partial class StatsViewModel : PageViewModelBase, IRefreshablePage
{
    private readonly Func<int, IReadOnlyList<PlaytimeByGameRow>> _loadByGame;
    private readonly Func<int, IReadOnlyList<PlaySessionRecord>> _loadRecent;
    private readonly Func<(double TotalSeconds, long Sessions, double WeekSeconds, double MonthSeconds)> _loadTotals;

    public StatsViewModel(
        Func<int, IReadOnlyList<PlaytimeByGameRow>> loadByGame,
        Func<int, IReadOnlyList<PlaySessionRecord>> loadRecent,
        Func<(double TotalSeconds, long Sessions, double WeekSeconds, double MonthSeconds)> loadTotals)
    {
        _loadByGame = loadByGame;
        _loadRecent = loadRecent;
        _loadTotals = loadTotals;
        Refresh();
    }

    public override string DisplayName => "Stats";

    public override string IconGlyph => "\uE9D2"; // Diagnostic/chart glyph

    public override string? HeaderSubtitle => "Playtime recorded automatically from your emulator sessions";

    [ObservableProperty]
    public partial string TotalPlaytime { get; private set; } = "0h 0m";

    [ObservableProperty]
    public partial string TotalSessions { get; private set; } = "0";

    [ObservableProperty]
    public partial string WeekPlaytime { get; private set; } = "0h 0m";

    [ObservableProperty]
    public partial string MonthPlaytime { get; private set; } = "0h 0m";

    public bool HasData => TopGames.Count > 0;

    [ObservableProperty]
    public partial ObservableCollection<PlaytimeBarRow> TopGames { get; private set; }

    [ObservableProperty]
    public partial ObservableCollection<RecentSessionLine> RecentSessions { get; private set; }

    public bool HasRecentSessions => RecentSessions.Count > 0;

    public void Refresh() => Reload();

    private void Reload()
    {
        var totals = _loadTotals();
        TotalPlaytime = FormatDuration(totals.TotalSeconds);
        TotalSessions = totals.Sessions.ToString("N0");
        WeekPlaytime = FormatDuration(totals.WeekSeconds);
        MonthPlaytime = FormatDuration(totals.MonthSeconds);

        var byGame = _loadByGame(12);
        var top = byGame.FirstOrDefault();
        var maxSeconds = top is null || top.TotalSeconds <= 0 ? 1.0 : top.TotalSeconds;

        TopGames = new ObservableCollection<PlaytimeBarRow>(byGame.Select(row => new PlaytimeBarRow
        {
            Title = row.Title,
            PlaytimeText = FormatDuration(row.TotalSeconds),
            Sessions = row.Sessions,
            BarPercent = Math.Clamp(row.TotalSeconds / maxSeconds, 0.02, 1.0),
        }));
        OnPropertyChanged(nameof(HasData));

        var titles = byGame.ToDictionary(r => r.GameId, r => r.Title, StringComparer.OrdinalIgnoreCase);
        RecentSessions = new ObservableCollection<RecentSessionLine>(_loadRecent(20).Select(session => new RecentSessionLine(
            titles.GetValueOrDefault(session.GameId, session.GameId),
            FormatDuration(session.DurationSeconds),
            FormatWhen(session.StartedAt, session.EndedAt))));
        OnPropertyChanged(nameof(HasRecentSessions));
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
        {
            return "0h 0m";
        }
        var totalMinutes = (long)Math.Round(seconds / 60.0);
        return $"{totalMinutes / 60}h {totalMinutes % 60}m";
    }

    private static string FormatWhen(DateTime startedAt, DateTime endedAt)
    {
        var local = startedAt.ToLocalTime();
        var today = DateTime.Today;
        var date = local.Date switch
        {
            var d when d == today => "Today",
            var d when d == today.AddDays(-1) => "Yesterday",
            _ => local.ToString("ddd, MMM d"),
        };
        var end = endedAt.ToLocalTime();
        var startText = $"{date}, {local:h:mm tt}";
        return end.Date == local.Date
            ? $"{startText} - {end:h:mm tt}"
            : $"{startText} - {end:h:mm tt}, {end:ddd}";
    }
}
