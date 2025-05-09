using Vint.Core.Battle.Damage.Calculator;
using Vint.Core.Battle.Effects;
using Vint.Core.Battle.Modules.Interfaces;
using Vint.Core.Battle.Modules.Types.Base;
using Vint.Core.Battle.Tank;
using Vint.Core.Battle.Tank.Temperature;
using Vint.Core.Battle.Weapons;
using Vint.Core.ECS.Components.Server.Modules.Effect.EmergencyProtection;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events.Battle.Module;
using Vint.Core.Utils;

namespace Vint.Core.Battle.Modules.Types;

[ModuleId(-357196071)]
public class EmergencyProtectionModule : TriggerBattleModule, IHealthModule, ITemperatureWeaponHandler {
    public override string ConfigPath => "garage/module/upgrade/properties/emergencyprotection";

    float AdditiveHpFactor { get; set; }
    float FixedHp { get; set; }
    TimeSpan Duration { get; set; }

    CalculatedDamage CalculatedHeal => new(default, Tank.MaxHealth * AdditiveHpFactor + FixedHp, false, false);

    public async Task OnHealthChanged(float before, float current, float max) {
        if (current > 0) return;

        await Activate();
    }

    public IEntity BattleEntity => Entity;
    public float TemperatureLimit => -1f;
    public float TemperatureDelta => -1f;
    public TimeSpan TemperatureDuration => Duration;

    public override EmergencyProtectionEffect GetEffect() => new(Duration, Tank, Level);

    public override async Task Activate() {
        if (!CanBeActivated) return;

        EmergencyProtectionEffect? effect = Tank.Effects
            .OfType<EmergencyProtectionEffect>()
            .SingleOrDefault();

        if (effect != null) return;

        effect = GetEffect();
        await effect.Activate();

        IEntity effectEntity = effect.Entity!;

        await base.Activate();

        await Tank.TemperatureProcessor.ResetAll();
        TemperatureAssist temperatureAssist = TemperatureCalculator.Calculate(Tank, this, false);

        await Round.DamageProcessor.Heal(Tank, CalculatedHeal);
        Tank.TemperatureProcessor.EnqueueAssist(temperatureAssist);

        await Round.Players.Send(new TriggerEffectExecuteEvent(), effectEntity);
    }

    public override async Task Init(BattleTank tank, IEntity userSlot, IEntity marketModule) {
        await base.Init(tank, userSlot, marketModule);

        AdditiveHpFactor = GetStat<ModuleEmergencyProtectionEffectAdditiveHPFactorPropertyComponent>();
        FixedHp = GetStat<ModuleEmergencyProtectionEffectFixedHPPropertyComponent>();
        Duration = TimeSpan.FromMilliseconds(GetStat<ModuleEmergencyProtectionEffectHolyshieldDurationPropertyComponent>());
    }
}
