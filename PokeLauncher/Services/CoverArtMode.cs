namespace PokeLauncher.Services;

/// <summary>
/// Whether custom cover images (Covers\<game-id>.png/.jpg/…) are shown instead of the
/// generated placeholder art. A global, app-session-wide preference persisted in the
/// settings table; MainViewModel raises per-game change notifications when it flips so
/// every bound card refreshes without leaking static event subscriptions.
/// </summary>
public static class CoverArtMode
{
    public static bool ShowCustomCovers { get; private set; } = true;

    public static void Set(bool showCustomCovers) => ShowCustomCovers = showCustomCovers;
}
