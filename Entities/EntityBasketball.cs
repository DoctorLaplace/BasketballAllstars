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

        protected bool beforeCollided;
        protected long msLaunch;
        protected Vec3d motionBeforeCollide = new Vec3d();
        private double prevY;

        public override bool IsInteractable => true;

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
            SelectionBox = new Cuboidf(-0.25f, -0.05f, -0.25f, 0.25f, 0.45f, 0.25f);
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            if (CollisionBox == null)
            {
                CollisionBox = new Cuboidf(-0.15f, 0f, -0.15f, 0.15f, 0.30f, 0.15f);
            }
            SelectionBox = new Cuboidf(-0.25f, -0.05f, -0.25f, 0.25f, 0.45f, 0.25f);

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

            if (ProjectileStack?.Collectible != null)
            {
                ProjectileStack.ResolveBlockOrItem(World);
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

                if (LifetimeSeconds > 300f)
                {
                    Die();
                }
            }

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
            prevY = pos.Y;
        }

        public override void OnCollided()
        {
            EntityPos pos = Pos;

            if (!beforeCollided && World is IServerWorldAccessor)
            {
                if (InNetTransit)
                {
                    // Ignore collisions while inside the net ropes
                    pos.Motion.Y = -0.065;
                    WatchedAttributes.MarkAllDirty();
                    beforeCollided = true;
                    return;
                }

                float strength = GameMath.Clamp((float)motionBeforeCollide.Length() * 3, 0, 1);

                if (CollidedHorizontally)
                {
                    float xdir = pos.Motion.X == 0 ? -1 : 1;
                    float zdir = pos.Motion.Z == 0 ? -1 : 1;

                    // Wall bounce restitution (85% speed retention, was 70%)
                    pos.Motion.X = xdir * motionBeforeCollide.X * 0.85f;
                    pos.Motion.Z = zdir * motionBeforeCollide.Z * 0.85f;

                    if (strength > 0.05f)
                    {
                        BasketballAudioParticles.PlayBounceSound(World, Pos.XYZ, strength);
                    }
                }

                if (CollidedVertically && motionBeforeCollide.Y <= 0)
                {
                    if (Math.Abs(motionBeforeCollide.Y) > 0.015)
                    {
                        // Vertical bounce restitution (86% height energy retention, was 72%)
                        pos.Motion.Y = -motionBeforeCollide.Y * 0.86f;

                        // Horizontal momentum retention during ground bounce (95% speed retention, was 88%)
                        pos.Motion.X = motionBeforeCollide.X * 0.95f;
                        pos.Motion.Z = motionBeforeCollide.Z * 0.95f;

                        // Nudge upward slightly to prevent sticking inside floor block
                        pos.Y += 0.025;

                        BasketballAudioParticles.PlayBounceSound(World, Pos.XYZ, (float)Math.Abs(motionBeforeCollide.Y) * 2.0f);
                        BasketballAudioParticles.SpawnBounceParticles(World, Pos.XYZ);
                    }
                    else
                    {
                        pos.Motion.Y = 0;
                        pos.Motion.X = motionBeforeCollide.X * 0.94f;
                        pos.Motion.Z = motionBeforeCollide.Z * 0.94f;
                    }
                }

                WatchedAttributes.MarkAllDirty();
            }

            beforeCollided = true;
        }

        private void CheckHoopTrigger()
        {
            if (IsScored) return;

            int minX = (int)Math.Floor(Pos.X - 1.5);
            int maxX = (int)Math.Floor(Pos.X + 1.5);
            int minY = (int)Math.Floor(Pos.Y - 1.5);
            int maxY = (int)Math.Floor(Pos.Y + 1.5);
            int minZ = (int)Math.Floor(Pos.Z - 1.5);
            int maxZ = (int)Math.Floor(Pos.Z + 1.5);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        BlockPos checkPos = new BlockPos(x, y, z, Pos.Dimension);
                        Block block = World.BlockAccessor.GetBlock(checkPos);
                        if (block is BlockHoop hoopBlock)
                        {
                            Vec3d rimCenter = hoopBlock.GetRimCenter(checkPos);
                            double dx = Pos.X - rimCenter.X;
                            double dz = Pos.Z - rimCenter.Z;
                            double horizDist = Math.Sqrt(dx * dx + dz * dz);

                            double rimY = rimCenter.Y;

                            // Subtle gravity pull towards the center of the hoop within 1 block radius (0.95m) above the hoop:
                            if (horizDist <= 0.95 && horizDist > 0.02 && Pos.Y >= rimY - 0.10 && Pos.Y <= rimY + 1.25)
                            {
                                double pullRatio = (0.95 - horizDist) / 0.95;
                                double subtleGravity = pullRatio * 0.015;
                                Pos.Motion.X -= (dx / horizDist) * subtleGravity;
                                Pos.Motion.Z -= (dz / horizDist) * subtleGravity;

                                // Mild horizontal damping within the inner zone directly above the hoop net
                                if (horizDist <= 0.55 && Pos.Motion.Y < 0.20)
                                {
                                    double horizSpeed = Math.Sqrt(Pos.Motion.X * Pos.Motion.X + Pos.Motion.Z * Pos.Motion.Z);
                                    const double minSpeed = 0.035;
                                    if (horizSpeed > minSpeed)
                                    {
                                        double newSpeed = Math.Max(minSpeed, horizSpeed * 0.92);
                                        double factor = newSpeed / horizSpeed;
                                        Pos.Motion.X *= factor;
                                        Pos.Motion.Z *= factor;
                                    }
                                }
                            }

                            // If the ball hits, enters, or rests anywhere on the top of the hoop rim:
                            // Start net transit, score the basket, and drop through smoothly!
                            if (horizDist < 0.52 && Pos.Y >= rimY - 0.25 && Pos.Y <= rimY + 0.65)
                            {
                                if (Pos.Motion.Y <= 0.08 || beforeCollided || (prevY >= rimY && Pos.Y < rimY))
                                {
                                    var beHoop = World.BlockAccessor.GetBlockEntity(checkPos) as BlockEntityHoop;
                                    IServerPlayer? throwerPlayer = (FiredBy as EntityPlayer)?.Player as IServerPlayer;

                                    beHoop?.ScoreBasket(throwerPlayer, isDunk: false);

                                    StartNetTransit(rimCenter);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            base.OnInteract(byEntity, slot, hitPosition, mode);
            if (!Collectible) return;

            if (mode == EnumInteractMode.Interact && byEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer sPlayer)
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
                    Die();
                    return;
                }
            }

            // 2. Player receiving catch (in-flight passes, rebounds, and ground pickups)
            // Calibrated strictly to player height + 10% (feet up to ~2.04m)
            EntityPlayer? nearestPlayer = World.GetNearestEntity(Pos.XYZ.AddCopy(0, -1.02, 0), 1.15f, 1.10f, e => e is EntityPlayer ep && ep.Alive) as EntityPlayer;
            if (nearestPlayer?.Player is IServerPlayer sPlayer)
            {
                double playerHeight = nearestPlayer.CollisionBox?.Y2 ?? 1.85;
                double maxCatchHeight = playerHeight * 1.10; // Exact Height + 10%
                double relY = Pos.Y - nearestPlayer.Pos.Y;

                if (relY >= -0.10 && relY <= maxCatchHeight)
                {
                    bool isThrower = FiredBy?.EntityId == nearestPlayer.EntityId;

                    // If thrown by someone else (pass from dummy or teammate): catch in mid-air
                    if (!isThrower && timeSinceLaunch > 100)
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
                    Die();
                }
            }
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            writer.Write(IsScored);
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
