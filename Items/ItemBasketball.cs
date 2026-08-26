using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using BasketballAllstars.Entities;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Items
{
    public class ItemBasketball : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            // 0.5s grace period if ball was just picked up off the ground by right-clicking
            long now = byEntity.World.ElapsedMilliseconds;
            long noThrowUntil = byEntity.Attributes.GetLong("basketballPickupNoThrowUntilMs", 0);
            if (noThrowUntil > now && (noThrowUntil - now) <= 1000)
            {
                byEntity.Attributes.SetInt("aiming", 0);
                handHandling = EnumHandHandling.PreventDefault;
                return;
            }
            else if (noThrowUntil > 0)
            {
                // Clear expired or stale timestamp from previous sessions
                byEntity.Attributes.SetLong("basketballPickupNoThrowUntilMs", 0);
            }

            // Shift-Right click on a block: Place basketball as a block in the world!
            if (byEntity.Controls.ShiftKey && blockSel != null)
            {
                byEntity.Attributes.SetInt("aiming", 0);
                IPlayer player = byEntity.World.PlayerByUid((byEntity as EntityPlayer)?.PlayerUID);
                BlockPos placePos = blockSel.Position.AddCopy(blockSel.Face);
                Block targetBlock = byEntity.World.BlockAccessor.GetBlock(placePos);
                Block basketballBlock = byEntity.World.GetBlock(new AssetLocation("basketballallstars:basketball"));

                if (basketballBlock != null && (targetBlock.IsReplacableBy(basketballBlock) || targetBlock.BlockId == 0))
                {
                    if (byEntity.World.Side == EnumAppSide.Server)
                    {
                        byEntity.World.BlockAccessor.SetBlock(basketballBlock.BlockId, placePos);
                        byEntity.World.PlaySoundAt(new AssetLocation("sounds/block/cloth"), placePos.X + 0.5, placePos.Y + 0.5, placePos.Z + 0.5, player);

                        if (!(byEntity is EntityPlayer) || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                        {
                            slot.TakeOut(1);
                            slot.MarkDirty();
                        }
                    }
                    handHandling = EnumHandHandling.PreventDefault;
                    return;
                }
            }

            // Normal Right-click: start throw charging
            byEntity.Attributes.SetInt("aiming", 1);
            handHandling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (byEntity.Attributes.GetInt("aiming", 0) == 0) return false;
            return true;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            int wasAiming = byEntity.Attributes.GetInt("aiming", 0);
            byEntity.Attributes.SetInt("aiming", 0);

            if (wasAiming == 0) return;
            if (slot == null || slot.Empty || slot.Itemstack == null || slot.StackSize <= 0) return;

            long now = byEntity.World.ElapsedMilliseconds;
            long noThrowUntil = byEntity.Attributes.GetLong("basketballPickupNoThrowUntilMs", 0);
            if (noThrowUntil > now && (noThrowUntil - now) <= 1000) return;

            if (byEntity.World.Side == EnumAppSide.Server && byEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer sPlayer)
            {
                // Calibrated throw speed: gentle close-range layup/arc (0.18 min) to full court shot (0.80 max)
                float charge = Math.Clamp(secondsUsed, 0.15f, 1.8f);
                float chargeFraction = (charge - 0.15f) / (1.8f - 0.15f);
                float throwSpeed = 0.18f + chargeFraction * 0.62f;

                EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation("basketballallstars:basketball"));
                if (entityType != null)
                {
                    ItemStack stack = slot.TakeOut(1);
                    if (stack == null || stack.StackSize <= 0) return;
                    slot.MarkDirty();

                    Entity entity = byEntity.World.ClassRegistry.CreateEntity(entityType);
                    if (entity is EntityBasketball ball)
                    {
                        ball.FiredBy = byEntity;
                        ball.ProjectileStack = stack;

                        EntityProjectile.SpawnProjectile(ball, byEntity, throwSpeed, 0.75, -0.05, 0.15, 0.35, 20);

                        // Play throw sound
                        BasketballAudioParticles.PlayThrowSound(byEntity.World, byEntity.Pos.XYZ);

                        // If throwing mid-dunk, release flight trajectory and maintain momentum
                        var dunkSystem = DunkTrajectorySystem.Get(byEntity.World.Side);
                        if (dunkSystem != null && dunkSystem.IsPlayerInTrajectory(entityPlayer.PlayerUID, out _))
                        {
                            dunkSystem.ReleaseMidDunkTrajectory(entityPlayer.PlayerUID, entityPlayer);
                        }
                    }
                }
            }
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            byEntity.Attributes.SetInt("aiming", 0);
            return true;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            // Animate held basketball bouncing from waist straight down to ground in first and third person
            if (target != EnumItemRenderTarget.Gui && target != EnumItemRenderTarget.Ground && renderinfo.Transform != null)
            {
#pragma warning disable CS0618
                bool isFp = target == EnumItemRenderTarget.HandFp;
#pragma warning restore CS0618

                EntityPlayer? holdingEntity = isFp ? capi.World.Player?.Entity : ((renderinfo.InSlot?.Inventory as InventoryBasePlayer)?.Player?.Entity as EntityPlayer);
                if (holdingEntity == null && renderinfo.InSlot?.Inventory is IOwnedInventory ownedInv)
                {
                    holdingEntity = ownedInv.Owner as EntityPlayer;
                }
                if (holdingEntity == null)
                {
                    holdingEntity = capi.World.Player?.Entity;
                }

                if (holdingEntity == null) return;

                bool isAiming = holdingEntity.Attributes.GetInt("aiming", 0) == 1;
                bool inAir = !holdingEntity.OnGround;
                bool inTrajectory = DunkTrajectorySystem.ClientInstance != null && DunkTrajectorySystem.ClientInstance.IsPlayerInTrajectory(holdingEntity.PlayerUID, out _);

                // Only dribble when standing on ground and not aiming; hold between hands when in mid-air or dunking
                if (!isAiming && !inAir && !inTrajectory)
                {
                    double now = capi.World.ElapsedMilliseconds;
                    double bouncePeriodMs = 380.0;
                    double progress = (now % bouncePeriodMs) / bouncePeriodMs;
                    float bounceSin = (float)Math.Sin(progress * Math.PI);

                    var tf = renderinfo.Transform.Clone();

                    if (isFp)
                    {
                        // In first-person: smooth arc down towards floor and back to hand (moved down 20%)
                        tf.Translation.Y -= 0.08f + bounceSin * 0.54f;
                        tf.Translation.Z += bounceSin * 0.12f;
                    }
                    else
                    {
                        // In third-person: calculate vertical displacement towards floor (moved down 20%)
                        float disp = 0.12f + bounceSin * 0.78f;

                        AttachmentPointAndPose? apap = holdingEntity.AnimManager?.Animator?.GetAttachmentPointPose("RightHand");
                        if (apap?.AnimModelMatrix != null && apap.AnimModelMatrix.Length >= 16)
                        {
                            float[] mat = apap.AnimModelMatrix;
                            // Project body vertical (0, -disp, 0) into hand bone space via transpose matrix multiplication
                            tf.Translation.X += mat[1] * (-disp);
                            tf.Translation.Y += mat[5] * (-disp);
                            tf.Translation.Z += mat[9] * (-disp);
                        }
                        else
                        {
                            tf.Translation.Y -= disp;
                        }
                    }

                    renderinfo.Transform = tf;
                }
                else if ((inAir || inTrajectory) && !isAiming)
                {
                    // In-air and slam dunking: shift in blue axis (+Z)
                    var tf = renderinfo.Transform.Clone();
                    tf.Translation.Z += 0.21f;
                    renderinfo.Transform = tf;
                }
            }
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);

            // Apply ball carrier buffs
            if (byEntity is EntityPlayer entityPlayer)
            {
                ApplyCarrierBuffs(entityPlayer);
            }
        }

        public static void ApplyCarrierBuffs(EntityPlayer entityPlayer, bool isChargingJump = false)
        {
            // Speed boost while dribbling (+35%), or movement speed penalty (0.75x) while winding up / charging jump
            float speedMod = isChargingJump ? -0.25f : 0.35f;
            entityPlayer.Stats.Set("walkspeed", "basketball_carrier", speedMod, false);

            // Jump height boost (only while actively holding in hand or near ball)
            entityPlayer.Stats.Set("jumpHeightMul", "basketball_carrier", 0.60f, false);

            // Fall damage immunity: reduce fallDamageFactor by 1.0 (base 1.0 - 1.0 = 0.0)
            entityPlayer.Stats.Set("fallDamageFactor", "basketball_carrier", -1.0f, false);
            entityPlayer.WatchedAttributes.SetBool("basketballFallImmunity", true);

            entityPlayer.walkSpeed = entityPlayer.Stats.GetBlended("walkspeed");
        }

        public static void RemoveCarrierBuffs(EntityPlayer entityPlayer)
        {
            entityPlayer.Stats.Remove("walkspeed", "basketball_carrier");
            entityPlayer.Stats.Remove("jumpHeightMul", "basketball_carrier");
            entityPlayer.Stats.Remove("fallDamageFactor", "basketball_carrier");
            entityPlayer.WatchedAttributes.SetBool("basketballFallImmunity", false);
            entityPlayer.walkSpeed = entityPlayer.Stats.GetBlended("walkspeed");
        }
    }
}
