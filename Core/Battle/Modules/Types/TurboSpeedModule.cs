using Vint.Core.Battle.Effects;
using Vint.Core.Battle.Modules.Types.Base;

namespace Vint.Core.Battle.Modules.Types;

[ModuleId(-365494384)]
public class TurboSpeedModule : ActiveBattleModule {
    public override string ConfigPath => "garage/module/upgrade/properties/turbospeed";

    public override async Task Activate() {
        if (!CanBeActivated) return;

        await base.Activate();

        TurboSpeedEffect? effect = Tank
            .Effects
            .OfType<TurboSpeedEffect>()
            .SingleOrDefault();

        switch (effect) {
            case null:
                await GetEffect()
                    .Activate();

                break;

            case IExtendableEffect extendableEffect:
                await extendableEffect.Extend(Level);
                break;
        }
    }

    public override TurboSpeedEffect GetEffect() => new(Tank, Level);
}
