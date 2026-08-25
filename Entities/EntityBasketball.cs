using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Blocks;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Entities
{
    public class EntityBasketball : Entity, IProjectile
    {
        #region IProjectile
        public Entity? FiredBy { get; set; }
        public float Damage { get; set; } = 0;
        public int DamageTier { get; set; } = 0;
        public EnumDamageType DamageType { get; set; } = EnumDamageType.BluntAttack;
        public bool IgnoreInvFrames { get; set; } = true;
        public ItemStack? ProjectileStack { get; set; }
        public ItemStack? WeaponStack { get; set; }
        public float DropOnImpactChance { get; set; } = 0f;
        public bool DamageStackOnImpact { get; set; } = false;
        public bool Collectible { get; set; } = true;
        public bool EntityHit { get; }
        public float Weight { get; set; } = 0.35f;
        public bool Stuck { get; set; }

        public void PreInitialize() { }
        public void SetFromConfig(IProjectileJsonConfig config) { }
        #endregion

        public float LifetimeSeconds { get; set; } = 0f;
        public bool IsScored { get; set; } = false;
        public bool InNetTransit { get; set; } = false;

        private double netExitY = 0;
        private Vec3d netCenter = new Vec3d();
        private float restTimeSeconds = 0f;

        protected bool beforeCollided;
        protected long msLaunch;
        protected Vec3d motionBeforeCollide = new Vec3d();
        private double prevY;

        public override bool IsInteractable => true;
        public override bool IsCreature => false;

        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage) => true;

        public override bool ReceiveDamage(DamageSource damageSource, float damage)
        {
            if (World != null && World.Side == EnumAppSide.Server)
            {
                // Attacking/punching the ball breaks it into its placed block or dropped item
                ConvertToPlacedBlock();
            }
            return false;
        }

        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource damageSourceForDeath = null)
        {
            if (Collectible && reason == EnumDespawnReason.Death && World != null && World.Side == EnumAppSide.Server)
            {
                Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
                if (ballItem != null)
                {
                    World.SpawnItemEntity(ProjectileStack ?? new ItemStack(ballItem, 1), Pos.XYZ);
                }
            }
            base.Die(reason, damageSourceForDeath);
        }

        public void StartNetTransit(Vec3d rimCenter)
        {
            InNetTransit = true;
            IsScored = true;
            netExitY = rimCenter.Y - 0.70;
            netCenter.Set(rimCenter.X, rimCenter.Y, rimCenter.Z);
            Pos.SetPos(rimCenter.X, rimCenter.Y - 0.15, rimCenter.Z);
            Pos.Motion.Set(0, -0.065, 0);
            WatchedAttributes.MarkAllDirty();
        }

        public EntityBasketball()
        {
            CollisionBox = new Cuboidf(-0.15f, 0f, -0.15f, 0.15f, 0.30f, 0.15f);
            SelectionBox = new Cuboidf(-0.15f, 0f, -0.15f, 0.15f, 0.30f, 0.15f);
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            if (CollisionBox == null)
            {
                CollisionBox = new Cuboidf(-0.15f, 0f, -0.15f, 0.15f, 0.30f, 0.15f);
            }
            SelectionBox = new Cuboidf(-0.15f, 0f, -0.15f, 0.15f, 0.30f, 0.15f);

            msLaunch = World.ElapsedMilliseconds;

            if (Api.Side == EnumAppSide.Server && FiredBy != null)
            {
                WatchedAttributes.SetLong("firedBy", FiredBy.EntityId);
            }
            if (Api.Side == EnumAppSide.Client)
            {
                long firedById = WatchedAttributes.GetLong("firedBy");
                if (firedById != 0) FiredBy = Api.World.GetEntityById(firedById);
            }

            if (World != null)
            {
                if (ProjectileStack == null)
                {
                    Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
                    if (ballItem != null)
                    {
                        ProjectileStack = new ItemStack(ballItem, 1);
                    }
                }

                if (ProjectileStack != null)
                {
                    ProjectileStack.ResolveBlockOrItem(World);
                }
            }

            var physics = GetBehavior<EntityBehaviorPassivePhysics>();
            if (physics != null)
            {
                physics.CollisionYExtra = 0f;
            }

            prevY = Pos.Y;
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (ShouldDespawn) return;

            LifetimeSeconds += dt;
            EntityPos pos = Pos;

            // Spin animation while in flight
            double speed = pos.Motion.Length();
            if (speed > 0.01 && !InNetTransit)
            {
                pos.Pitch = (World.ElapsedMilliseconds / 250f) % GameMath.TWOPI;
                pos.Yaw = (World.ElapsedMilliseconds / 300f) % GameMath.TWOPI;
            }

            if (InNetTransit)
            {
                // Slower controlled transit through net ropes
                pos.X = netCenter.X;
                pos.Z = netCenter.Z;
                pos.Motion.X = 0;
                pos.Motion.Z = 0;
                pos.Motion.Y = -0.065;

                if (pos.Y <= netExitY)
                {
                    // Exited bottom of net: resume normal gravity and drop down
                    InNetTransit = false;
                    pos.Motion.Y = -0.10;
                    pos.Motion.X = (World.Rand.NextDouble() - 0.5) * 0.02;
                    pos.Motion.Z = (World.Rand.NextDouble() - 0.5) * 0.02;
                }
                WatchedAttributes.MarkAllDirty();
            }

            if (World.Side == EnumAppSide.Server)
            {
                CheckHoopTrigger();
                CheckPlayerOrDummyPickup();
                CheckGroundRestConversion(dt);

                if (LifetimeSeconds > 600f)
                {
                    ConvertToPlacedBlock();
                }
            }

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
            prevY = pos.Y;
        }

        private void CheckGroundRestConversion(float dt)
        {
            if (InNetTransit) return;

            double speed = Pos.Motion.Length();
            if (OnGround && speed < 0.04)
            {
                restTimeSeconds += dt;
                if (restTimeSeconds >= 45f)
                {
                    ConvertToPlacedBlock();
                }
            }
            else
            {
                restTimeSeconds = 0f;
            }
        }

        public void ConvertToPlacedBlock()
        {
            if (World == null || World.Side != EnumAppSide.Server) return;

            BlockPos blockPos = Pos.AsBlockPos;
            Block bballBlock = World.GetBlock(new AssetLocation("basketballallstars:basketball"));

            if (bballBlock != null)
            {
                Block curBlock = World.BlockAccessor.GetBlock(blockPos);
                if (!curBlock.IsReplacableBy(bballBlock))
                {
                    blockPos = blockPos.UpCopy();
                    curBlock = World.BlockAccessor.GetBlock(blockPos);
                }

                if (curBlock.IsReplacableBy(bballBlock))
                {
                    World.BlockAccessor.SetBlock(bballBlock.BlockId, blockPos);
                    World.BlockAccessor.TriggerNeighbourBlockUpdate(blockPos);
                    Die(EnumDespawnReason.Removed);
                    return;
                }
            }

            // Fallback: spawn dropped item stack
            Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
            if (ballItem != null)
            {
                World.SpawnItemEntity(new ItemStack(ballItem, 1), Pos.XYZ);
            }
            Die(EnumDespawnReason.Removed);
        }

        public override void OnCollided()
        {
            EntityPos pos = Pos;

            if (!beforeCollided && World is IServerWorldAccessor)
            {
                if (InNetTransit)
                {
                    pos.Motion.Y = -0.065;
                    WatchedAttributes.MarkAllDirty();
                    beforeCollided = true;
                    return;
                }

                double impactSpeed = motionBeforeCollide.Length();

                if (impactSpeed > 0.04)
                {
                    BasketballAudioParticles.PlayBounceSound(World, pos.XYZ, (float)impactSpeed);
                    BasketballAudioParticles.SpawnBounceParticles(World, pos.XYZ);
                }

                // Restitution physics (Floor / Ceiling / Wall rebound)
                if (CollidedVertically)
                {
                    if (motionBeforeCollide.Y < -0.02)
                    {
                        pos.Motion.Y = -motionBeforeCollide.Y * 0.86;
                    }
                    else if (motionBeforeCollide.Y > 0.02)
                    {
                        // Ceiling collision
                        pos.Motion.Y = -motionBeforeCollide.Y * 0.86;
                    }
                    pos.Motion.X *= 0.94;
                    pos.Motion.Z *= 0.94;
                }

                if (CollidedHorizontally)
                {
                    bool hitX = Math.Abs(pos.Motion.X) < 0.001 && Math.Abs(motionBeforeCollide.X) > 0.005;
                    bool hitZ = Math.Abs(pos.Motion.Z) < 0.001 && Math.Abs(motionBeforeCollide.Z) > 0.005;

                    // If neither or both detected through zeroed motion, check delta magnitude
                    if (!hitX && !hitZ)
                    {
                        double deltaX = Math.Abs(motionBeforeCollide.X - pos.Motion.X);
                        double deltaZ = Math.Abs(motionBeforeCollide.Z - pos.Motion.Z);
                        if (deltaX > deltaZ + 0.005) hitX = true;
                        else if (deltaZ > deltaX + 0.005) hitZ = true;
                        else { hitX = true; hitZ = true; } // Corner hit
                    }

                    if (hitX && !hitZ)
                    {
                        // X-axis wall: reflect X, preserve tangential Z
                        pos.Motion.X = -motionBeforeCollide.X * 0.82;
                        pos.Motion.Z = motionBeforeCollide.Z * 0.94;
                    }
                    else if (hitZ && !hitX)
                    {
                        // Z-axis wall: reflect Z, preserve tangential X
                        pos.Motion.Z = -motionBeforeCollide.Z * 0.82;
                        pos.Motion.X = motionBeforeCollide.X * 0.94;
                    }
                    else
                    {
                        // Corner hit: rebound both axes
                        pos.Motion.X = -motionBeforeCollide.X * 0.82;
                        pos.Motion.Z = -motionBeforeCollide.Z * 0.82;
                    }
                }

                if (pos.Motion.Length() < 0.02)
                {
                    pos.Motion.Set(0, 0, 0);
                }

                WatchedAttributes.MarkAllDirty();
                beforeCollided = true;
            }

            base.OnCollided();
        }

        private void CheckHoopTrigger()
        {
            if (IsScored || InNetTransit) return;

            BlockPos ballBlockPos = Pos.AsBlockPos;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        BlockPos checkPos = ballBlockPos.AddCopy(dx, dy, dz);
                        Block block = World.BlockAccessor.GetBlock(checkPos);

                        if (block is BlockHoop hoop)
                        {
                            Vec3d rimCenter = hoop.GetRimCenter(checkPos);
                            Vec3d ballPos = Pos.XYZ;

                            double horizDist = Math.Sqrt(Math.Pow(ballPos.X - rimCenter.X, 2) + Math.Pow(ballPos.Z - rimCenter.Z, 2));
                            double vertDist = ballPos.Y - rimCenter.Y;

                            // Gentle radial rim gravity assist (within 0.95m above rim)
                            if (horizDist < 0.95 && vertDist > 0.15 && vertDist < 0.90 && Pos.Motion.Y < 0)
                            {
                                Vec3d toCenter = rimCenter.SubCopy(ballPos);
                                toCenter.Y = 0;
                                if (toCenter.Length() > 0.01)
                                {
                                    toCenter.Normalize();
                                    Pos.Motion.X += toCenter.X * 0.015;
                                    Pos.Motion.Z += toCenter.Z * 0.015;
                                }
                            }

                            // Clean score detection (ball passes downward through rim cylinder)
                            if (horizDist <= 0.45 && prevY >= rimCenter.Y - 0.05 && ballPos.Y <= rimCenter.Y + 0.15 && Pos.Motion.Y < -0.01)
                            {
                                ScoreBasket(checkPos, rimCenter);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void ScoreBasket(BlockPos hoopPos, Vec3d rimCenter)
        {
            StartNetTransit(rimCenter);

            IServerPlayer? scorerPlayer = (FiredBy as EntityPlayer)?.Player as IServerPlayer;

            if (World.BlockAccessor.GetBlockEntity(hoopPos) is BlockEntityHoop beHoop)
            {
                beHoop.ScoreBasket(scorerPlayer, false);
            }
            else
            {
                BasketballAudioParticles.PlayHoopScoreSounds(World, rimCenter, false);
                BasketballAudioParticles.SpawnHoopCelebrationParticles(World, rimCenter);
            }
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            base.OnInteract(byEntity, slot, hitPosition, mode);
            if (!Collectible) return;

            if (byEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer sPlayer)
            {
                // 0.5s grace period: prevents accidental throw upon releasing right-click used to pick up the ball off the ground
                byEntity.Attributes.SetLong("basketballPickupNoThrowUntilMs", World.ElapsedMilliseconds + 500);
                TryCollect(sPlayer);
            }
        }

        private void CheckPlayerOrDummyPickup()
        {
            if (!Collectible || InNetTransit) return;

            long timeSinceLaunch = World.ElapsedMilliseconds - msLaunch;
            if (timeSinceLaunch < 80) return;

            // 1. Practice Dummy catch: can receive passes/shots up to dummy height + 10%
            Entity? nearestDummy = World.GetNearestEntity(Pos.XYZ.AddCopy(0, -1.0, 0), 1.25f, 1.15f, e => e is EntityBasketballDummy dummy && dummy.Alive && !dummy.HasBall);
            if (nearestDummy is EntityBasketballDummy targetDummy)
            {
                double dummyHeight = targetDummy.CollisionBox?.Y2 ?? 1.85;
                double maxDummyHeight = dummyHeight * 1.10;
                double relDummyY = Pos.Y - targetDummy.Pos.Y;

                if (relDummyY >= -0.10 && relDummyY <= maxDummyHeight)
                {
                    targetDummy.CatchBall();
                    Die(EnumDespawnReason.PickedUp);
                    return;
                }
            }

            // 2. Player receiving catch (in-flight passes, rebounds, and ground pickups)
            // Calibrated strictly to player height + 10% (feet up to ~2.04m)
            EntityPlayer? nearestPlayer = World.GetNearestEntity(Pos.XYZ.AddCopy(0, -1.02, 0), 1.15f, 1.10f, e => e is EntityPlayer ep && ep.Alive) as EntityPlayer;
            if (nearestPlayer?.Player is IServerPlayer sPlayer)
            {
                double playerHeight = nearestPlayer.CollisionBox?.Y2 ?? 1.85;
                double maxCatchHeight = playerHeight * 1.10;
                double relY = Pos.Y - nearestPlayer.Pos.Y;

                if (relY >= -0.10 && relY <= maxCatchHeight)
                {
                    bool isThrower = FiredBy != null && FiredBy.EntityId == nearestPlayer.EntityId;

                    // If thrown by someone else: immediate catch
                    if (!isThrower)
                    {
                        TryCollect(sPlayer);
                        return;
                    }
                    // If thrown by self: catch on rebound, slow speed, or after 320ms in flight
                    else if (isThrower && (timeSinceLaunch > 320 || Pos.Motion.Length() < 0.25 || beforeCollided))
                    {
                        TryCollect(sPlayer);
                        return;
                    }
                }
            }
        }

        private void TryCollect(IServerPlayer player)
        {
            Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
            if (ballItem != null)
            {
                ItemStack stack = ProjectileStack ?? new ItemStack(ballItem, 1);
                if (player.InventoryManager.TryGiveItemstack(stack, true))
                {
                    BasketballAudioParticles.PlayCatchOrPickupSound(World, Pos.XYZ);
                    Die(EnumDespawnReason.PickedUp);
                }
            }
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            writer.Write(IsScored);
            writer.Write(restTimeSeconds);
            bool hasStack = ProjectileStack != null;
            writer.Write(hasStack);
            if (hasStack)
            {
                ProjectileStack!.ToBytes(writer);
            }
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();
            IsScored = reader.ReadBoolean();
            restTimeSeconds = reader.ReadSingle();
            bool hasStack = reader.ReadBoolean();
            if (hasStack)
            {
                ProjectileStack = World == null ? new ItemStack(reader) : new ItemStack(reader, World);
            }
            else
            {
                ProjectileStack = null;
            }
        }
    }
}
