using Content.Shared.Ashfall;
using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.GameTicking;

public sealed partial class ServerGameTicker
{
    [ViewVariables]
    public ProtoId<LobbyBackgroundPrototype>? LobbyBackground { get; private set; }

    [ViewVariables]
    private List<ProtoId<LobbyBackgroundPrototype>>? _lobbyBackgrounds;

    private static readonly string[] WhitelistedBackgroundExtensions = new string[] {"png", "jpg", "jpeg", "webp"};

    private void InitializeLobbyBackground()
    {
        var allprotos = ProtoMan.EnumeratePrototypes<LobbyBackgroundPrototype>().ToList();
        _lobbyBackgrounds ??= new List<ProtoId<LobbyBackgroundPrototype>>();

        var configuredBackground = Cfg.GetCVar(AshfallCCVars.LobbyBackground);
        if (!string.IsNullOrWhiteSpace(configuredBackground) &&
            ProtoMan.TryIndex<LobbyBackgroundPrototype>(configuredBackground, out var configuredProto) &&
            WhitelistedBackgroundExtensions.Contains(configuredProto.Background.Extension))
        {
            _lobbyBackgrounds.Add(configuredProto.ID);
            RandomizeLobbyBackground();
            return;
        }

        //create protoids from them
        foreach (var proto in allprotos)
        {
            var ext = proto.Background.Extension;
            if (!WhitelistedBackgroundExtensions.Contains(ext))
                continue;

            //create a protoid and add it to the list
            _lobbyBackgrounds.Add(new ProtoId<LobbyBackgroundPrototype>(proto.ID));
        }

        RandomizeLobbyBackground();
    }

    private void RandomizeLobbyBackground()
    {
        if (_lobbyBackgrounds != null && _lobbyBackgrounds.Count != 0)
            LobbyBackground = Random.Pick(_lobbyBackgrounds);
        else
            LobbyBackground = null;
    }
}
