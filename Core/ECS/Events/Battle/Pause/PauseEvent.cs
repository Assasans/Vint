using Vint.Core.Battle.Player;
using Vint.Core.ECS.Components.Battle.Pause;
using Vint.Core.ECS.Entities;
using Vint.Core.Server.Game;
using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Events.Battle.Pause;

[ProtocolId(-1316093147997460626)]
public class PauseEvent : IServerEvent {
    public async Task Execute(IPlayerConnection connection, IEntity[] entities) {
        Tanker? tanker = connection.LobbyPlayer?.Tanker;

        if (tanker is not { IsPaused: false })
            return;

        IEntity user = entities.Single();
        tanker.IsPaused = true;

        await user.AddComponent<PauseComponent>();
        await user.AddComponent(new IdleCounterComponent(0));
        await connection.Send(new IdleBeginTimeSyncEvent(DateTimeOffset.UtcNow), user);
    }
}
