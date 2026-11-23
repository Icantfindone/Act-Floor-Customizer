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
public readonly record struct HostValues(int Act1, int Act2, int Act3, int Unknown, int Shop, int Rest, int Elite);

public struct HostFloorsMessage : INetMessage
{
    public int Act1;
    public int Act2;
    public int Act3;
    public int Unknown;
    public int Shop;
    public int Rest;
    public int Elite;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Math.Clamp(Act1, 0, FloorSettings.MaxFloors));
        writer.WriteByte((byte)Math.Clamp(Act2, 0, FloorSettings.MaxFloors));
        writer.WriteByte((byte)Math.Clamp(Act3, 0, FloorSettings.MaxFloors));
        writer.WriteByte((byte)Math.Clamp(Unknown, 0, RoomMix.MaxPercent));
        writer.WriteByte((byte)Math.Clamp(Shop, 0, RoomMix.MaxPercent));
        writer.WriteByte((byte)Math.Clamp(Rest, 0, RoomMix.MaxPercent));
        writer.WriteByte((byte)Math.Clamp(Elite, 0, RoomMix.MaxPercent));
    }

    public void Deserialize(PacketReader reader)
    {
        Act1 = reader.ReadByte();
        Act2 = reader.ReadByte();
        Act3 = reader.ReadByte();
        Unknown = reader.ReadByte();
        Shop = reader.ReadByte();
        Rest = reader.ReadByte();
        Elite = reader.ReadByte();
    }
}

public static class HostSync
{
    private static readonly object Gate = new();
    private static bool _hasHostValues;
    private static HostValues _values;
    private static INetGameService? _listening;
    private static MessageHandlerDelegate<HostFloorsMessage>? _handler;

    /// <summary>Host values received for the run that is about to start, if any.</summary>
    public static bool TryGet(out HostValues values)
    {
        lock (Gate)
        {
            values = _values;
            return _hasHostValues;
        }
    }

    public static void Forget()
    {
        lock (Gate)
        {
            _hasHostValues = false;
            _values = default;
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
            Act3 = ModEntry.Settings.ForAct(2),
            Unknown = ModEntry.Settings.UnknownPercent,
            Shop = ModEntry.Settings.ShopPercent,
            Rest = ModEntry.Settings.RestPercent,
            Elite = ModEntry.Settings.ElitePercent
        };
        try
        {
            service.SendMessage(message);
            MapLayout.Diag($"lobby: sent host floors {message.Act1}/{message.Act2}/{message.Act3} mix={message.Unknown}/{message.Shop}/{message.Rest}/{message.Elite}");
        }
        catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Send host floors: " + ex.Message); }
    }

    private static void OnHostFloors(HostFloorsMessage message, ulong senderId)
    {
        Apply(new HostValues(message.Act1, message.Act2, message.Act3, message.Unknown, message.Shop, message.Rest, message.Elite));
        MapLayout.Diag($"lobby: host floors {message.Act1}/{message.Act2}/{message.Act3} mix={message.Unknown}/{message.Shop}/{message.Rest}/{message.Elite} from {senderId}");
    }

    /// <summary>Stores the host's values; the next co-op run binds them to its acts.</summary>
    public static void Apply(HostValues values)
    {
        lock (Gate)
        {
            _values = new HostValues(
                Math.Clamp(values.Act1, 0, FloorSettings.MaxFloors),
                Math.Clamp(values.Act2, 0, FloorSettings.MaxFloors),
                Math.Clamp(values.Act3, 0, FloorSettings.MaxFloors),
                Math.Clamp(values.Unknown, 0, RoomMix.MaxPercent),
                Math.Clamp(values.Shop, 0, RoomMix.MaxPercent),
                Math.Clamp(values.Rest, 0, RoomMix.MaxPercent),
                Math.Clamp(values.Elite, 0, RoomMix.MaxPercent));
            _hasHostValues = true;
        }
    }

    /// <summary>Floor-only overload; keeps the vanilla mix.</summary>
    public static void Apply(int act1, int act2, int act3) => Apply(new HostValues(act1, act2, act3, 0, 0, 0, 0));
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
