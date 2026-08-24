using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using BasketballAllstars.Entities;

namespace BasketballAllstars.Items
{
    public class ItemBasketballDummy : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel == null) return;
            IPlayer player = byEntity.World.PlayerByUid((byEntity as EntityPlayer)?.PlayerUID);

            double x = blockSel.FullPosition.X;
            double y = blockSel.Position.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y);
            double z = blockSel.FullPosition.Z;

            BlockPos blockPos = new BlockPos((int)x, (int)y, (int)z, byEntity.Pos.Dimension);
            if (!byEntity.World.Claims.TryAccess(player, blockPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                slot.MarkDirty();
                return;
            }

            if (byEntity.World.Side == EnumAppSide.Server)
            {
                EntityProperties type = byEntity.World.GetEntityType(new AssetLocation("basketballallstars:basketballdummy"));
                if (type != null)
                {
                    Entity entity = byEntity.World.ClassRegistry.CreateEntity(type);
                    if (entity is EntityBasketballDummy dummy)
                    {
                        dummy.Pos.SetPos(x, y, z);
                        dummy.Pos.Yaw = byEntity.Pos.Yaw + GameMath.PI - (float)GameMath.PIHALF; // Face the placing player
                        dummy.HasBall = false; // Spawns empty-handed ready for drills, passes, or defense

                        byEntity.World.SpawnEntity(dummy);
                        byEntity.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), dummy, player);

                        if (!(byEntity is EntityPlayer) || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                        {
                            slot.TakeOut(1);
                            slot.MarkDirty();
                        }

                        handling = EnumHandHandling.PreventDefaultAction;
                    }
                }
            }
            else
            {
                handling = EnumHandHandling.PreventDefaultAction;
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "Place Practice Dummy",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }
    }
}
