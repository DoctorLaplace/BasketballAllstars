using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Blocks;
using BasketballAllstars.Entities;
using BasketballAllstars.Items;
using BasketballAllstars.Network;

namespace BasketballAllstars.Systems
{
    public enum EnumDunkStyle
    {
        Normal = 0,     // Heroic tomahawk slam glide with tangent tilt (33.3% chance)
        Spin360 = 1,    // 360 to 2160 degree horizontal body spin (33.3% chance)
        FrontFlip = 2   // 360 to 2160 degree forward somersault front flip (33.3% chance)
    }

    public class ActiveTrajectory
    {
        public string PlayerUid { get; set; } = "";
        public Vec3d StartPos { get; set; } = new Vec3d();
        public Vec3d TargetPos { get; set; } = new Vec3d();
        public float ArcHeight { get; set; } = 5.0f;
        public float DurationSeconds { get; set; } = 1.4f;
        public double StartLocalMs { get; set; } = 0;
        public bool IsDunk { get; set; } = false;
        public BlockPos? TargetHoopPos { get; set; } = null;
        public string TargetPlayerUid { get; set; } = "";
        public bool IsSuspended { get; set; } = false;
        public Vec3d SuspendedPos { get; set; } = new Vec3d();
        public double SuspendStartMs { get; set; } = 0;
        public int DunkStyle { get; set; } = 0;
        public int Revolutions { get; set; } = 1;
        public float FlightYaw { get; set; } = 0f;
    }

    public class DunkTrajectorySystem
    {
        public static DunkTrajectorySystem? ServerInstance { get; private set; }
        public static DunkTrajectorySystem? ClientInstance { get; private set; }
        public static DunkTrajectorySystem? Instance => ServerInstance ?? ClientInstance;
        public static DunkTrajectorySystem? Get(ICoreAPI api) => api?.Side == EnumAppSide.Server ? ServerInstance : (ClientInstance ?? ServerInstance);
        public static DunkTrajectorySystem? Get(EnumAppSide side) => side == EnumAppSide.Server ? ServerInstance : (ClientInstance ?? ServerInstance);

        private readonly ICoreAPI api;
        private readonly Dictionary<string, ActiveTrajectory> activeTrajectories = new();
        private readonly Dictionary<string, ActiveTrajectory> clientTrajectories = new();
        private static readonly Random rand = new Random();

        public float ClientJumpCharge { get; set; } = 0f;
        public bool ClientIsChargingJump { get; set; } = false;
        public BlockPos? ClientLockedHoopPos { get; set; } = null;
        public string ClientLockedDunkerUid { get; set; } = "";
        private bool wasSpaceHeld = false;
        public bool WasSpaceHeld => wasSpaceHeld;
        public bool SuppressJumpUntilRelease { get; set; } = false;

        public bool HasActiveClientTrajectory => clientTrajectories.Count > 0;
        public string LocalPlayerUid => (api as ICoreClientAPI)?.World.Player?.PlayerUID ?? "";

        public DunkTrajectorySystem(ICoreAPI api)
        {
            this.api = api;
            if (api.Side == EnumAppSide.Server)
            {
                ServerInstance = this;
            }
            else
            {
                ClientInstance = this;
            }
        }

        public void Start()
        {
            if (api.Side == EnumAppSide.Server)
            {
                (api as ICoreServerAPI)?.Event.RegisterGameTickListener(OnServerTrajectoryTick, 15);
            }
            else if (api.Side == EnumAppSide.Client)
            {
                (api as ICoreClientAPI)?.Event.RegisterGameTickListener(OnClientJumpTick, 20);
                (api as ICoreClientAPI)?.Event.RegisterGameTickListener(OnClientTrajectoryTick, 1);
            }
        }

        public bool IsPlayerInTrajectory(string playerUid, out ActiveTrajectory traj)
        {
            if (clientTrajectories.TryGetValue(playerUid, out var cTraj))
            {
                traj = cTraj;
                return true;
            }
            if (activeTrajectories.TryGetValue(playerUid, out var sTraj))
            {
                traj = sTraj;
                return true;
            }
            traj = null!;
            return false;
        }

        public void ApplyDunkStyleRotation(EntityPlayer entityPlayer, ActiveTrajectory traj)
        {
            if (entityPlayer == null || traj == null) return;

            if (traj.IsSuspended)
            {
                // Hold pose facing the clash direction and violently vibrate
                double time = (entityPlayer.World.ElapsedMilliseconds / 1000.0) * 45.0;
                float jitterYaw = (float)(Math.Sin(time * 1.3) * 0.045 + Math.Cos(time * 2.1) * 0.025);
                float jitterPitch = (float)(Math.Cos(time * 1.7) * 0.04);
                float jitterRoll = (float)(Math.Sin(time * 1.9) * 0.05);

                entityPlayer.BodyYaw = traj.FlightYaw + jitterYaw;
                entityPlayer.WalkYaw = traj.FlightYaw + jitterYaw;
                entityPlayer.Pos.Yaw = traj.FlightYaw + jitterYaw;
                entityPlayer.Pos.Roll = jitterRoll;
                entityPlayer.WalkPitch = jitterPitch;

                // Timed spurts of directional plume spark bursts
                if (entityPlayer.World.Side == EnumAppSide.Client && (entityPlayer.World.ElapsedMilliseconds % 280 < 30))
                {
                    BasketballAudioParticles.SpawnClashSparks(entityPlayer.World, entityPlayer.Pos.XYZ.AddCopy(0, 1.0, 0));
                }
                return;
            }

            double elapsedMs = entityPlayer.World.ElapsedMilliseconds - traj.StartLocalMs;
            float t = Math.Clamp((float)(elapsedMs / (Math.Max(traj.DurationSeconds, 0.1f) * 1000.0)), 0f, 1f);
            float flightYaw = traj.FlightYaw;
            int revs = Math.Max(traj.Revolutions, 1);

            switch (traj.DunkStyle)
            {
                case (int)EnumDunkStyle.Spin360:
                    // 360 to 2160 Degree Horizontal Body Spin (1 to 6 full spins)
                    float spinYaw = (float)(flightYaw + t * GameMath.TWOPI * revs);
                    entityPlayer.BodyYaw = spinYaw;
                    entityPlayer.WalkYaw = spinYaw;
                    entityPlayer.Pos.Yaw = spinYaw;
                    entityPlayer.WalkPitch = 0f;
                    break;

                case (int)EnumDunkStyle.FrontFlip:
                    // Forward Somersault Front Flip (1 to 6 full front flips)
                    entityPlayer.BodyYaw = flightYaw;
                    entityPlayer.WalkYaw = flightYaw;
                    entityPlayer.Pos.Yaw = flightYaw;
                    entityPlayer.WalkPitch = (float)(t * GameMath.TWOPI * revs);
                    break;

                case (int)EnumDunkStyle.Normal:
                default:
                    // Heroic Tomahawk Slam with Forward Face & Dynamic Tangent Tilt
                    double dx = traj.TargetPos.X - traj.StartPos.X;
                    double dz = traj.TargetPos.Z - traj.StartPos.Z;
                    double horizDist = Math.Sqrt(dx * dx + dz * dz);
                    double velY = (traj.TargetPos.Y - traj.StartPos.Y) + traj.ArcHeight * 4.0 * (1.0 - 2.0 * t);
                    float tangentPitch = (float)Math.Atan2(velY, Math.Max(horizDist, 0.1));

                    entityPlayer.BodyYaw = flightYaw;
                    entityPlayer.WalkYaw = flightYaw;
                    entityPlayer.Pos.Yaw = flightYaw;
                    entityPlayer.WalkPitch = -tangentPitch * 0.70f;
                    break;
            }
        }

        // ========================================================================
        // Server Trajectory Logic
        // ========================================================================

        public void StartDunkTrajectory(IServerPlayer player, BlockPos hoopPos, float charge, int requestedStyle = -1, int requestedRevs = -1)
        {
            if (player.Entity == null || !player.Entity.Alive || !player.Entity.OnGround || charge < 0.50f) return;

            Block block = api.World.BlockAccessor.GetBlock(hoopPos);
            if (block is not BlockHoop hoopBlock) return;

            var beHoop = api.World.BlockAccessor.GetBlockEntity(hoopPos) as BlockEntityHoop;
            if (beHoop != null && !beHoop.IsDunkable) return;

            Vec3d rimCenter = hoopBlock.GetRimCenter(hoopPos);
            Vec3d startPos = player.Entity.Pos.XYZ.Clone();

            // Approach vector from rim center towards player: arcs directly towards the side closest to the player
            double dxFromRim = startPos.X - rimCenter.X;
            double dzFromRim = startPos.Z - rimCenter.Z;
            double horizDistFromRim = Math.Sqrt(dxFromRim * dxFromRim + dzFromRim * dzFromRim);
            Vec3d approachDir = horizDistFromRim > 0.001 
                ? new Vec3d(dxFromRim / horizDistFromRim, 0, dzFromRim / horizDistFromRim) 
                : new Vec3d(0, 0, 1);

            // Player body destination: on the exact approach angle closest to player (0.70m from rim)
            Vec3d playerTargetPos = rimCenter.AddCopy(approachDir.X * 0.70, -0.30, approachDir.Z * 0.70);

            // Flight yaw points directly into the rim center from takeoff
            float flightYaw = (float)Math.Atan2(rimCenter.X - startPos.X, rimCenter.Z - startPos.Z);

            if (activeTrajectories.ContainsKey(player.PlayerUID)) return;

            double distance = startPos.DistanceTo(rimCenter);
            float duration = (float)Math.Clamp(distance / 6.0, 0.9, 2.5);
            float arcHeight = (float)Math.Clamp(distance * 0.38 + 2.5, 3.0, 7.5);

            int dunkStyle = requestedStyle >= 0 ? requestedStyle : rand.Next(0, 3);
            int revolutions = requestedRevs >= 1 ? requestedRevs : rand.Next(1, 3);

            var traj = new ActiveTrajectory
            {
                PlayerUid = player.PlayerUID,
                StartPos = startPos,
                TargetPos = playerTargetPos,
                ArcHeight = arcHeight,
                DurationSeconds = duration,
                StartLocalMs = api.World.ElapsedMilliseconds,
                IsDunk = true,
                TargetHoopPos = hoopPos,
                DunkStyle = dunkStyle,
                Revolutions = revolutions,
                FlightYaw = flightYaw
            };

            activeTrajectories[player.PlayerUID] = traj;
            player.Entity.Stats.Set("fallDamageFactor", "basketball_dunk", 0.0f, false);
            player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", true);
            player.Entity.Attributes.SetLong("basketballNoPickupUntilMs", api.World.ElapsedMilliseconds + (long)(duration * 1000) + 1000);
            player.Entity.ServerControls.Gliding = true;
            player.Entity.ServerControls.IsFlying = true;
            player.Entity.Controls.Gliding = true;
            player.Entity.Controls.IsFlying = true;

            // Sync trajectory to all clients
            var serverChannel = (api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            serverChannel?.BroadcastPacket(new TrajectorySyncMessage
            {
                PlayerUid = player.PlayerUID,
                StartPos = startPos,
                TargetPos = traj.TargetPos,
                DurationSeconds = duration,
                ArcHeight = arcHeight,
                IsDunk = true,
                DunkStyle = dunkStyle,
                Revolutions = revolutions
            });

            BasketballAudioParticles.PlayThrowSound(api.World, startPos);
        }

        public void StartInterceptTrajectory(IServerPlayer player, string targetDunkerUid, float charge)
        {
            if (player.Entity == null) return;
            IServerPlayer? dunkerPlayer = (api as ICoreServerAPI)?.World.PlayerByUid(targetDunkerUid) as IServerPlayer;
            if (dunkerPlayer?.Entity == null) return;

            // Interceptions can only be initiated against players who are actively performing a slam dunk!
            if (!activeTrajectories.TryGetValue(targetDunkerUid, out var dunkerTraj) || !dunkerTraj.IsDunk)
            {
                return;
            }

            Vec3d startPos = player.Entity.Pos.XYZ.Clone();
            Vec3d dunkerPos = dunkerPlayer.Entity.Pos.XYZ.Clone();
            double distance = startPos.DistanceTo(dunkerPos);
            float duration = (float)Math.Clamp(distance / 12.0, 0.45, 1.25);
            float arcHeight = (float)Math.Clamp(distance * 0.30 + 1.5, 2.0, 5.5);

            double dx = dunkerPos.X - startPos.X;
            double dz = dunkerPos.Z - startPos.Z;
            float flightYaw = (float)Math.Atan2(dx, dz);

            int dunkStyle = (int)EnumDunkStyle.FrontFlip;
            int revolutions = 2;

            var traj = new ActiveTrajectory
            {
                PlayerUid = player.PlayerUID,
                TargetPlayerUid = targetDunkerUid,
                StartPos = startPos,
                TargetPos = dunkerPos,
                ArcHeight = arcHeight,
                DurationSeconds = duration,
                StartLocalMs = api.World.ElapsedMilliseconds,
                IsDunk = false,
                DunkStyle = dunkStyle,
                Revolutions = revolutions,
                FlightYaw = flightYaw
            };

            activeTrajectories[player.PlayerUID] = traj;
            player.Entity.Stats.Set("fallDamageFactor", "basketball_dunk", 0.0f, false);
            player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", true);
            player.Entity.ServerControls.Gliding = true;
            player.Entity.ServerControls.IsFlying = true;
            player.Entity.Controls.Gliding = true;
            player.Entity.Controls.IsFlying = true;

            var serverChannel = (api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            serverChannel?.BroadcastPacket(new TrajectorySyncMessage
            {
                PlayerUid = player.PlayerUID,
                TargetPlayerUid = targetDunkerUid,
                StartPos = startPos,
                TargetPos = dunkerPos,
                DurationSeconds = duration,
                ArcHeight = arcHeight,
                IsDunk = false,
                DunkStyle = dunkStyle,
                Revolutions = revolutions
            });

            BasketballAudioParticles.PlayThrowSound(api.World, startPos);
        }

        public ActiveTrajectory? GetActiveTrajectory(string playerUid)
        {
            activeTrajectories.TryGetValue(playerUid, out var traj);
            return traj;
        }

        public void SuspendTrajectory(string playerUid, Vec3d freezePos)
        {
            if (activeTrajectories.TryGetValue(playerUid, out var traj))
            {
                traj.IsSuspended = true;
                traj.SuspendedPos = freezePos.Clone();
                traj.SuspendStartMs = api.World.ElapsedMilliseconds;
            }
            if (clientTrajectories.TryGetValue(playerUid, out var cTraj))
            {
                cTraj.IsSuspended = true;
                cTraj.SuspendedPos = freezePos.Clone();
                cTraj.SuspendStartMs = api.World.ElapsedMilliseconds;
            }
        }

        public void ResumeTrajectory(string playerUid)
        {
            if (activeTrajectories.TryGetValue(playerUid, out var traj) && traj.IsSuspended)
            {
                traj.StartLocalMs += (api.World.ElapsedMilliseconds - traj.SuspendStartMs);
                traj.IsSuspended = false;
            }
            if (clientTrajectories.TryGetValue(playerUid, out var cTraj) && cTraj.IsSuspended)
            {
                cTraj.StartLocalMs += (api.World.ElapsedMilliseconds - cTraj.SuspendStartMs);
                cTraj.IsSuspended = false;
            }
        }

        public void CancelTrajectory(string playerUid)
        {
            activeTrajectories.Remove(playerUid);
            clientTrajectories.Remove(playerUid);

            if (api is ICoreServerAPI sapi)
            {
                var player = sapi.World.PlayerByUid(playerUid) as IServerPlayer;
                if (player?.Entity != null)
                {
                    player.Entity.WalkPitch = 0f;
                    player.Entity.Pos.Roll = 0f;
                    player.Entity.Pos.HeadPitch = 0f;
                    player.Entity.Pos.HeadYaw = 0f;
                    player.Entity.Controls.Gliding = false;
                    player.Entity.Controls.IsFlying = player.WorldData?.FreeMove ?? false;
                    player.Entity.ServerControls.Gliding = false;
                    player.Entity.ServerControls.IsFlying = player.WorldData?.FreeMove ?? false;
                    player.Entity.Stats.Set("fallDamageFactor", "basketball_dunk", 0.0f, false);
                    player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", true);
                    sapi.Event.RegisterCallback((dt) =>
                    {
                        if (player?.Entity != null && player.Entity.Alive)
                        {
                            player.Entity.Stats.Remove("fallDamageFactor", "basketball_dunk");
                            player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", false);
                        }
                    }, 2000);
                }

                var serverChannel = sapi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                serverChannel?.BroadcastPacket(new TrajectoryCancelMessage
                {
                    PlayerUid = playerUid,
                    ReleaseMotion = player?.Entity?.Pos.Motion != null ? new Vec3d(player.Entity.Pos.Motion.X, player.Entity.Pos.Motion.Y, player.Entity.Pos.Motion.Z) : null
                });
            }
            else if (api is ICoreClientAPI capi)
            {
                var player = capi.World.PlayerByUid(playerUid);
                if (player?.Entity != null)
                {
                    player.Entity.WalkPitch = 0f;
                    player.Entity.Pos.Roll = 0f;
                    player.Entity.Pos.HeadPitch = 0f;
                    player.Entity.Pos.HeadYaw = 0f;
                    player.Entity.Controls.Gliding = false;
                    player.Entity.Controls.IsFlying = player.WorldData?.FreeMove ?? false;
                    player.Entity.ServerControls.Gliding = false;
                    player.Entity.ServerControls.IsFlying = player.WorldData?.FreeMove ?? false;
                }
            }
        }

        public void ReleaseMidDunkTrajectory(string playerUid, EntityPlayer entityPlayer)
        {
            ActiveTrajectory? traj = null;
            if (!activeTrajectories.TryGetValue(playerUid, out traj))
            {
                clientTrajectories.TryGetValue(playerUid, out traj);
            }

            if (traj != null)
            {
                double elapsedMs = api.World.ElapsedMilliseconds - traj.StartLocalMs;
                float duration = Math.Max(traj.DurationSeconds, 0.1f);
                float t = Math.Clamp((float)(elapsedMs / (duration * 1000.0)), 0f, 1f);

                // Compute instantaneous velocity vector
                double vx = (traj.TargetPos.X - traj.StartPos.X) / duration;
                double vz = (traj.TargetPos.Z - traj.StartPos.Z) / duration;
                double vy = (traj.TargetPos.Y - traj.StartPos.Y) / duration + (traj.ArcHeight * 4.0 * (1.0 - 2.0 * t)) / duration;

                Vec3d releaseMotion = new Vec3d(vx / 30.0, Math.Max(vy / 30.0, 0.05), vz / 30.0);
                entityPlayer.Pos.Motion.Set(releaseMotion.X, releaseMotion.Y, releaseMotion.Z);
                entityPlayer.Stats.Set("fallDamageFactor", "basketball_dunk", 0.0f, false);
                entityPlayer.WatchedAttributes.SetBool("basketballFallImmunity", true);
                api.Event.RegisterCallback((dt) =>
                {
                    if (entityPlayer != null && entityPlayer.Alive)
                    {
                        entityPlayer.Stats.Remove("fallDamageFactor", "basketball_dunk");
                        entityPlayer.WatchedAttributes.SetBool("basketballFallImmunity", false);
                    }
                }, 2000);
                entityPlayer.Attributes.SetLong("basketballNoPickupUntilMs", api.World.ElapsedMilliseconds + 1000);

                activeTrajectories.Remove(playerUid);
                clientTrajectories.Remove(playerUid);

                if (api is ICoreServerAPI sapi)
                {
                    var serverChannel = sapi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                    serverChannel?.BroadcastPacket(new TrajectoryCancelMessage
                    {
                        PlayerUid = playerUid,
                        ReleaseMotion = releaseMotion
                    });
                }
            }
        }

        public void OnClientTrajectoryCancel(TrajectoryCancelMessage msg)
        {
            if (api is not ICoreClientAPI capi) return;
            var player = capi.World.PlayerByUid(msg.PlayerUid);
            if (player?.Entity != null)
            {
                player.Entity.WalkPitch = 0f;
                player.Entity.Pos.Roll = 0f;
                player.Entity.Controls.Gliding = false;
                player.Entity.Controls.IsFlying = player.WorldData?.FreeMove ?? false;
                player.Entity.ServerControls.Gliding = false;
                player.Entity.ServerControls.IsFlying = player.WorldData?.FreeMove ?? false;
                if (msg.ReleaseMotion != null)
                {
                    player.Entity.Pos.Motion.Set(msg.ReleaseMotion.X, msg.ReleaseMotion.Y, msg.ReleaseMotion.Z);
                }
            }
            clientTrajectories.Remove(msg.PlayerUid);
        }

        private void OnServerTrajectoryTick(float dt)
        {
            if (api is not ICoreServerAPI sapi) return;

            var completedList = new List<string>();

            foreach (var kvp in activeTrajectories)
            {
                var traj = kvp.Value;
                IServerPlayer? player = sapi.World.PlayerByUid(traj.PlayerUid) as IServerPlayer;
                if (player?.Entity == null || !player.Entity.Alive)
                {
                    completedList.Add(kvp.Key);
                    continue;
                }

                if (traj.IsSuspended)
                {
                    double vTime = (player.Entity.World.ElapsedMilliseconds / 1000.0) * 45.0;
                    double vibX = Math.Sin(vTime * 1.5) * 0.045 + Math.Cos(vTime * 2.7) * 0.025;
                    double vibY = Math.Cos(vTime * 1.8) * 0.04 + Math.Sin(vTime * 2.2) * 0.02;
                    double vibZ = Math.Sin(vTime * 1.9) * 0.045 + Math.Cos(vTime * 1.3) * 0.025;
                    player.Entity.Pos.SetPos(traj.SuspendedPos.AddCopy(vibX, vibY, vibZ));
                    player.Entity.Pos.Motion.Set(0, 0, 0);
                    player.Entity.ServerControls.Gliding = true;
                    player.Entity.ServerControls.IsFlying = true;
                    player.Entity.Controls.Gliding = true;
                    player.Entity.Controls.IsFlying = true;
                    continue;
                }

                Vec3d targetPos = traj.TargetPos;
                if (!string.IsNullOrEmpty(traj.TargetPlayerUid))
                {
                    var targetPlayer = sapi.World.PlayerByUid(traj.TargetPlayerUid);
                    if (targetPlayer?.Entity != null)
                    {
                        targetPos = targetPlayer.Entity.Pos.XYZ;
                        double diffX = targetPos.X - player.Entity.Pos.X;
                        double diffZ = targetPos.Z - player.Entity.Pos.Z;
                        if (diffX * diffX + diffZ * diffZ > 0.01)
                        {
                            traj.FlightYaw = (float)Math.Atan2(diffX, diffZ);
                        }
                    }
                }

                double elapsedMs = sapi.World.ElapsedMilliseconds - traj.StartLocalMs;
                float t = Math.Clamp((float)(elapsedMs / (Math.Max(traj.DurationSeconds, 0.1f) * 1000.0)), 0f, 1f);

                double currentX = traj.StartPos.X + (targetPos.X - traj.StartPos.X) * t;
                double currentZ = traj.StartPos.Z + (targetPos.Z - traj.StartPos.Z) * t;
                double baseY = traj.StartPos.Y + (targetPos.Y - traj.StartPos.Y) * t;
                double arcOffset = traj.ArcHeight * 4.0 * t * (1.0 - t);
                double currentY = baseY + arcOffset;

                // Pivot compensation for front flip: pivot around the center of the body (1.30m up from feet)
                if (traj.DunkStyle == (int)EnumDunkStyle.FrontFlip)
                {
                    float pitch = (float)(t * GameMath.TWOPI * traj.Revolutions);
                    currentY += 1.30 * (1.0 - Math.Cos(pitch));
                    currentX -= 1.30 * Math.Sin(pitch) * Math.Sin(traj.FlightYaw);
                    currentZ -= 1.30 * Math.Sin(pitch) * Math.Cos(traj.FlightYaw);
                }

                player.Entity.Pos.SetPos(currentX, currentY, currentZ);
                player.Entity.Pos.Motion.Set(0, 0, 0);

                // Check collision with interceptors via AirClashSystem
                AirClashSystem.Instance?.CheckAirClashes(player, currentX, currentY, currentZ);

                if (t >= 1.0f)
                {
                    completedList.Add(kvp.Key);

                    if (traj.IsDunk && traj.TargetHoopPos != null)
                    {
                        // SLAM DUNK! Only score if the player is still holding the basketball (hasn't thrown or lost it)
                        if (player.Entity != null && BasketballGameState.IsHoldingBall(player.Entity))
                        {
                            // 1. Take the basketball out of the player's hands and drop it through the hoop!
                            ItemSlot? ballSlot = BasketballGameState.FindBallSlot(player.Entity);
                            ItemStack? ballStack = ballSlot?.TakeOut(1);
                            ballSlot?.MarkDirty();

                            // 2. Score the basket and trigger sound/particles
                            var beHoop = api.World.BlockAccessor.GetBlockEntity(traj.TargetHoopPos) as BlockEntityHoop;
                            beHoop?.ScoreBasket(player, isDunk: true);

                            Block hoopB = api.World.BlockAccessor.GetBlock(traj.TargetHoopPos);
                            Vec3d hoopRimCenter = hoopB is BlockHoop hb ? hb.GetRimCenter(traj.TargetHoopPos) : traj.TargetPos;

                            // 3. Spawn physical basketball entity dropping cleanly through the net
                            EntityProperties entityType = api.World.GetEntityType(new AssetLocation("basketballallstars:basketball"));
                            if (entityType != null)
                            {
                                Entity entity = api.World.ClassRegistry.CreateEntity(entityType);
                                if (entity is EntityBasketball ball)
                                {
                                    ball.FiredBy = player.Entity;
                                    ball.ProjectileStack = ballStack ?? new ItemStack(api.World.GetItem(new AssetLocation("basketballallstars:basketball")), 1);
                                    ball.StartNetTransit(hoopRimCenter);
                                    api.World.SpawnEntity(ball);
                                }
                            }

                            // Spawn dunk celebration effects directly at the hoop basket
                            BasketballAudioParticles.SpawnHoopCelebrationParticles(api.World, hoopRimCenter.AddCopy(0, 0.35, 0));
                        }
                    }
                }
            }

            foreach (var key in completedList)
            {
                if (activeTrajectories.TryGetValue(key, out var finishedTraj))
                {
                    activeTrajectories.Remove(key);
                    var player = sapi.World.PlayerByUid(key) as IServerPlayer;
                    if (player?.Entity != null)
                    {
                        // 2.0s lingering fall damage immunity after completing dunk
                        player.Entity.Stats.Set("fallDamageFactor", "basketball_dunk", 0.0f, false);
                        player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", true);
                        sapi.Event.RegisterCallback((dt) =>
                        {
                            if (player?.Entity != null && player.Entity.Alive)
                            {
                                player.Entity.Stats.Remove("fallDamageFactor", "basketball_dunk");
                                player.Entity.WatchedAttributes.SetBool("basketballFallImmunity", false);
                            }
                        }, 2000);

                        player.Entity.Attributes.SetLong("basketballNoPickupUntilMs", api.World.ElapsedMilliseconds + 1000);
                        player.Entity.ServerControls.Gliding = false;
                        player.Entity.ServerControls.IsFlying = player.WorldData?.FreeMove ?? false;
                        player.Entity.Controls.Gliding = false;
                        player.Entity.Controls.IsFlying = player.WorldData?.FreeMove ?? false;
                    }
                }
            }
        }

        // ========================================================================
        // Client Aim, Spacebar Jump Charging & Flight Simulation
        // ========================================================================

        public void OnClientTrajectorySync(TrajectorySyncMessage msg)
        {
            // If the local player already predicted this trajectory, only sync the authoritative style & revolutions
            if (clientTrajectories.TryGetValue(msg.PlayerUid, out var existingTraj))
            {
                existingTraj.StartPos = msg.StartPos.Clone();
                existingTraj.TargetPos = msg.TargetPos.Clone();
                existingTraj.TargetPlayerUid = msg.TargetPlayerUid;
                existingTraj.ArcHeight = msg.ArcHeight;
                existingTraj.DurationSeconds = msg.DurationSeconds;
                existingTraj.StartLocalMs = api.World.ElapsedMilliseconds;
                existingTraj.DunkStyle = msg.DunkStyle;
                existingTraj.Revolutions = msg.Revolutions;
                existingTraj.FlightYaw = existingTraj.FlightYaw;
                existingTraj.IsDunk = msg.IsDunk;
                return;
            }

            double dx = msg.TargetPos.X - msg.StartPos.X;
            double dz = msg.TargetPos.Z - msg.StartPos.Z;
            float flightYaw = (float)Math.Atan2(dx, dz);

            var traj = new ActiveTrajectory
            {
                PlayerUid = msg.PlayerUid,
                TargetPlayerUid = msg.TargetPlayerUid,
                StartPos = msg.StartPos.Clone(),
                TargetPos = msg.TargetPos.Clone(),
                ArcHeight = msg.ArcHeight,
                DurationSeconds = msg.DurationSeconds,
                StartLocalMs = api.World.ElapsedMilliseconds,
                IsDunk = msg.IsDunk,
                DunkStyle = msg.DunkStyle,
                Revolutions = msg.Revolutions,
                FlightYaw = flightYaw
            };
            clientTrajectories[msg.PlayerUid] = traj;
        }

        private void OnClientTrajectoryTick(float dt)
        {
            if (api is not ICoreClientAPI capi) return;

            var toRemove = new List<string>();

            foreach (var kvp in clientTrajectories)
            {
                var traj = kvp.Value;
                var player = capi.World.PlayerByUid(traj.PlayerUid);
                if (player?.Entity == null || !player.Entity.Alive)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                if (traj.IsSuspended)
                {
                    double vTime = (player.Entity.World.ElapsedMilliseconds / 1000.0) * 45.0;
                    double vibX = Math.Sin(vTime * 1.5) * 0.045 + Math.Cos(vTime * 2.7) * 0.025;
                    double vibY = Math.Cos(vTime * 1.8) * 0.04 + Math.Sin(vTime * 2.2) * 0.02;
                    double vibZ = Math.Sin(vTime * 1.9) * 0.045 + Math.Cos(vTime * 1.3) * 0.025;
                    player.Entity.Pos.SetPos(traj.SuspendedPos.AddCopy(vibX, vibY, vibZ));
                    player.Entity.Pos.Motion.Set(0, 0, 0);
                    player.Entity.Controls.Gliding = true;
                    player.Entity.Controls.IsFlying = true;
                    player.Entity.ServerControls.Gliding = true;
                    player.Entity.ServerControls.IsFlying = true;
                    ApplyDunkStyleRotation(player.Entity, traj);
                    continue;
                }

                Vec3d targetPos = traj.TargetPos;
                if (!string.IsNullOrEmpty(traj.TargetPlayerUid))
                {
                    var targetPlayer = capi.World.PlayerByUid(traj.TargetPlayerUid);
                    if (targetPlayer?.Entity != null)
                    {
                        targetPos = targetPlayer.Entity.Pos.XYZ;
                        double diffX = targetPos.X - player.Entity.Pos.X;
                        double diffZ = targetPos.Z - player.Entity.Pos.Z;
                        if (diffX * diffX + diffZ * diffZ > 0.01)
                        {
                            traj.FlightYaw = (float)Math.Atan2(diffX, diffZ);
                        }
                    }
                }

                double elapsedMs = capi.World.ElapsedMilliseconds - traj.StartLocalMs;
                float t = Math.Clamp((float)(elapsedMs / (Math.Max(traj.DurationSeconds, 0.1f) * 1000.0)), 0f, 1f);

                double curX = traj.StartPos.X + (targetPos.X - traj.StartPos.X) * t;
                double curZ = traj.StartPos.Z + (targetPos.Z - traj.StartPos.Z) * t;
                double baseY = traj.StartPos.Y + (targetPos.Y - traj.StartPos.Y) * t;
                double arcY = traj.ArcHeight * 4.0 * t * (1.0 - t);
                double curY = baseY + arcY;

                // Pivot compensation for front flip: pivot around the center of the body (1.30m up from feet)
                if (traj.DunkStyle == (int)EnumDunkStyle.FrontFlip)
                {
                    float pitch = (float)(t * GameMath.TWOPI * traj.Revolutions);
                    curY += 1.30 * (1.0 - Math.Cos(pitch));
                    curX -= 1.30 * Math.Sin(pitch) * Math.Sin(traj.FlightYaw);
                    curZ -= 1.30 * Math.Sin(pitch) * Math.Cos(traj.FlightYaw);
                }

                player.Entity.Pos.SetPos(curX, curY, curZ);
                player.Entity.Pos.Motion.Set(0, 0, 0);
                player.Entity.Controls.Gliding = true;
                player.Entity.Controls.IsFlying = true;
                player.Entity.ServerControls.Gliding = true;
                player.Entity.ServerControls.IsFlying = true;
                ApplyDunkStyleRotation(player.Entity, traj);

                if (t >= 1.0f)
                {
                    toRemove.Add(kvp.Key);
                    player.Entity.WalkPitch = 0f;
                    player.Entity.Pos.Roll = 0f;
                    player.Entity.Controls.Gliding = false;
                    player.Entity.Controls.IsFlying = player.WorldData?.FreeMove ?? false;
                    player.Entity.ServerControls.Gliding = false;
                    player.Entity.ServerControls.IsFlying = player.WorldData?.FreeMove ?? false;
                }
            }

            foreach (var k in toRemove)
            {
                clientTrajectories.Remove(k);
            }
        }

        private void OnClientJumpTick(float dt)
        {
            if (api is not ICoreClientAPI capi) return;
            var player = capi.World.Player;
            if (player?.Entity == null) return;

            // Cannot jump or initiate a new dunk while already airborne in a trajectory
            if (IsPlayerInTrajectory(player.PlayerUID, out _))
            {
                ClientIsChargingJump = false;
                ClientJumpCharge = 0f;
                wasSpaceHeld = false;
                SuppressJumpUntilRelease = false;
                ClientLockedHoopPos = null;
                ClientLockedDunkerUid = "";
                return;
            }

            bool holdingBall = BasketballGameState.IsHoldingBall(player.Entity);
            bool isBallNearby = BasketballGameState.IsAnyPlayerHoldingBallNearby(api, player.Entity, 30.0);

            // Scanning targets
            if (holdingBall)
            {
                ClientLockedHoopPos = ScanForTargetHoop(capi, player.Entity);
                ClientLockedDunkerUid = "";
            }
            else
            {
                ClientLockedHoopPos = null;
                ClientLockedDunkerUid = ScanForAirborneDunker(capi, player.Entity);
            }

            // Detect spacebar state (only allow charging while planted on the ground)
            bool spaceDown = capi.Input.IsHotKeyPressed("jump") ||
                             (capi.Input.KeyboardKeyStateRaw != null && (int)GlKeys.Space < capi.Input.KeyboardKeyStateRaw.Length && capi.Input.KeyboardKeyStateRaw[(int)GlKeys.Space]);

            if (spaceDown)
            {
                if (player.Entity.OnGround && (holdingBall || isBallNearby || !string.IsNullOrEmpty(ClientLockedDunkerUid)))
                {
                    if (!SuppressJumpUntilRelease)
                    {
                        // Fresh jump charge initiated on the ground
                        SuppressJumpUntilRelease = true;
                        ClientIsChargingJump = true;
                        ClientJumpCharge = 0f;
                    }

                    if (ClientIsChargingJump)
                    {
                        // Counter dunk parries take half the time to initiate than a dunk!
                        float chargeRate = !string.IsNullOrEmpty(ClientLockedDunkerUid) ? 1.6f : 0.8f;
                        ClientJumpCharge = Math.Min(ClientJumpCharge + dt * chargeRate, 1.0f);
                    }
                    wasSpaceHeld = true;

                    // Suppress vanilla immediate jump while charging so player stays grounded to build power
                    if (player.Entity.Pos.Motion.Y > 0)
                    {
                        player.Entity.Pos.Motion.Y = 0;
                    }
                }
                else if (!player.Entity.OnGround)
                {
                    // Player stepped or fell off a block while holding spacebar:
                    // Retain accumulated charge and keep jump suppressed so player doesn't jump in mid-air
                }

                if (SuppressJumpUntilRelease)
                {
                    player.Entity.Controls.Jump = false;
                }
            }
            else
            {
                // Spacebar physically released: only fire if we were charging while grounded
                if (wasSpaceHeld && ClientIsChargingJump && player.Entity.OnGround)
                {
                    float minChargeThreshold = !string.IsNullOrEmpty(ClientLockedDunkerUid) ? 0.25f : 0.50f;
                    if (ClientJumpCharge >= minChargeThreshold)
                    {
                        // Spacebar released with required charge: execute slam dunk, counter dunk parry, or super jump!
                        ExecuteClientJump(capi, player, ClientJumpCharge);
                    }
                    else
                    {
                        // Released before reaching threshold: perform standard player jump instead of doing nothing!
                        double jumpMultiplier = Math.Sqrt(Math.Max(1.0, player.Entity.Stats.GetBlended("jumpHeightMul")));
                        player.Entity.Pos.Motion.Y = 0.145 * jumpMultiplier;
                        player.Entity.PlayEntitySound("jump", player);
                    }
                }

                ClientIsChargingJump = false;
                ClientJumpCharge = 0f;
                wasSpaceHeld = false;
                SuppressJumpUntilRelease = false;
            }
        }

        private void ExecuteClientJump(ICoreClientAPI capi, IClientPlayer player, float charge)
        {
            var channel = capi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);

            if (ClientLockedHoopPos != null)
            {
                int dunkStyle = rand.Next(0, 3);
                int revolutions = rand.Next(1, 3);

                // Request and predict Slam Dunk
                channel?.SendPacket(new DunkStartRequestMessage
                {
                    TargetHoopPos = ClientLockedHoopPos,
                    ChargeAmount = charge,
                    DunkStyle = dunkStyle,
                    Revolutions = revolutions
                });

                Block block = capi.World.BlockAccessor.GetBlock(ClientLockedHoopPos);
                if (block is BlockHoop hoopBlock)
                {
                    var beHoop = capi.World.BlockAccessor.GetBlockEntity(ClientLockedHoopPos) as BlockEntityHoop;
                    if (beHoop != null && !beHoop.IsDunkable) return;
                    Vec3d rimCenter = hoopBlock.GetRimCenter(ClientLockedHoopPos);
                    Vec3d startPos = player.Entity.Pos.XYZ.Clone();

                    // Approach vector from rim center towards player: arcs directly towards the side closest to the player
                    double dxFromRim = startPos.X - rimCenter.X;
                    double dzFromRim = startPos.Z - rimCenter.Z;
                    double horizDistFromRim = Math.Sqrt(dxFromRim * dxFromRim + dzFromRim * dzFromRim);
                    Vec3d approachDir = horizDistFromRim > 0.001 
                        ? new Vec3d(dxFromRim / horizDistFromRim, 0, dzFromRim / horizDistFromRim) 
                        : new Vec3d(0, 0, 1);

                    // Player body destination: on the exact approach angle closest to player (0.70m from rim)
                    Vec3d playerTargetPos = rimCenter.AddCopy(approachDir.X * 0.70, -0.30, approachDir.Z * 0.70);

                    // Flight yaw points directly into the rim center from takeoff
                    float flightYaw = (float)Math.Atan2(rimCenter.X - startPos.X, rimCenter.Z - startPos.Z);

                    double distance = startPos.DistanceTo(rimCenter);
                    float duration = (float)Math.Clamp(distance / 6.0, 0.9, 2.5);
                    float arcHeight = (float)Math.Clamp(distance * 0.38 + 2.5, 3.0, 7.5);

                    clientTrajectories[player.PlayerUID] = new ActiveTrajectory
                    {
                        PlayerUid = player.PlayerUID,
                        StartPos = startPos,
                        TargetPos = playerTargetPos,
                        ArcHeight = arcHeight,
                        DurationSeconds = duration,
                        StartLocalMs = capi.World.ElapsedMilliseconds,
                        IsDunk = true,
                        TargetHoopPos = ClientLockedHoopPos,
                        DunkStyle = dunkStyle,
                        Revolutions = revolutions,
                        FlightYaw = flightYaw
                    };
                }
            }
            else if (!string.IsNullOrEmpty(ClientLockedDunkerUid))
            {
                // Verify the target is actually performing a slam dunk
                if (!clientTrajectories.TryGetValue(ClientLockedDunkerUid, out var dunkerTraj) || !dunkerTraj.IsDunk)
                {
                    return;
                }

                // Request and predict Interception
                channel?.SendPacket(new InterceptStartRequestMessage
                {
                    TargetPlayerUid = ClientLockedDunkerUid,
                    ChargeAmount = charge
                });

                IPlayer? targetDunker = capi.World.PlayerByUid(ClientLockedDunkerUid);
                if (targetDunker?.Entity != null)
                {
                    Vec3d startPos = player.Entity.Pos.XYZ.Clone();
                    Vec3d targetPos = targetDunker.Entity.Pos.XYZ.Clone();

                    double dx = targetPos.X - startPos.X;
                    double dz = targetPos.Z - startPos.Z;
                    float flightYaw = (float)Math.Atan2(dx, dz);
                    double distance = startPos.DistanceTo(targetPos);
                    float duration = (float)Math.Clamp(distance / 12.0, 0.45, 1.25);
                    float arcHeight = (float)Math.Clamp(distance * 0.30 + 1.5, 2.0, 5.5);

                    clientTrajectories[player.PlayerUID] = new ActiveTrajectory
                    {
                        PlayerUid = player.PlayerUID,
                        TargetPlayerUid = ClientLockedDunkerUid,
                        StartPos = startPos,
                        TargetPos = targetPos,
                        ArcHeight = arcHeight,
                        DurationSeconds = duration,
                        StartLocalMs = capi.World.ElapsedMilliseconds,
                        IsDunk = false,
                        DunkStyle = (int)EnumDunkStyle.FrontFlip,
                        Revolutions = 2,
                        FlightYaw = flightYaw
                    };
                }
            }
            else
            {
                // Calibrated Super Jump (4-5 blocks vertical boost, crisp and controlled)
                float jumpPower = 0.15f + charge * 0.20f;
                player.Entity.Pos.Motion.Y = jumpPower;

                Vec3f lookVecF = player.Entity.Pos.GetViewVector().Normalize();
                player.Entity.Pos.Motion.X += lookVecF.X * (0.05f + charge * 0.10f);
                player.Entity.Pos.Motion.Z += lookVecF.Z * (0.05f + charge * 0.10f);

                BasketballAudioParticles.PlayThrowSound(capi.World, player.Entity.Pos.XYZ);
                BasketballAudioParticles.SpawnBounceParticles(capi.World, player.Entity.Pos.XYZ);
            }
        }

        private BlockPos? ScanForTargetHoop(ICoreClientAPI capi, EntityPlayer player)
        {
            Vec3d eyePos = player.CameraPos;
            Vec3f lookVecF = player.Pos.GetViewVector().Normalize();
            Vec3d lookVec = new Vec3d(lookVecF.X, lookVecF.Y, lookVecF.Z);

            BlockPos playerBlockPos = player.Pos.AsBlockPos;
            int radius = 18;

            BlockPos? bestHoop = null;
            double bestScore = -1.0;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -4; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        BlockPos checkPos = playerBlockPos.AddCopy(x, y, z);
                        Block block = capi.World.BlockAccessor.GetBlock(checkPos);
                        if (block is BlockHoop hoopBlock)
                        {
                            var beHoop = capi.World.BlockAccessor.GetBlockEntity(checkPos) as BlockEntityHoop;
                            if (beHoop != null && !beHoop.IsDunkable) continue;

                            Vec3d rimCenter = hoopBlock.GetRimCenter(checkPos);
                            Vec3d toHoop = rimCenter.SubCopy(eyePos);
                            double dist = toHoop.Length();
                            if (dist > 1.8 && dist <= 20.0)
                            {
                                Vec3d dirToHoop = toHoop.Normalize();
                                double dot = lookVec.Dot(dirToHoop);
                                if (dot > 0.78 && dot > bestScore)
                                {
                                    bestScore = dot;
                                    bestHoop = checkPos;
                                }
                            }
                        }
                    }
                }
            }

            return bestHoop;
        }

        private string ScanForAirborneDunker(ICoreClientAPI capi, EntityPlayer player)
        {
            Vec3d eyePos = player.CameraPos;
            Vec3f lookVecF = player.Pos.GetViewVector().Normalize();
            Vec3d lookVec = new Vec3d(lookVecF.X, lookVecF.Y, lookVecF.Z);

            string bestDunker = "";
            double bestScore = -1.0;

            foreach (var otherPlayer in capi.World.AllPlayers)
            {
                if (otherPlayer.PlayerUID == player.PlayerUID || otherPlayer.Entity == null) continue;

                // Only lock onto players who are actively performing a slam dunk arc
                bool isAirborneDunker = clientTrajectories.TryGetValue(otherPlayer.PlayerUID, out var traj) && traj.IsDunk && !traj.IsSuspended;

                if (isAirborneDunker)
                {
                    Vec3d toTarget = otherPlayer.Entity.Pos.XYZ.SubCopy(eyePos);
                    double dist = toTarget.Length();
                    if (dist > 1.0 && dist <= 30.0)
                    {
                        double dot = lookVec.Dot(toTarget.Normalize());
                        if (dot > 0.60 && dot > bestScore)
                        {
                            bestScore = dot;
                            bestDunker = otherPlayer.PlayerUID;
                        }
                    }
                }
            }

            return bestDunker;
        }
    }
}
