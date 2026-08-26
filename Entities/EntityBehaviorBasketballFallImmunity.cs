using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace BasketballAllstars.Entities
{
    public class EntityBehaviorBasketballFallImmunity : EntityBehavior
    {
        public EntityBehaviorBasketballFallImmunity(Entity entity) : base(entity) { }

        public override string PropertyName() => "basketballfallimmunity";

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (damageSource.Source == EnumDamageSource.Fall || damageSource.Type == EnumDamageType.Gravity)
            {
                if (entity is EntityPlayer entityPlayer)
                {
                    long immunityUntil = entityPlayer.Attributes.GetLong("basketballFallImmunityUntilMs", 0);
                    long nowMs = entity.World.ElapsedMilliseconds;

                    // Lingering fall immunity after dunk (2 seconds) or during active dunk flight
                    if (nowMs < immunityUntil || entityPlayer.WatchedAttributes.GetBool("basketballFallImmunity", false))
                    {
                        damage = 0f;
                        return;
                    }

                    // Carrying basketball immunity
                    var mainItem = entityPlayer.RightHandItemSlot?.Itemstack?.Collectible;
                    var offItem = entityPlayer.LeftHandItemSlot?.Itemstack?.Collectible;
                    if ((mainItem != null && mainItem.Code.Path.Contains("basketball")) ||
                        (offItem != null && offItem.Code.Path.Contains("basketball")))
                    {
                        damage = 0f;
                        return;
                    }
                }
            }
        }
    }
}
