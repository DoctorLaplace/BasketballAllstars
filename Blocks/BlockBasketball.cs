using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Entities;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Blocks
{
    public class BlockBasketball : Block
    {
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) => "";

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null || byPlayer?.Entity == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            // Right click (or Shift-Right click): pick up the basketball into player inventory
            if (world.Side == EnumAppSide.Server)
            {
                // 0.5s grace period: prevents accidental throw upon releasing the right mouse button used for pickup
                byPlayer.Entity.Attributes.SetLong("basketballPickupNoThrowUntilMs", world.ElapsedMilliseconds + 500);

                Item ballItem = world.GetItem(new AssetLocation("basketballallstars:basketball"));
                if (ballItem != null)
                {
                    ItemStack stack = new ItemStack(ballItem, 1);
                    if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        world.SpawnItemEntity(stack, blockSel.Position.ToVec3d().AddCopy(0.5, 0.2, 0.5));
                    }
                }
                world.BlockAccessor.SetBlock(0, blockSel.Position);
                BasketballAudioParticles.PlayCatchOrPickupSound(world, blockSel.Position.ToVec3d().AddCopy(0.5, 0.2, 0.5));
            }
            return true;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            Item ballItem = world.GetItem(new AssetLocation("basketballallstars:basketball"));
            if (ballItem != null)
            {
                return new ItemStack[] { new ItemStack(ballItem, 1) };
            }
            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}
