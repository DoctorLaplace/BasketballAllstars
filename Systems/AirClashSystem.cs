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

        // ========================================================================
        // Server Clash Detection & State Machine
        // ========================================================================

        public void CheckAirClashes(IServerPlayer dunker, double x, double y, double z)
        {
            if (api is not ICoreServerAPI sapi) return;

            var dunkerPos = new Vec3d(x, y, z);
            foreach (IServerPlayer otherPlayer in sapi.World.AllOnlinePlayers)
            {
                if (otherPlayer.PlayerUID == dunker.PlayerUID || otherPlayer.Entity == null) continue;

                var otherTraj = DunkTrajectorySystem.Instance?.GetActiveTrajectory(otherPlayer.PlayerUID);
                if (otherTraj != null && !otherTraj.IsDunk && !otherTraj.IsSuspended)
                {
                    double dist = otherPlayer.Entity.Pos.XYZ.DistanceTo(dunkerPos);
                    if (dist < 2.2)
                    {
                        // Trigger Air Clash Duel!
                        StartAirClash(dunker, otherPlayer, dunkerPos);
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

            // Generate 10 random arrow keys (0: Up, 1: Right, 2: Down, 3: Left)
            byte[] sequence = new byte[10];
            for (int i = 0; i < 10; i++)
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
            BasketballAudioParticles.PlayClashSound(api.World, clashPos);
            BasketballAudioParticles.SpawnClashSparks(api.World, clashPos);

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
            if (isDunker)
            {
                duel.DunkerProgress = Math.Min(completedInputs, 10);
            }
            else if (player.PlayerUID == duel.InterceptorUid)
            {
                duel.InterceptorProgress = Math.Min(completedInputs, 10);
            }

            // Sync progress
            var serverChannel = (api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            serverChannel?.BroadcastPacket(new AirClashDuelProgressSyncMessage
            {
                DuelId = duelId,
                DunkerProgress = duel.DunkerProgress,
                InterceptorProgress = duel.InterceptorProgress
            });

            // Check if someone reached 10 inputs
            if (duel.DunkerProgress >= 10)
            {
                ResolveDuel(duel, winnerIsDunker: true);
            }
            else if (duel.InterceptorProgress >= 10)
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

            if (winnerIsDunker)
            {
                // Dunker won the clash! Deflect interceptor and resume dunk
                BasketballAudioParticles.PlayClashSound(api.World, duel.ClashPos);
                BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos);

                if (interceptor?.Entity != null)
                {
                    DunkTrajectorySystem.Instance?.CancelTrajectory(duel.InterceptorUid);
                    // Knockback interceptor downwards and away
                    interceptor.Entity.Pos.Motion.Set((rand.NextDouble() - 0.5) * 0.8, -0.65, (rand.NextDouble() - 0.5) * 0.8);
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
                // Interceptor won the clash! Steal ball and spark explode dunker upward
                BasketballAudioParticles.PlayClashSound(api.World, duel.ClashPos);
                BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos);
                BasketballAudioParticles.SpawnClashSparks(api.World, duel.ClashPos.AddCopy(0, 0.5, 0));

                // Transfer ball to interceptor
                if (dunker != null && interceptor != null)
                {
                    BasketballGameState.Instance?.TransferBall(dunker, interceptor);
                }

                // Cancel both airborne trajectories
                DunkTrajectorySystem.Instance?.CancelTrajectory(duel.DunkerUid);
                DunkTrajectorySystem.Instance?.CancelTrajectory(duel.InterceptorUid);

                // Defending player lands cleanly; dunking player is spark exploded and thrown upward!
                if (dunker?.Entity != null)
                {
                    dunker.Entity.Pos.Motion.Set((rand.NextDouble() - 0.5) * 0.3, 0.85, (rand.NextDouble() - 0.5) * 0.3);
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
        }

        // ========================================================================
        // Client Handling
        // ========================================================================

        public void OnClientStartClash(AirClashStartMessage msg)
        {
            if (api is not ICoreClientAPI capi) return;
            DunkTrajectorySystem.ClientInstance?.SuspendTrajectory(msg.DunkerUid, msg.ClashPos);
            DunkTrajectorySystem.ClientInstance?.SuspendTrajectory(msg.InterceptorUid, msg.ClashPos.AddCopy(0.3, 0, 0.3));
            GuiDialogAirClashQTE.OpenDuel(capi, msg);
        }

        public void OnClientClashProgress(AirClashDuelProgressSyncMessage msg)
        {
            GuiDialogAirClashQTE.Instance?.UpdateProgress(msg.DunkerProgress, msg.InterceptorProgress);
        }

        public void OnClientClashResult(AirClashResultMessage msg)
        {
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
