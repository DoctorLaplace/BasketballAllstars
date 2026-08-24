using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Entities
{
    public class EntityBasketballDummy : EntityHumanoid
    {
        private double lastDribbleTickMs = 0;
        private double playerContestStartMs = 0;
        private double nextStealAttemptMs = 0;
        private long contestingPlayerId = 0;
        private EntityBasketball? visualDribbleBall;

        public bool HasBall
        {
            get => WatchedAttributes.GetBool("hasBasketball", false);
            set
            {
                WatchedAttributes.SetBool("hasBasketball", value);
                WatchedAttributes.MarkAllDirty();
                UpdateDribbleBallState();
            }
        }

        public EntityBasketballDummy()
        {
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            UpdateDribbleBallState();
        }

        private void UpdateDribbleBallState()
        {
            if (World == null || World.Side != EnumAppSide.Server) return;

            if (HasBall)
            {
                if (visualDribbleBall == null || !visualDribbleBall.Alive)
                {
                    EntityProperties entityType = World.GetEntityType(new AssetLocation("basketballallstars:basketball"));
                    if (entityType != null)
                    {
                        Entity entity = World.ClassRegistry.CreateEntity(entityType);
                        if (entity is EntityBasketball ball)
                        {
                            Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
                            ball.FiredBy = this;
                            ball.ProjectileStack = new ItemStack(ballItem, 1);
                            ball.Collectible = false;
                            ball.Pos.SetPos(GetDribblePos(0.05));
                            World.SpawnEntity(ball);
                            visualDribbleBall = ball;
                        }
                    }
                }
            }
            else
            {
                if (visualDribbleBall != null && visualDribbleBall.Alive)
                {
                    visualDribbleBall.Die();
                    visualDribbleBall = null;
                }
            }
        }

        public Vec3d GetDribblePos(double bounceHeight)
        {
            // Facing vector from dummy chest towards the player
            double visualYaw = Pos.Yaw + GameMath.PIHALF;
            double fwdX = Math.Sin(visualYaw);
            double fwdZ = Math.Cos(visualYaw);

            // Perpendicular vector to the dummy's right side
            double rightX = -fwdZ;
            double rightZ = fwdX;

            double forwardOffset = 0.25;
            double sideOffset = 0.48;

            return Pos.XYZ.AddCopy(
                fwdX * forwardOffset + rightX * sideOffset,
                bounceHeight,
                fwdZ * forwardOffset + rightZ * sideOffset
            );
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            if (World == null || World.Side != EnumAppSide.Server) return;

            double nowMs = World.ElapsedMilliseconds;

            // 1. When holding the ball: animate bouncing basketball at the side and play dribble effects
            if (HasBall && OnGround)
            {
                // Ensure visual ball exists
                if (visualDribbleBall == null || !visualDribbleBall.Alive)
                {
                    UpdateDribbleBallState();
                }

                // Dribble cycle: 380ms per full bounce
                double cycleFraction = ((nowMs % 380) / 380.0);
                // Parabolic bounce: peaks at 0.70m, hits floor at 0.05m
                double bounceHeight = 0.05 + Math.Sin(cycleFraction * Math.PI) * 0.65;

                if (visualDribbleBall != null && visualDribbleBall.Alive)
                {
                    Vec3d ballPos = GetDribblePos(bounceHeight);
                    visualDribbleBall.Pos.SetPos(ballPos);
                    visualDribbleBall.Pos.Motion.Set(0, 0, 0);
                    visualDribbleBall.WatchedAttributes.MarkAllDirty();
                }

                if (nowMs - lastDribbleTickMs > 380)
                {
                    lastDribbleTickMs = nowMs;
                    Vec3d impactPos = GetDribblePos(0.05);
                    BasketballAudioParticles.PlayDribbleSound(World, impactPos);
                    BasketballAudioParticles.SpawnDribbleParticles(World, impactPos);
                }

                // Face nearest player when sparring
                EntityPlayer? nearPlayer = World.GetNearestEntity(Pos.XYZ, 4.0f, 2.0f, e => e is EntityPlayer ep && ep.Alive) as EntityPlayer;
                if (nearPlayer != null)
                {
                    Vec3d toPlayer = nearPlayer.Pos.XYZ.SubCopy(Pos.XYZ);
                    toPlayer.Y = 0;
                    if (toPlayer.Length() > 0.1)
                    {
                        // 90 degree compensation for strawdummy model alignment
                        Pos.Yaw = (float)Math.Atan2(toPlayer.X, toPlayer.Z) - (float)GameMath.PIHALF;
                    }
                }
            }

            // 2. When empty-handed: defend and contest dribbling players
            if (!HasBall && OnGround)
            {
                if (visualDribbleBall != null && visualDribbleBall.Alive)
                {
                    visualDribbleBall.Die();
                    visualDribbleBall = null;
                }

                EntityPlayer? ballHolder = World.GetNearestEntity(Pos.XYZ, 1.6f, 1.8f, e => e is EntityPlayer ep && ep.Alive && BasketballGameState.IsHoldingBall(ep)) as EntityPlayer;
                if (ballHolder?.Player is IServerPlayer sPlayer)
                {
                    // Face the ball-carrying player
                    Vec3d toPlayer = ballHolder.Pos.XYZ.SubCopy(Pos.XYZ);
                    toPlayer.Y = 0;
                    if (toPlayer.Length() > 0.1)
                    {
                        // 90 degree compensation for strawdummy model alignment
                        Pos.Yaw = (float)Math.Atan2(toPlayer.X, toPlayer.Z) - (float)GameMath.PIHALF;
                    }

                    // Check if player is immune to steals
                    bool isImmune = BasketballGameState.ServerInstance?.HasStealImmunity(sPlayer.PlayerUID, EntityId, nowMs) ?? false;

                    if (!isImmune)
                    {
                        if (contestingPlayerId != ballHolder.EntityId)
                        {
                            contestingPlayerId = ballHolder.EntityId;
                            playerContestStartMs = nowMs;
                        }

                        // If the player stays within tight steal range for 1.2 seconds, dummy swipes the ball!
                        if (nowMs - playerContestStartMs >= 1200 && nowMs >= nextStealAttemptMs)
                        {
                            ItemSlot? ballSlot = BasketballGameState.FindBallSlot(ballHolder);
                            if (ballSlot != null && !ballSlot.Empty)
                            {
                                ballSlot.TakeOut(1);
                                ballSlot.MarkDirty();

                                CatchBall();
                                nextStealAttemptMs = nowMs + 3000.0;
                                BasketballGameState.ServerInstance?.SetStealImmunity(sPlayer.PlayerUID, EntityId, nowMs + 1500.0);

                                BasketballAudioParticles.PlayStealSound(World, Pos.XYZ);
                                BasketballAudioParticles.SpawnBounceParticles(World, Pos.XYZ.AddCopy(0, 0.9, 0));
                            }
                        }
                    }
                }
                else
                {
                    contestingPlayerId = 0;
                }
            }
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (World.Side == EnumAppSide.Client || mode != EnumInteractMode.Interact)
            {
                base.OnInteract(byEntity, slot, hitPosition, mode);
                return;
            }

            if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player is not IServerPlayer sPlayer)
            {
                base.OnInteract(byEntity, slot, hitPosition, mode);
                return;
            }

            // Shift-right click: pickup/break the dummy or drop ball
            if (byEntity.Controls.ShiftKey)
            {
                if (HasBall)
                {
                    HasBall = false;
                    SpawnBallItem();
                    return;
                }

                // Drop dummy item and remove entity
                Item dummyItem = World.GetItem(new AssetLocation("basketballallstars:basketballdummy"));
                if (dummyItem != null)
                {
                    ItemStack stack = new ItemStack(dummyItem, 1);
                    if (!sPlayer.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        World.SpawnItemEntity(stack, Pos.XYZ.AddCopy(0, 0.5, 0));
                    }
                }
                Die();
                return;
            }

            // Normal right-click interaction:
            // 1. If player is holding a basketball -> hand/pass directly to dummy
            ItemSlot activeSlot = sPlayer.InventoryManager.ActiveHotbarSlot;
            if (activeSlot?.Itemstack?.Item?.Code?.Path == "basketball")
            {
                if (!HasBall)
                {
                    CatchBall();
                    activeSlot.TakeOut(1);
                    activeSlot.MarkDirty();
                    return;
                }
            }

            // 2. If dummy has ball:
            if (HasBall)
            {
                double dist = Pos.XYZ.DistanceTo(byEntity.Pos.XYZ);

                // If close (< 2.2m): Player steals / takes the ball
                if (dist < 2.2)
                {
                    HasBall = false;
                    Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
                    if (ballItem != null)
                    {
                        ItemStack stack = new ItemStack(ballItem, 1);
                        if (!sPlayer.InventoryManager.TryGiveItemstack(stack, true))
                        {
                            World.SpawnItemEntity(stack, Pos.XYZ.AddCopy(0, 0.5, 0));
                        }
                    }

                    BasketballGameState.ServerInstance?.SetStealImmunity(sPlayer.PlayerUID, EntityId, World.ElapsedMilliseconds + 1500.0);
                    BasketballAudioParticles.PlayStealSound(World, Pos.XYZ.AddCopy(0, 0.5, 0));
                    BasketballAudioParticles.SpawnBounceParticles(World, Pos.XYZ.AddCopy(0, 0.5, 0));
                    return;
                }
                // If standing back (>= 2.2m to 12.0m): Dummy passes the ball straight to the player!
                else if (dist <= 12.0)
                {
                    PassToPlayer(sPlayer);
                    return;
                }
            }

            base.OnInteract(byEntity, slot, hitPosition, mode);
        }

        public void CatchBall()
        {
            if (HasBall) return;
            HasBall = true;
            if (World != null)
            {
                BasketballAudioParticles.PlayCatchOrPickupSound(World, Pos.XYZ.AddCopy(0, 0.5, 0));
                BasketballAudioParticles.SpawnBounceParticles(World, Pos.XYZ.AddCopy(0, 0.5, 0));
            }
        }

        public void PassToPlayer(IServerPlayer targetPlayer)
        {
            if (!HasBall || World == null || World.Side != EnumAppSide.Server || targetPlayer.Entity == null) return;

            // Remove visual dribble ball before spawning throw projectile
            if (visualDribbleBall != null && visualDribbleBall.Alive)
            {
                visualDribbleBall.Die();
                visualDribbleBall = null;
            }

            EntityProperties entityType = World.GetEntityType(new AssetLocation("basketballallstars:basketball"));
            if (entityType != null)
            {
                Entity entity = World.ClassRegistry.CreateEntity(entityType);
                if (entity is EntityBasketball ball)
                {
                    Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
                    ball.FiredBy = this;
                    ball.ProjectileStack = new ItemStack(ballItem, 1);

                    Vec3d spawnPos = Pos.XYZ.AddCopy(0, 1.2, 0);
                    Vec3d targetPos = targetPlayer.Entity.Pos.XYZ.AddCopy(0, 1.2, 0);
                    Vec3d throwDir = targetPos.SubCopy(spawnPos).Normalize();

                    ball.Pos.SetPos(spawnPos.AddCopy(throwDir.X * 0.4, 0, throwDir.Z * 0.4));
                    ball.Pos.Motion.Set(throwDir.X * 0.40, throwDir.Y * 0.40 + 0.08, throwDir.Z * 0.40);

                    World.SpawnEntity(ball);
                    BasketballAudioParticles.PlayThrowSound(World, spawnPos);

                    HasBall = false;
                }
            }
        }

        private void SpawnBallItem()
        {
            Item ballItem = World.GetItem(new AssetLocation("basketballallstars:basketball"));
            if (ballItem != null)
            {
                ItemStack stack = new ItemStack(ballItem, 1);
                World.SpawnItemEntity(stack, Pos.XYZ.AddCopy(0, 0.5, 0));
            }
        }

        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource? damageSourceForDeath = null)
        {
            if (visualDribbleBall != null && visualDribbleBall.Alive)
            {
                visualDribbleBall.Die();
                visualDribbleBall = null;
            }
            base.Die(reason, damageSourceForDeath);
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (visualDribbleBall != null && visualDribbleBall.Alive)
            {
                visualDribbleBall.Die();
                visualDribbleBall = null;
            }
            base.OnEntityDespawn(despawn);
        }

        public override void ToBytes(BinaryWriter writer, bool isForClient)
        {
            base.ToBytes(writer, isForClient);
            writer.Write(HasBall);
        }

        public override void FromBytes(BinaryReader reader, bool isForClient)
        {
            base.FromBytes(reader, isForClient);
            HasBall = reader.ReadBoolean();
        }
    }
}
