using Vint.Core.ECS.Components.Battle.Weapon;
using Vint.Core.ECS.Components.Server.Common;

namespace Vint.Core.ECS.Components.Server.Weapon;

public class BulletRadiusPropertyComponent : RangedComponent, IConvertible<WeaponBulletShotComponent> {
    public void Convert(WeaponBulletShotComponent component) =>
        component.BulletRadius = FinalValue;
}
