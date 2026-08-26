using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Gui;
using BasketballAllstars.Items;
using BasketballAllstars.Network;

namespace BasketballAllstars.Systems
{
    public class ActiveAirClashDuel
    {
        public int DuelId { get; set; }
        public string DunkerUid { get; set; } = "";
        public string InterceptorUid { get; set; } = "";
        public Vec3d ClashPos { get; set; } = new Vec3d();
        public byte[] QTESequence { get; set; } = Array.Empty<byte>();
        public int DunkerProgress { get; set; } = 0;
        public int InterceptorProgress { get; set; } = 0;
        public double StartTimeMs { get; set; } = 0;
        public bool IsFinished { get; set; } = false;
    }

    public class AirClashSystem
    {
        public static AirClashSystem? ServerInstance { get; private set; }
        public static AirClashSystem? ClientInstance { get; private set; }
        public static AirClashSystem? Instance => ClientInstance ?? ServerInstance;

        private readonly ICoreAPI api;
        private readonly Dictionary<int, ActiveAirClashDuel> activeDuels = new();
        private int nextDuelId = 1;
        private static readonly Random rand = new Random();

        public AirClashSystem(ICoreAPI api)
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

        public bool IsPlayerInDuel(string playerUid)
        {
            foreach (var duel in activeDuels.Values)
            {
                if (!duel.IsFinished && (duel.DunkerUid == playerUid || duel.InterceptorUid == playerUid))
                {
                    return true;
                }
            }
            return false;
        }

        // ========================================================================
        // Server Clash Detection & State Machine
        // ========================================================================

        public void CheckAirClashes(IServerPlayer triggeringPlayer, double x, double y, double z)
        {
            if (api is not ICoreServerAPI sapi) return;

            var myTraj = DunkTrajectorySystem.Instance?.GetActiveTrajectory(triggeringPlayer.PlayerUID);
            if (myTraj == null || myTraj.IsSuspended) return;

            var playerPos = new Vec3d(x, y, z);
            foreach (IServerPlayer otherPlayer in sapi.World.AllOnlinePlayers)
            {
                if (otherPlayer.PlayerUID == triggeringPlayer.PlayerUID || otherPlayer.Entity == null) continue;

                var otherTraj = DunkTrajectorySystem.Instance?.GetActiveTrajectory(otherPlayer.PlayerUID);
                if (otherTraj == null || otherTraj.IsSuspended) continue;

                // One must be the dunker, the other the interceptor
                if (myTraj.IsDunk && !otherTraj.IsDunk)
                {
                    double dist = otherPlayer.Entity.Pos.XYZ.DistanceTo(playerPos);
                    if (dist < 2.5)
                    {
                        StartAirClash(triggeringPlayer, otherPlayer, playerPos);
                        break;
                    }
                }
                else if (!myTraj.IsDunk && otherTraj.IsDunk)
                {
                    double dist = otherPlayer.Entity.Pos.XYZ.DistanceTo(playerPos);
                    if (dist < 2.5)
                    {
                        StartAirClash(otherPlayer, triggeringPlayer, playerPos);
                        break;
                    }
                }
            }
        }

        private void StartAirClash(IServerPlayer dunker, IServerPlayer interceptor, Vec3d clashPos)
        {
            int duelId = nextDuelId++;

            // Suspend both airborne trajectories
            DunkTrajectorySystem.Instance?.SuspendTrajectory(dunker.PlayerUID, clashPos);
            DunkTrajectorySystem.Instance?.SuspendTrajectory(interceptor.PlayerUID, clashPos.AddCopy(0.3, 0, 0.3));

            // Generate 5 random arrow keys (0: Up, 1: Right, 2: Down, 3: Left)
            byte[] sequence = new byte[5];
            for (int i = 0; i < sequence.Length; i++)
            {
                sequence[i] = (byte)rand.Next(0, 4);
            }

            var duel = new ActiveAirClashDuel
            {
                DuelId = duelId,
                DunkerUid = dunker.PlayerUID,
                InterceptorUid = interceptor.PlayerUID,
                ClashPos = clashPos,
                QTESequence = sequence,
                StartTimeMs = api.World.ElapsedMilliseconds
            };

            activeDuels[duelId] = duel;

            // Audio & Spark Effects
            BasketballAudioParticles.PlayClashStartSounds(api.World, clashPos);
            BasketballAudioParticles.SpawnClashSparks(api.World, clashPos);

            // Steal immunity during active clash
            BasketballGameState.ServerInstance?.SetPlayerImmunity(dunker.PlayerUID, interceptor.PlayerUID, 15000.0);

            // Broadcast duel start to both players
            var serverChannel = (api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            var startMsg = new AirClashStartMessage
            {
                DuelId = duelId,
                DunkerUid = dunker.PlayerUID,
                InterceptorUid = interceptor.PlayerUID,
                QTESequence = sequence,
                ClashPos = clashPos
            };

            serverChannel?.SendPacket(startMsg, dunker);
            serverChannel?.SendPacket(startMsg, interceptor);
        }

        public void HandleClientInputProgress(IServerPlayer player, int duelId, int completedInputs)
        {
            if (!activeDuels.TryGetValue(duelId, out var duel) || duel.IsFinished) return;

            bool isDunker = player.PlayerUID == duel.DunkerUid;
            int targetCount = duel.QTESequence?.Length ?? 5;

            if (completedInputs < 0)
            {
                // Player made a mistake: immediate defeat!
                ResolveDuel(duel, winnerIsDunker: !isDunker);
                return;
            }

            // Play parry hit sound and spawn spark burst audible to all nearby players
            BasketballAudioParticles.PlayParryHitSound(api.World, duel.ClashPos);
            BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos);

            if (isDunker)
            {
                duel.DunkerProgress = Math.Min(completedInputs, targetCount);
            }
            else if (player.PlayerUID == duel.InterceptorUid)
            {
                duel.InterceptorProgress = Math.Min(completedInputs, targetCount);
            }

            // Sync progress
            var serverChannel = (api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            serverChannel?.BroadcastPacket(new AirClashDuelProgressSyncMessage
            {
                DuelId = duelId,
                DunkerProgress = duel.DunkerProgress,
                InterceptorProgress = duel.InterceptorProgress
            });

            // Check if someone reached target inputs
            if (duel.DunkerProgress >= targetCount)
            {
                ResolveDuel(duel, winnerIsDunker: true);
            }
            else if (duel.InterceptorProgress >= targetCount)
            {
                ResolveDuel(duel, winnerIsDunker: false);
            }
        }

        private void ResolveDuel(ActiveAirClashDuel duel, bool winnerIsDunker)
        {
            if (duel.IsFinished) return;
            duel.IsFinished = true;
            activeDuels.Remove(duel.DuelId);

            if (api is not ICoreServerAPI sapi) return;

            IServerPlayer? dunker = sapi.World.PlayerByUid(duel.DunkerUid) as IServerPlayer;
            IServerPlayer? interceptor = sapi.World.PlayerByUid(duel.InterceptorUid) as IServerPlayer;

            var serverChannel = sapi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);

            // Fetch active trajectories before cancellation to obtain approach vectors
            var dunkerTraj = DunkTrajectorySystem.Instance?.GetActiveTrajectory(duel.DunkerUid);
            var interceptorTraj = DunkTrajectorySystem.Instance?.GetActiveTrajectory(duel.InterceptorUid);

            // Determine approach direction of the interceptor (from interceptor start position towards clash position)
            Vec3d interceptorOriginDir = new Vec3d(0, 0, 1);
            if (interceptorTraj != null)
            {
                double dX = interceptorTraj.StartPos.X - duel.ClashPos.X;
                double dZ = interceptorTraj.StartPos.Z - duel.ClashPos.Z;
                double len = Math.Sqrt(dX * dX + dZ * dZ);
                if (len > 0.05)
                {
                    interceptorOriginDir = new Vec3d(dX / len, 0, dZ / len);
                }
            }
            else if (dunkerTraj != null)
            {
                double dX = dunkerTraj.StartPos.X - duel.ClashPos.X;
                double dZ = dunkerTraj.StartPos.Z - duel.ClashPos.Z;
                double len = Math.Sqrt(dX * dX + dZ * dZ);
                if (len > 0.05)
                {
                    interceptorOriginDir = new Vec3d(dX / len, 0, dZ / len);
                }
            }

            // Play random parry defeat sound and explosive sparks at clash position
            BasketballAudioParticles.PlayParryDefeatSound(api.World, duel.ClashPos);
            BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos);

            if (winnerIsDunker)
            {
                // Dunker won the clash! Deflect interceptor back in the opposite angle they came from and resume dunk
                DunkTrajectorySystem.Instance?.CancelTrajectory(duel.InterceptorUid);

                if (interceptor?.Entity != null)
                {
                    // Thrown back in the opposite angle of their incoming flight (recoil towards their origin)
                    interceptor.Entity.Pos.Motion.Set(interceptorOriginDir.X * 0.85, 0.40, interceptorOriginDir.Z * 0.85);
                }

                // Resume dunker trajectory to finish the slam
                DunkTrajectorySystem.Instance?.ResumeTrajectory(duel.DunkerUid);

                serverChannel?.BroadcastPacket(new AirClashResultMessage
                {
                    DuelId = duel.DuelId,
                    WinnerUid = duel.DunkerUid,
                    LoserUid = duel.InterceptorUid,
                    DunkerWon = true,
                    ClashPos = duel.ClashPos
                });
            }
            else
            {
                // Interceptor won the clash! Steal ball and throw dunker in the direction interceptor came from
                BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos.AddCopy(0, 0.5, 0));

                // Transfer ball to interceptor
                if (dunker != null && interceptor != null)
                {
                    BasketballGameState.Instance?.TransferBall(dunker, interceptor);
                }

                // Cancel both airborne trajectories
                DunkTrajectorySystem.Instance?.CancelTrajectory(duel.DunkerUid);
                DunkTrajectorySystem.Instance?.CancelTrajectory(duel.InterceptorUid);

                // Dunking player is spark exploded and thrown in the direction the intercepting player was coming from
                if (dunker?.Entity != null)
                {
                    dunker.Entity.Pos.Motion.Set(interceptorOriginDir.X * 0.95, 0.75, interceptorOriginDir.Z * 0.95);
                }
                if (interceptor?.Entity != null)
                {
                    interceptor.Entity.Pos.Motion.Set(0, -0.15, 0);
                }

                serverChannel?.BroadcastPacket(new AirClashResultMessage
                {
                    DuelId = duel.DuelId,
                    WinnerUid = duel.InterceptorUid,
                    LoserUid = duel.DunkerUid,
                    DunkerWon = false,
                    ClashPos = duel.ClashPos
                });
            }

            // Apply 1 second of lingering steal immunity between the two clashing players
            BasketballGameState.ServerInstance?.SetPlayerImmunity(duel.DunkerUid, duel.InterceptorUid, 1000.0);
        }

        // ========================================================================
        // Client Handling
        // ========================================================================

        public void OnClientStartClash(AirClashStartMessage msg)
        {
            if (api is not ICoreClientAPI capi) return;
            DunkTrajectorySystem.ClientInstance?.SuspendTrajectory(msg.DunkerUid, msg.ClashPos);
            DunkTrajectorySystem.ClientInstance?.SuspendTrajectory(msg.InterceptorUid, msg.ClashPos.AddCopy(0.3, 0, 0.3));

            // Start looping rumble, electrical clash, and latent energy sounds
            BasketballAudioParticles.StartClashLoopingSounds(capi, msg.ClashPos);

            // Only open QTE GUI if local player is one of the duel participants
            if (capi.World.Player?.PlayerUID == msg.DunkerUid || capi.World.Player?.PlayerUID == msg.InterceptorUid)
            {
                GuiDialogAirClashQTE.OpenDuel(capi, msg);
            }
        }

        public void OnClientClashProgress(AirClashDuelProgressSyncMessage msg)
        {
            GuiDialogAirClashQTE.Instance?.UpdateProgress(msg.DunkerProgress, msg.InterceptorProgress);
        }

        public void OnClientClashResult(AirClashResultMessage msg)
        {
            // Stop clash looping sounds immediately when clash ends
            BasketballAudioParticles.StopClashLoopingSounds();

            if (msg.DunkerWon)
            {
                DunkTrajectorySystem.ClientInstance?.CancelTrajectory(msg.LoserUid);
                DunkTrajectorySystem.ClientInstance?.ResumeTrajectory(msg.WinnerUid);
            }
            else
            {
                DunkTrajectorySystem.ClientInstance?.CancelTrajectory(msg.WinnerUid);
                DunkTrajectorySystem.ClientInstance?.CancelTrajectory(msg.LoserUid);
            }
            GuiDialogAirClashQTE.Instance?.ShowResult(msg.DunkerWon);
        }
    }
}
