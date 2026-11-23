using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using System.Reflection;

namespace ActFloorCustomizer;

// Co-op desync note (why this file exists):
// every peer builds its own RunState from the shared seed inside StartNewMultiplayerRun, and room
// pools plus the map are generated right after that. If two players use different extra-floor
// counts, their acts get different room counts, different encounter pools and different maps.
// There is no host-to-client run state transfer to lean on, so the host's values are broadcast
// while the lobby is still open, before any peer runs RunState.CreateForNewRun.
public struct HostFloorsMessage : INetMessage
{
    public int Act1;
    public int Act2;
    public int Act3;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Math.Clamp(Act1, 0, 30));
        writer.WriteByte((byte)Math.Clamp(Act2, 0, 30));
        writer.WriteByte((byte)Math.Clamp(Act3, 0, 30));
    }

    public void Deserialize(PacketReader reader)
    {
        Act1 = reader.ReadByte();
        Act2 = reader.ReadByte();
        Act3 = reader.ReadByte();
    }
}

public static class HostSync
{
    private static readonly object Gate = new();
    private static bool _hasHostValues;
    private static int _act1;
    private static int _act2;
    private static int _act3;
    private static INetGameService? _listening;
    private static MessageHandlerDelegate<HostFloorsMessage>? _handler;

    /// <summary>Host values received for the run that is about to start, if any.</summary>
    public static bool TryGet(out int act1, out int act2, out int act3)
    {
        lock (Gate)
        {
            act1 = _act1;
            act2 = _act2;
            act3 = _act3;
            return _hasHostValues;
        }
    }

    public static void Forget()
    {
        lock (Gate)
        {
            _hasHostValues = false;
            _act1 = 0;
            _act2 = 0;
            _act3 = 0;
        }
    }

    // A new lobby always starts from a clean slate, so a value cached by an earlier session can
    // never leak into the next run.
    public static void OnLobbyCreated(INetGameService? service)
    {
        Forget();
        Unlisten();
        if (service == null || service.Type != NetGameType.Client) return;
        _handler = OnHostFloors;
        service.RegisterMessageHandler(_handler);
        _listening = service;
        MapLayout.Diag("lobby: listening for host floor values");
    }

    public static void Unlisten()
    {
        if (_listening == null || _handler == null) return;
        try { _listening.UnregisterMessageHandler(_handler); }
        catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Unregister handler: " + ex.Message); }
        _listening = null;
        _handler = null;
    }

    // Runs on the host only, from a prefix on BeginRunForAllPlayers: the reliable channel keeps this
    // ahead of LobbyBeginRunMessage, so clients hold the values before they build their RunState.
    public static void Broadcast(INetGameService? service)
    {
        if (service == null || service.Type != NetGameType.Host) return;
        var message = new HostFloorsMessage
        {
            Act1 = ModEntry.Settings.ForAct(0),
            Act2 = ModEntry.Settings.ForAct(1),
            Act3 = ModEntry.Settings.ForAct(2)
        };
        try
        {
            service.SendMessage(message);
            MapLayout.Diag($"lobby: sent host floors {message.Act1}/{message.Act2}/{message.Act3}");
        }
        catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Send host floors: " + ex.Message); }
    }

    private static void OnHostFloors(HostFloorsMessage message, ulong senderId)
    {
        Apply(message.Act1, message.Act2, message.Act3);
        MapLayout.Diag($"lobby: host floors {message.Act1}/{message.Act2}/{message.Act3} from {senderId}");
    }

    /// <summary>Stores the host's values; the next co-op run binds them to its acts.</summary>
    public static void Apply(int act1, int act2, int act3)
    {
        lock (Gate)
        {
            _act1 = Math.Clamp(act1, 0, 30);
            _act2 = Math.Clamp(act2, 0, 30);
            _act3 = Math.Clamp(act3, 0, 30);
            _hasHostValues = true;
        }
    }
}

[HarmonyPatch]
internal static class LobbyHostSyncPatch
{
    // Patching every constructor keeps this working if the game adds overloads.
    static IEnumerable<MethodBase> TargetMethods() =>
        typeof(StartRunLobby).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    static void Postfix(StartRunLobby __instance)
    {
        try { HostSync.OnLobbyCreated(__instance.NetService); }
        catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Lobby hook: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
internal static class LobbyBeginRunHostSyncPatch
{
    static void Prefix(StartRunLobby __instance)
    {
        try { HostSync.Broadcast(__instance.NetService); }
        catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Host broadcast: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(StartRunLobby), "CleanUp")]
internal static class LobbyCleanUpHostSyncPatch
{
    static void Prefix() => HostSync.Unlisten();
}
