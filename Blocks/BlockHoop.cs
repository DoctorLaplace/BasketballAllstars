using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace BasketballAllstars.Blocks
{
    public class BlockHoop : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer?.Entity?.Controls?.Sneak == true && blockSel?.Position != null)
            {
                if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityHoop beHoop)
                {
                    bool newState = beHoop.ToggleDunkable(byPlayer);
                    if (world.Side == EnumAppSide.Client && byPlayer is IClientPlayer clientPlayer)
                    {
                        string status = newState ? "Dunking ALLOWED" : "Dunking DISABLED";
                        clientPlayer.ShowChatNotification($"Basketball Hoop: {status}");
                    }
                    world.PlaySoundAt(new AssetLocation("sounds/toggleswitch"), blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, byPlayer, true, 16f, 0.9f);
                    return true;
                }
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityHoop beHoop)
            {
                return beHoop.IsDunkable ? "Dunking: Allowed (Sneak + Right Click to disable)" : "Dunking: Disabled (Sneak + Right Click to allow)";
            }
            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public Vec3d GetRimCenter(BlockPos pos)
        {
            // Center of the hoop ring at the top of the block
            return new Vec3d(pos.X + 0.5, pos.Y + 0.95, pos.Z + 0.5);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // 4 thin border bars of the top ring with wide open center
            return new Cuboidf[]
            {
                new Cuboidf(0.0f,    0.9375f, 0.0f,    1.0f, 1.0f, 0.0625f), // North bar
                new Cuboidf(0.0f,    0.9375f, 0.9375f, 1.0f, 1.0f, 1.0f),    // South bar
                new Cuboidf(0.0f,    0.9375f, 0.0625f, 0.0625f, 1.0f, 0.9375f), // West bar
                new Cuboidf(0.9375f, 0.9375f, 0.0625f, 1.0f, 1.0f, 0.9375f)    // East bar
            };
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return new Cuboidf[] { new Cuboidf(0.0f, 0.9375f, 0.0f, 1.0f, 1.0f, 1.0f) };
        }
    }
}
