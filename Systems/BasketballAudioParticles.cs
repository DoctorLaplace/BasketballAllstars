using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace BasketballAllstars.Systems
{
    public static class BasketballAudioParticles
    {
        private static readonly Random rand = new Random();

        public static void PlayDribbleSound(IWorldAccessor world, Vec3d pos)
        {
            float pitch = 0.84f + (float)rand.NextDouble() * 0.06f; // Deeper pitch
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/dribble"),
                pos.X, pos.Y, pos.Z,
                null,
                pitch,
                28f,
                0.88f
            );
        }

        public static void PlayBounceSound(IWorldAccessor world, Vec3d pos, float impactSpeed)
        {
            // Crisp raw bounce sound for physical impacts with the court/world
            float pitch = 0.82f + (float)rand.NextDouble() * 0.08f;
            float volume = Math.Clamp(impactSpeed * 1.5f, 0.40f, 1.15f);

            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/dribble_original"),
                pos.X, pos.Y, pos.Z,
                null,
                pitch,
                36f,
                volume
            );
        }

        public static void PlayThrowSound(IWorldAccessor world, Vec3d pos)
        {
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/fingersonball"),
                pos.X, pos.Y, pos.Z,
                null,
                false,
                21f,
                1.05f
            );
        }

        public static void PlayCatchOrPickupSound(IWorldAccessor world, Vec3d pos)
        {
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/fingersonball"),
                pos.X, pos.Y, pos.Z,
                null,
                false,
                21f,
                1.0f
            );
        }

        public static void PlayStealSound(IWorldAccessor world, Vec3d pos)
        {
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/grabball"),
                pos.X, pos.Y, pos.Z,
                null,
                false,
                33f,
                1.1f
            );
        }

        public static void PlayBackboardRattle(IWorldAccessor world, Vec3d rimPos)
        {
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/backboardrattle"),
                rimPos.X, rimPos.Y, rimPos.Z,
                null,
                false,
                54f,
                1.2f
            );
        }

        public static void PlayHoopScoreSounds(IWorldAccessor world, Vec3d rimPos, bool isDunk)
        {
            // Swish & ball hit basket sound (emanating directly from hoop ring, range 30m)
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/ballhitbasket"),
                rimPos.X, rimPos.Y, rimPos.Z,
                null,
                false,
                30f,
                0.95f
            );

            // Airhorns celebratory fanfare (emanating from hoop, range 39m)
            world.PlaySoundAt(
                new AssetLocation("basketballallstars:sounds/airhorns"),
                rimPos.X, rimPos.Y, rimPos.Z,
                null,
                false,
                39f,
                0.80f
            );

            // Stadium crowd cheering: plays globally for all players within 50 blocks of the scored hoop
            PlayCrowdCheer(world, rimPos);

            // Slam dunk rattling the backboard
            if (isDunk)
            {
                PlayBackboardRattle(world, rimPos);
            }
        }

        public static void PlayCrowdCheer(IWorldAccessor world, Vec3d rimPos)
        {
            if (world is IServerWorldAccessor serverWorld)
            {
                foreach (IServerPlayer player in serverWorld.AllOnlinePlayers)
                {
                    if (player.Entity == null) continue;
                    double dist = player.Entity.Pos.XYZ.DistanceTo(rimPos);
                    if (dist <= 50.0)
                    {
                        // Plays centered on player as global ambient stadium celebration
                        serverWorld.PlaySoundAt(
                            new AssetLocation("basketballallstars:sounds/crowdcheer"),
                            player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z,
                            null,
                            false,
                            32f,
                            0.34f
                        );
                    }
                }
            }
            else if (world is IClientWorldAccessor clientWorld)
            {
                IPlayer localPlayer = clientWorld.Player;
                if (localPlayer?.Entity != null && localPlayer.Entity.Pos.XYZ.DistanceTo(rimPos) <= 50.0)
                {
                    clientWorld.PlaySoundAt(
                        new AssetLocation("basketballallstars:sounds/crowdcheer"),
                        localPlayer.Entity.Pos.X, localPlayer.Entity.Pos.Y, localPlayer.Entity.Pos.Z,
                        null,
                        false,
                        32f,
                        0.34f
                    );
                }
            }
        }

        public static void PlayClashSound(IWorldAccessor world, Vec3d clashPos)
        {
            world.PlaySoundAt(
                new AssetLocation("game:sounds/effect/anvilhit"),
                clashPos.X, clashPos.Y, clashPos.Z,
                null,
                false,
                48f,
                1.1f
            );
        }

        public static void SpawnBounceParticles(IWorldAccessor world, Vec3d pos)
        {
            SimpleParticleProperties particles = new SimpleParticleProperties(
                minQuantity: 3,
                maxQuantity: 6,
                color: ColorUtil.ToRgba(160, 200, 180, 150),
                minPos: new Vec3d(pos.X - 0.15, pos.Y, pos.Z - 0.15),
                maxPos: new Vec3d(pos.X + 0.15, pos.Y + 0.05, pos.Z + 0.15),
                minVelocity: new Vec3f(-0.3f, 0.2f, -0.3f),
                maxVelocity: new Vec3f(0.3f, 0.8f, 0.3f),
                lifeLength: 0.4f,
                gravityEffect: 0.3f,
                minSize: 0.15f,
                maxSize: 0.35f,
                model: EnumParticleModel.Cube
            );

            world.SpawnParticles(particles);
        }

        public static void SpawnDribbleParticles(IWorldAccessor world, Vec3d pos)
        {
            SimpleParticleProperties particles = new SimpleParticleProperties(
                minQuantity: 2,
                maxQuantity: 4,
                color: ColorUtil.ToRgba(140, 220, 140, 60),
                minPos: new Vec3d(pos.X - 0.1, pos.Y, pos.Z - 0.1),
                maxPos: new Vec3d(pos.X + 0.1, pos.Y + 0.05, pos.Z + 0.1),
                minVelocity: new Vec3f(-0.2f, 0.1f, -0.2f),
                maxVelocity: new Vec3f(0.2f, 0.5f, 0.2f),
                lifeLength: 0.3f,
                gravityEffect: 0.2f,
                minSize: 0.1f,
                maxSize: 0.25f,
                model: EnumParticleModel.Cube
            );

            world.SpawnParticles(particles);
        }

        public static void SpawnHoopCelebrationParticles(IWorldAccessor world, Vec3d rimPos)
        {
            // Multi-colored celebratory confetti
            int[] confettiColors = new int[]
            {
                ColorUtil.ToRgba(255, 255, 50, 50),   // Red
                ColorUtil.ToRgba(255, 50, 150, 255),  // Blue
                ColorUtil.ToRgba(255, 255, 220, 0),   // Gold
                ColorUtil.ToRgba(255, 50, 255, 100),  // Green
                ColorUtil.ToRgba(255, 255, 100, 255), // Purple
                ColorUtil.ToRgba(255, 255, 255, 255)  // White
            };

            for (int i = 0; i < confettiColors.Length; i++)
            {
                SimpleParticleProperties confetti = new SimpleParticleProperties(
                    minQuantity: 12,
                    maxQuantity: 20,
                    color: confettiColors[i],
                    minPos: new Vec3d(rimPos.X - 0.3, rimPos.Y + 0.1, rimPos.Z - 0.3),
                    maxPos: new Vec3d(rimPos.X + 0.3, rimPos.Y + 0.6, rimPos.Z + 0.3),
                    minVelocity: new Vec3f(-1.5f, 1.0f, -1.5f),
                    maxVelocity: new Vec3f(1.5f, 3.5f, 1.5f),
                    lifeLength: 2.0f,
                    gravityEffect: 0.45f,
                    minSize: 0.2f,
                    maxSize: 0.45f,
                    model: EnumParticleModel.Cube
                );

                world.SpawnParticles(confetti);
            }

            // Glowing sparkles
            SimpleParticleProperties sparkles = new SimpleParticleProperties(
                minQuantity: 25,
                maxQuantity: 40,
                color: ColorUtil.ToRgba(255, 255, 255, 180),
                minPos: new Vec3d(rimPos.X - 0.4, rimPos.Y - 0.2, rimPos.Z - 0.4),
                maxPos: new Vec3d(rimPos.X + 0.4, rimPos.Y + 0.8, rimPos.Z + 0.4),
                minVelocity: new Vec3f(-0.8f, -0.5f, -0.8f),
                maxVelocity: new Vec3f(0.8f, 2.0f, 0.8f),
                lifeLength: 1.5f,
                gravityEffect: 0.1f,
                minSize: 0.1f,
                maxSize: 0.3f,
                model: EnumParticleModel.Quad
            );
            sparkles.VertexFlags = 128; // Glow in the dark

            world.SpawnParticles(sparkles);
        }

        public static void SpawnClashSparks(IWorldAccessor world, Vec3d clashPos)
        {
            SimpleParticleProperties sparks = new SimpleParticleProperties(
                minQuantity: 40,
                maxQuantity: 65,
                color: ColorUtil.ToRgba(255, 255, 240, 100),
                minPos: new Vec3d(clashPos.X - 0.2, clashPos.Y + 0.8, clashPos.Z - 0.2),
                maxPos: new Vec3d(clashPos.X + 0.2, clashPos.Y + 1.2, clashPos.Z + 0.2),
                minVelocity: new Vec3f(-3.5f, -1.0f, -3.5f),
                maxVelocity: new Vec3f(3.5f, 3.5f, 3.5f),
                lifeLength: 0.6f,
                gravityEffect: 0.8f,
                minSize: 0.15f,
                maxSize: 0.4f,
                model: EnumParticleModel.Quad
            );
            sparks.VertexFlags = 255; // Full glow

            world.SpawnParticles(sparks);
        }
    }
}
