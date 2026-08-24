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
            // Center of the hoop ring based on orientation
            string? orientation = Variant?["horizontalorientation"] ?? "north";
            double rimOffset = 0.45;
            double rimY = pos.Y + 0.80;

            double cx = pos.X + 0.5;
            double cz = pos.Z + 0.5;

            switch (orientation)
            {
                case "north":
                    cz -= rimOffset;
                    break;
                case "south":
                    cz += rimOffset;
                    break;
                case "east":
                    cx += rimOffset;
                    break;
                case "west":
                    cx -= rimOffset;
                    break;
            }

            return new Vec3d(cx, rimY, cz);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            string? orientation = Variant?["horizontalorientation"] ?? "north";
            switch (orientation)
            {
                case "north":
                    // Rim extends North; backboard is at South edge
                    return new Cuboidf[] { new Cuboidf(0f, 0f, 0.85f, 1f, 1.2f, 1f) };
                case "south":
                    // Rim extends South; backboard is at North edge
                    return new Cuboidf[] { new Cuboidf(0f, 0f, 0f, 1f, 1.2f, 0.15f) };
                case "east":
                    // Rim extends East; backboard is at West edge
                    return new Cuboidf[] { new Cuboidf(0f, 0f, 0f, 0.15f, 1.2f, 1f) };
                case "west":
                    // Rim extends West; backboard is at East edge
                    return new Cuboidf[] { new Cuboidf(0.85f, 0f, 0f, 1f, 1.2f, 1f) };
                default:
                    return new Cuboidf[] { new Cuboidf(0f, 0f, 0.85f, 1f, 1.2f, 1f) };
            }
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return new Cuboidf[] { new Cuboidf(0f, 0f, 0f, 1f, 1.2f, 1f) };
        }
    }
}
