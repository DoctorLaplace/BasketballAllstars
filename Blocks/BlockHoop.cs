using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace BasketballAllstars.Blocks
{
    public class BlockHoop : Block
    {
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) => "";

        public Vec3d GetRimCenter(BlockPos pos)
        {
            // Center of the hoop ring at the top of the block
            return new Vec3d(pos.X + 0.5, pos.Y + 0.90, pos.Z + 0.5);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // 4 thin border bars of the top ring with open center
            return new Cuboidf[]
            {
                new Cuboidf(0.125f, 0.875f, 0.125f, 0.875f, 1.0f, 0.25f),  // North bar
                new Cuboidf(0.125f, 0.875f, 0.75f,  0.875f, 1.0f, 0.875f), // South bar
                new Cuboidf(0.125f, 0.875f, 0.25f,  0.25f,  1.0f, 0.75f),  // West bar
                new Cuboidf(0.75f,  0.875f, 0.25f,  0.875f, 1.0f, 0.75f)   // East bar
            };
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return new Cuboidf[] { new Cuboidf(0.125f, 0.875f, 0.125f, 0.875f, 1.0f, 0.875f) };
        }
    }
}
