using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Components.Battle.Limit;

[ProtocolId(-3596341255560623830)]
public class TimeLimitComponent(
    long timeLimitSec,
    long warmUpTimeLimitSec
) : IComponent {
    public long TimeLimitSec { get; private set; } = timeLimitSec;

    public long WarmingUpTimeLimitSec { get; private set; } = warmUpTimeLimitSec;
}
