using Vint.Core.Battle.Modules.Interfaces;
using Vint.Core.Battle.Tank;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events.Battle.Weapon.Hit;
using Vint.Core.Server.Game;
using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Components.Battle.Weapon.Stream;

[ProtocolId(-6274985110858845212), ClientAddable, ClientRemovable]
public class StreamHitComponent : IComponent {
    public HitTarget? TankHit { get; private set; }
    public StaticHit? StaticHit { get; private set; }

    public async Task Added(IPlayerConnection connection, IEntity entity) {
        BattleTank? tank = connection.LobbyPlayer?.Tanker?.Tank;

        if (tank == null)
            return;

        foreach (IShotModule shotModule in tank.Modules.OfType<IShotModule>())
            await shotModule.OnShot();
    }
}
