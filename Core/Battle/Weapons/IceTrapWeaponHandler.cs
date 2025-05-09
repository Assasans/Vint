using Vint.Core.Battle.Damage.Calculator;
using Vint.Core.Battle.Rounds;
using Vint.Core.Battle.Tank;
using Vint.Core.Battle.Tank.Temperature;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events.Battle.Weapon.Hit;

namespace Vint.Core.Battle.Weapons;

public class IceTrapWeaponHandler(
    BattleTank tank,
    IDamageCalculator damageCalculator,
    TimeSpan cooldown,
    IEntity marketEntity,
    IEntity battleEntity,
    bool damageWeakeningByDistance,
    float maxDamageDistance,
    float minDamageDistance,
    float minDamagePercent,
    float maxDamage,
    float minDamage,
    float temperatureDelta,
    float temperatureLimit,
    TimeSpan temperatureDuration,
    Func<Task> explode
) : ModuleWeaponHandler(tank,
    damageCalculator,
    cooldown,
    marketEntity,
    battleEntity,
    damageWeakeningByDistance,
    maxDamageDistance,
    minDamageDistance,
    minDamagePercent,
    maxDamage,
    minDamage,
    int.MaxValue), IDiscreteWeaponHandler, IMineWeaponHandler, ITemperatureWeaponHandler {
    public override Task Fire(HitTarget target, int targetIndex) => throw new NotSupportedException();

    public float MinSplashDamagePercent { get; } = minDamagePercent;
    public float RadiusOfMaxSplashDamage { get; } = maxDamageDistance;
    public float RadiusOfMinSplashDamage { get; } = minDamageDistance;

    public async Task Explode() => await explode();

    public async Task SplashFire(HitTarget target, int targetIndex) {
        Round round = BattleTank.Round;
        BattleTank targetTank = round.Tankers
            .Select(tanker => tanker.Tank)
            .Single(tank => tank.Entities.Incarnation == target.IncarnationEntity);

        bool isEnemy = targetTank == BattleTank || BattleTank.IsEnemy(targetTank);

        TemperatureAssist assist = TemperatureCalculator.Calculate(BattleTank, this, !isEnemy);
        targetTank.TemperatureProcessor.EnqueueAssist(assist);

        if (targetTank.StateManager.CurrentState is not Active || !isEnemy)
            return;

        CalculatedDamage damage = await DamageCalculator.Calculate(BattleTank, targetTank, this, target, targetIndex, true, true);
        await round.DamageProcessor.Damage(BattleTank, targetTank, MarketEntity, BattleEntity, damage);
    }

    public float GetSplashMultiplier(float distance) {
        if (distance <= RadiusOfMaxSplashDamage) return 1;
        if (distance >= RadiusOfMinSplashDamage) return 0;

        return 0.01f *
               (MinSplashDamagePercent +
                (RadiusOfMinSplashDamage - distance) * (100f - MinSplashDamagePercent) / (RadiusOfMinSplashDamage - RadiusOfMaxSplashDamage));
    }

    public float TemperatureLimit { get; } = temperatureLimit;
    public float TemperatureDelta { get; } = temperatureDelta;
    public TimeSpan TemperatureDuration { get; } = temperatureDuration;
}
