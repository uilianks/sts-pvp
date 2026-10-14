using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;

namespace StsPvp;

/// <summary>
/// Duels over Steam: the game's own Steam co-op transport (Steam lobby + Steam relay, no IP or
/// open port), with the lobby made public and tagged so other duelists can find it. A lobby is
/// found by kind and key: a quick match lobby (any free one), a room code typed by a friend, or
/// the host's Steam id for a rematch. Only lobbies of the same game and mod build match.
/// </summary>
public static class SteamMatch
{
    public enum Kind { Quick, Room, Rematch }

    private const string TagKey = "stspvp";
    private const string KindKey = "stspvp_kind";
    private const string MatchKey = "stspvp_key";
    private const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 5;

    /// <summary>Game and mod builds: a different build would fail the co-op checks anyway.</summary>
    private static readonly string Tag =
        typeof(SteamHost).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12] + "-" +
        typeof(SteamMatch).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];

    /// <summary>The lobby this game hosts, kept public while open (see <see cref="KeepLobbyPublicPatch"/>).</summary>
    public static ulong HostedLobby { get; private set; }

    public static Kind HostedKind { get; private set; }

    public static ulong MySteamId => SteamUser.GetSteamID().m_SteamID;

    /// <summary>False when the game runs without Steam (Steam closed, or --force-steam=off).</summary>
    public static bool Available => SteamInitializer.Initialized;

    public static string NewRoomCode()
    {
        return new string(Enumerable.Range(0, CodeLength).Select(_ => CodeLetters[Random.Shared.Next(CodeLetters.Length)]).ToArray());
    }

    /// <summary>A typed room code, or null if the text is not one.</summary>
    public static string? ParseRoomCode(string text)
    {
        var code = text.Trim().ToUpperInvariant();
        return code.Length == CodeLength && code.All(CodeLetters.Contains) ? code : null;
    }

    /// <summary>Starts a Steam host whose lobby is public and tagged with this kind and key.</summary>
    public static async Task<string?> Host(NetHostGameService net, Kind kind, string key, int maxPlayers)
    {
        NetErrorInfo? error;
        try
        {
            error = await net.StartSteamHost(maxPlayers);
        }
        catch (Exception e)
        {
            Log.Error("[pvp] steam host failed: " + e);
            return "Steam is not available (" + e.Message + ").";
        }
        if (error.HasValue)
        {
            return error.Value.GetReason().ToString();
        }
        var lobby = ((SteamHost)net.NetHost!).LobbyId!.Value;
        HostedLobby = lobby.m_SteamID;
        HostedKind = kind;
        SteamMatchmaking.SetLobbyData(lobby, TagKey, Tag);
        SteamMatchmaking.SetLobbyData(lobby, KindKey, kind.ToString());
        SteamMatchmaking.SetLobbyData(lobby, MatchKey, key);
        SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypePublic);
        Log.Info($"[pvp] hosting steam lobby {HostedLobby} ({kind} {key})");
        return null;
    }

    /// <summary>Open lobbies of this kind and key, oldest (lowest id) first, never our own.</summary>
    public static async Task<List<ulong>> Find(Kind kind, string key)
    {
        SteamMatchmaking.AddRequestLobbyListStringFilter(TagKey, Tag, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(KindKey, kind.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(MatchKey, key, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);
        using var call = new SteamCallResult<LobbyMatchList_t>(SteamMatchmaking.RequestLobbyList(), SteamInitializer.DisconnectToken);
        var result = await call.Task;
        var lobbies = new List<ulong>();
        for (var i = 0; i < result.m_nLobbiesMatching; i++)
        {
            var lobby = SteamMatchmaking.GetLobbyByIndex(i);
            if (lobby.m_SteamID != HostedLobby && SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID != MySteamId)
            {
                lobbies.Add(lobby.m_SteamID);
            }
        }
        lobbies.Sort();
        return lobbies;
    }

    /// <summary>The hosted lobby closed (game left the main menu, or the host disconnected).</summary>
    public static void ForgetHostedLobby()
    {
        HostedLobby = 0;
    }
}

/// <summary>
/// The game reopens a lobby as friends-only (when a player leaves the character select, say):
/// a duel lobby stays public while open, so it can still be found.
/// </summary>
[HarmonyPatch(typeof(SteamHost), nameof(SteamHost.SetHostIsClosed))]
internal static class KeepLobbyPublicPatch
{
    private static void Postfix(SteamHost __instance, bool isClosed)
    {
        if (!isClosed && __instance.LobbyId is { } lobby && lobby.m_SteamID == SteamMatch.HostedLobby)
        {
            SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypePublic);
        }
    }
}
