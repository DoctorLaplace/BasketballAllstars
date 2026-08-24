using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Items;
using BasketballAllstars.Network;

namespace BasketballAllstars.Systems
{
    public class BasketballGameState
    {
        public static BasketballGameState? ServerInstance { get; private set; }
        public static BasketballGameState? ClientInstance { get; private set; }
        public static BasketballGameState? Instance => ClientInstance ?? ServerInstance;

        private readonly ICoreAPI api;
        private readonly Dictionary<string, double> stealImmunityTimers = new();
        private double lastDribbleTickMs = 0;

        public BasketballGameState(ICoreAPI api)
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
                (api as ICoreServerAPI)?.Event.RegisterGameTickListener(OnServerStateTick, 20);
            }
            else if (api.Side == EnumAppSide.Client)
            {
                (api as ICoreClientAPI)?.Event.RegisterGameTickListener(OnClientStateTick, 20);
            }
        }

        private void OnServerStateTick(float dt)
        {
            if (api is not ICoreServerAPI sapi) return;

            double nowMs = sapi.World.ElapsedMilliseconds;

            // Check steals and dribbling across all players
            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (player.Entity == null || !player.Entity.Alive || player.InventoryManager == null) continue;

                try
                {
                    bool hasBall = IsHoldingBall(player.Entity);
                    if (hasBall)
                    {
                        ItemBasketball.ApplyCarrierBuffs(player.Entity);

                        // Check if another player can steal the ball
                        CheckGroundSteals(sapi, player, nowMs);
                    }
                    else
                    {
                        if (!player.Entity.WatchedAttributes.GetBool("basketballFallImmunity", false))
                        {
                            ItemBasketball.RemoveCarrierBuffs(player.Entity);
                        }

                        // Check if player can steal from a nearby dummy
                        CheckDummySteals(sapi, player, nowMs);
                    }
                }
                catch
                {
                    // Ignore transient exceptions during player load/connect
                }
            }

            // Periodic dribble audio & particles for carriers on ground (no dribble in mid-air)
            if (nowMs - lastDribbleTickMs > 380)
            {
                lastDribbleTickMs = nowMs;
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                {
                    if (player.Entity != null && player.Entity.Alive && player.InventoryManager != null && IsHoldingBall(player.Entity) && player.Entity.OnGround)
                    {
                        Vec3d feetPos = player.Entity.Pos.XYZ.AddCopy(0, 0.05, 0);
                        BasketballAudioParticles.PlayDribbleSound(sapi.World, feetPos);
                        BasketballAudioParticles.SpawnDribbleParticles(sapi.World, feetPos);
                    }
                }
            }
        }

        private void CheckGroundSteals(ICoreServerAPI sapi, IServerPlayer possessor, double nowMs)
        {
            Vec3d posPos = possessor.Entity.Pos.XYZ;
            Vec3f posLookF = possessor.Entity.Pos.GetViewVector().Normalize();
            Vec3d posLook = new Vec3d(posLookF.X, posLookF.Y, posLookF.Z);

            foreach (IServerPlayer candidate in sapi.World.AllOnlinePlayers)
            {
                if (candidate.PlayerUID == possessor.PlayerUID || candidate.Entity == null || !candidate.Entity.Alive || candidate.InventoryManager == null) continue;

                // Check distance (< 1.45m)
                Vec3d candPos = candidate.Entity.Pos.XYZ;
                double dist = posPos.DistanceTo(candPos);
                if (dist > 1.45) continue;

                // Check immunity cooldown
                string immunityKey = $"{candidate.PlayerUID}_{possessor.PlayerUID}";
                if (stealImmunityTimers.TryGetValue(immunityKey, out double expiry) && nowMs < expiry)
                {
                    continue;
                }

                // Check front-facing steal geometry
                Vec3d toCand = candPos.SubCopy(posPos).Normalize();
                double dotPossessorToCand = posLook.Dot(toCand);
                Vec3f candLookF = candidate.Entity.Pos.GetViewVector().Normalize();
                Vec3d candLook = new Vec3d(candLookF.X, candLookF.Y, candLookF.Z);
                double dotCandToPossessor = candLook.Dot(toCand.Clone().Mul(-1));

                // Defender is in front of possessor and facing possessor
                if (dotPossessorToCand > 0.50 && dotCandToPossessor > 0.40)
                {
                    // STEAL!
                    TransferBall(possessor, candidate);

                    // Apply 1.0 second immunity against original possessor
                    stealImmunityTimers[immunityKey] = nowMs + 1000.0;

                    // Play steal effects
                    BasketballAudioParticles.PlayStealSound(sapi.World, candPos);
                    BasketballAudioParticles.SpawnBounceParticles(sapi.World, candPos);

                    // Broadcast steal notification
                    var serverChannel = sapi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                    serverChannel?.BroadcastPacket(new BallStealEventMessage
                    {
                        StealerUid = candidate.PlayerUID,
                        VictimUid = possessor.PlayerUID
                    });
                    break;
                }
            }
        }

        private void CheckDummySteals(ICoreServerAPI sapi, IServerPlayer stealer, double nowMs)
        {
            if (stealer.Entity == null || !stealer.Entity.Alive || stealer.InventoryManager == null) return;

            Vec3d playerPos = stealer.Entity.Pos.XYZ;
            Vec3f playerLookF = stealer.Entity.Pos.GetViewVector().Normalize();
            Vec3d playerLook = new Vec3d(playerLookF.X, playerLookF.Y, playerLookF.Z);

            Entity? nearestEntity = sapi.World.GetNearestEntity(playerPos, 1.45f, 1.8f, e => e is Entities.EntityBasketballDummy dummy && dummy.Alive && dummy.HasBall);
            if (nearestEntity is Entities.EntityBasketballDummy targetDummy)
            {
                // Check immunity
                string immunityKey = $"{stealer.PlayerUID}_dummy_{targetDummy.EntityId}";
                if (stealImmunityTimers.TryGetValue(immunityKey, out double expiry) && nowMs < expiry)
                {
                    return;
                }

                // Check facing direction towards dummy
                Vec3d toDummy = targetDummy.Pos.XYZ.SubCopy(playerPos).Normalize();
                double dot = playerLook.Dot(toDummy);

                if (dot > 0.40)
                {
                    // STEAL FROM DUMMY!
                    targetDummy.HasBall = false;

                    Item ballItem = sapi.World.GetItem(new AssetLocation("basketballallstars:basketball"));
                    if (ballItem != null)
                    {
                        ItemStack stack = new ItemStack(ballItem, 1);
                        if (!stealer.InventoryManager.TryGiveItemstack(stack, true))
                        {
                            sapi.World.SpawnItemEntity(stack, playerPos.AddCopy(0, 0.5, 0));
                        }
                    }

                    // Apply immunity cooldown (1.5s)
                    SetStealImmunity(stealer.PlayerUID, targetDummy.EntityId, nowMs + 1500.0);

                    // Play steal effects
                    BasketballAudioParticles.PlayStealSound(sapi.World, playerPos);
                    BasketballAudioParticles.SpawnBounceParticles(sapi.World, playerPos);
                }
            }
        }

        public void SetStealImmunity(string playerUid, long dummyEntityId, double expiryMs)
        {
            stealImmunityTimers[$"{playerUid}_dummy_{dummyEntityId}"] = expiryMs;
        }

        public bool HasStealImmunity(string playerUid, long dummyEntityId, double nowMs)
        {
            string key = $"{playerUid}_dummy_{dummyEntityId}";
            return stealImmunityTimers.TryGetValue(key, out double expiry) && nowMs < expiry;
        }

        public void TransferBall(IServerPlayer fromPlayer, IServerPlayer toPlayer)
        {
            if (fromPlayer.Entity == null || toPlayer.Entity == null || fromPlayer.InventoryManager == null || toPlayer.InventoryManager == null) return;

            // Remove from original holder
            ItemSlot? ballSlot = FindBallSlot(fromPlayer.Entity);
            if (ballSlot != null)
            {
                ballSlot.TakeOut(1);
                ballSlot.MarkDirty();
            }

            // Give to new holder
            Item ballItem = api.World.GetItem(new AssetLocation("basketballallstars:basketball"));
            if (ballItem != null)
            {
                ItemStack stack = new ItemStack(ballItem, 1);
                toPlayer.InventoryManager.TryGiveItemstack(stack, true);
            }

            ItemBasketball.RemoveCarrierBuffs(fromPlayer.Entity);
            ItemBasketball.ApplyCarrierBuffs(toPlayer.Entity);
        }

        public static bool IsHoldingBall(EntityPlayer? player)
        {
            if (player == null) return false;
            try
            {
                IPlayer? p = player.World?.PlayerByUid(player.PlayerUID);
                if (p?.InventoryManager == null) return false;

                return player.RightHandItemSlot?.Itemstack?.Item is ItemBasketball ||
                       player.LeftHandItemSlot?.Itemstack?.Item is ItemBasketball;
            }
            catch
            {
                return false;
            }
        }

        public static ItemSlot? FindBallSlot(EntityPlayer? player)
        {
            if (player == null) return null;
            try
            {
                IPlayer? p = player.World?.PlayerByUid(player.PlayerUID);
                if (p?.InventoryManager == null) return null;

                if (player.RightHandItemSlot?.Itemstack?.Item is ItemBasketball) return player.RightHandItemSlot;
                if (player.LeftHandItemSlot?.Itemstack?.Item is ItemBasketball) return player.LeftHandItemSlot;
            }
            catch
            {
                return null;
            }
            return null;
        }

        private void OnClientStateTick(float dt)
        {
            if (api is not ICoreClientAPI capi) return;
            var player = capi.World.Player;
            if (player?.Entity == null) return;

            // Client carrier state
            if (IsHoldingBall(player.Entity))
            {
                ItemBasketball.ApplyCarrierBuffs(player.Entity);
            }
        }
    }
}
