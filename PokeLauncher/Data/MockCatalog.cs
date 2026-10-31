using System.Collections.Generic;
using PokeLauncher.Models;

namespace PokeLauncher.Data;

/// <summary>
/// Built-in seed catalog for first-run initialization. No ROM files exist or are bundled;
/// user state (favorites, imported ROM paths, play stats) starts empty in the database.
/// All ROM hacks listed are fictional creations for layout purposes.
/// </summary>
public static class MockCatalog
{
    public static IReadOnlyList<PokemonGame> CreateGames() => new PokemonGame[]
    {
        new()
        {
            Id = "red", Title = "Pokémon Red", Generation = 1, Platform = "Game Boy", ReleaseYear = 1996,
            Description = "The adventure that started it all. Travel across Kanto, catch the original 151, and challenge the Pokémon League while Team Rocket schemes in the shadows.",
            CoverFromHex = "#FF5A5F", CoverToHex = "#7F1D2F",
        },
        new()
        {
            Id = "blue", Title = "Pokémon Blue", Generation = 1, Platform = "Game Boy", ReleaseYear = 1996,
            Description = "Kanto with version-exclusive encounters. Trade with Red to complete your Pokédex and hunt down the elusive Pokémon that only appear here.",
            CoverFromHex = "#4F9DFF", CoverToHex = "#1E3A8A",
        },
        new()
        {
            Id = "yellow", Title = "Pokémon Yellow", Generation = 1, Platform = "Game Boy", ReleaseYear = 1998,
            Description = "The special Pikachu edition. Your starter refuses its ball and rides on your shoulder, Gym Leaders use anime-inspired teams, and Team Rocket's duo keeps turning up.",
            CoverFromHex = "#FFD54A", CoverToHex = "#92400E",
        },
        new()
        {
            Id = "gold", Title = "Pokémon Gold", Generation = 2, Platform = "Game Boy Color", ReleaseYear = 1999,
            Description = "Johto introduces breeding, held items, and a real-time clock — then surprises you with the entire Kanto region waiting after the League.",
            CoverFromHex = "#FFC53D", CoverToHex = "#7C4A03",
        },
        new()
        {
            Id = "silver", Title = "Pokémon Silver", Generation = 2, Platform = "Game Boy Color", ReleaseYear = 1999,
            Description = "The counterpart to Gold with its own version exclusives, plus legendary beasts roaming the region and the return of a very familiar rival.",
            CoverFromHex = "#C9D1E4", CoverToHex = "#334155",
        },
        new()
        {
            Id = "crystal", Title = "Pokémon Crystal", Generation = 2, Platform = "Game Boy Color", ReleaseYear = 2000,
            Description = "The definitive Johto experience of its era: animated sprites, Suicune's storyline, the Battle Tower, and the first playable female protagonist.",
            CoverFromHex = "#7EE8FA", CoverToHex = "#155E75",
        },
        new()
        {
            Id = "ruby", Title = "Pokémon Ruby", Generation = 3, Platform = "Game Boy Advance", ReleaseYear = 2002,
            Description = "Hoenn doubles down on weather, abilities, and double battles. Team Magma wants to expand the land — your journey says otherwise.",
            CoverFromHex = "#FF6B6B", CoverToHex = "#7F1D1D",
        },
        new()
        {
            Id = "sapphire", Title = "Pokémon Sapphire", Generation = 3, Platform = "Game Boy Advance", ReleaseYear = 2002,
            Description = "The other side of Hoenn, where Team Aqua chases endless oceans. Version exclusives and diving spots make it a distinct adventure.",
            CoverFromHex = "#38BDF8", CoverToHex = "#1D4ED8",
        },
        new()
        {
            Id = "emerald", Title = "Pokémon Emerald", Generation = 3, Platform = "Game Boy Advance", ReleaseYear = 2004,
            Description = "Both villain teams, the rebuilt Battle Frontier, and a remixed storyline make Emerald the community's favourite way to revisit Hoenn.",
            CoverFromHex = "#34D399", CoverToHex = "#065F46",
        },
        new()
        {
            Id = "firered", Title = "Pokémon FireRed", Generation = 3, Platform = "Game Boy Advance", ReleaseYear = 2004,
            Description = "Kanto rebuilt on the GBA engine with the physical/special split-era presentation, the Sevii Islands, and full compatibility with Hoenn trading.",
            CoverFromHex = "#FF7A59", CoverToHex = "#991B1B",
        },
        new()
        {
            Id = "leafgreen", Title = "Pokémon LeafGreen", Generation = 3, Platform = "Game Boy Advance", ReleaseYear = 2004,
            Description = "FireRed's green twin with mirrored version exclusives — the pair that redefined what a remake could be.",
            CoverFromHex = "#86EFAC", CoverToHex = "#166534",
        },
        new()
        {
            Id = "diamond", Title = "Pokémon Diamond", Generation = 4, Platform = "Nintendo DS", ReleaseYear = 2006,
            Description = "Sinnoh brings the physical/special split, online trading, and a mythology-heavy story beneath Mt. Coronet. Dialga controls time itself.",
            CoverFromHex = "#93C5FD", CoverToHex = "#3730A3",
        },
        new()
        {
            Id = "pearl", Title = "Pokémon Pearl", Generation = 4, Platform = "Nintendo DS", ReleaseYear = 2006,
            Description = "Sinnoh with Palkia and its own version exclusives — the same beloved region through a slightly different lens.",
            CoverFromHex = "#F0ABFC", CoverToHex = "#86198F",
        },
        new()
        {
            Id = "platinum", Title = "Pokémon Platinum", Generation = 4, Platform = "Nintendo DS", ReleaseYear = 2008,
            Description = "The enhanced third version: a reworked story with Giratina's Distortion World, faster pacing, and the Battle Frontier's return.",
            CoverFromHex = "#C7D2FE", CoverToHex = "#4C1D95",
        },
        new()
        {
            Id = "heartgold", Title = "Pokémon HeartGold", Generation = 4, Platform = "Nintendo DS", ReleaseYear = 2009,
            Description = "Widely considered one of the best games in the series: all of Johto and Kanto, a walking first partner, and the Pokéathlon.",
            CoverFromHex = "#FDE68A", CoverToHex = "#92400E",
        },
        new()
        {
            Id = "soulsilver", Title = "Pokémon SoulSilver", Generation = 4, Platform = "Nintendo DS", ReleaseYear = 2009,
            Description = "HeartGold's counterpart with Lugia front and centre and mirrored exclusives — the definitive way to experience the second generation.",
            CoverFromHex = "#BAE6FD", CoverToHex = "#334155",
        },
    };

    public static IReadOnlyList<GameCollection> CreateCollections() => new GameCollection[]
    {
        new()
        {
            Id = "kanto", Name = "Kanto Classics", Description = "Where it all began — the original 151 era plus its GBA remakes.",
            GameIds = new[] { "red", "blue", "yellow", "firered", "leafgreen" },
        },
        new()
        {
            Id = "johto", Name = "Johto Journeys", Description = "Gold, Silver, and their definitive remakes — two regions in every box.",
            GameIds = new[] { "gold", "silver", "crystal", "heartgold", "soulsilver" },
        },
        new()
        {
            Id = "hoenn", Name = "Hoenn Masters", Description = "Ruby, Sapphire, and the community's beloved Emerald.",
            GameIds = new[] { "ruby", "sapphire", "emerald" },
        },
        new()
        {
            Id = "ready", Name = "Ready to Play", Description = "Games with an imported ROM file, ready for emulator launching.",
            GameIds = new string[] { },
        },
    };
}
