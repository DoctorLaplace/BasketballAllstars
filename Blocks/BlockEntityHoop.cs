using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Network;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Blocks
{
    public class BlockEntityHoop : BlockEntity
    {
        public int TotalBasketsScored { get; private set; } = 0;
        public int TotalPoints { get; private set; } = 0;
        public double LastScoredTimeMs { get; private set; } = 0;
        public bool IsDunkable { get; set; } = true;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
        }

        public bool ToggleDunkable(IPlayer byPlayer)
        {
            IsDunkable = !IsDunkable;
            MarkDirty(true);
            return IsDunkable;
        }

        public void ScoreBasket(IServerPlayer? scorer, bool isDunk)
        {
            if (Api == null || Api.Side != EnumAppSide.Server) return;

            int points = isDunk ? 3 : 2;
            TotalBasketsScored++;
            TotalPoints += points;
            LastScoredTimeMs = Api.World.ElapsedMilliseconds;
            MarkDirty(true);

            Vec3d rimPos = (Block as BlockHoop)?.GetRimCenter(Pos) ?? new Vec3d(Pos.X + 0.5, Pos.Y + 0.8, Pos.Z + 0.5);

            // Audio & Particle Effects
            BasketballAudioParticles.PlayHoopScoreSounds(Api.World, rimPos, isDunk);
            BasketballAudioParticles.SpawnHoopCelebrationParticles(Api.World, rimPos);

            string scorerName = scorer?.PlayerName ?? "Unknown Player";
            string scorerUid = scorer?.PlayerUID ?? "";

            // Broadcast score notification to network
            var serverChannel = (Api as ICoreServerAPI)?.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
            serverChannel?.BroadcastPacket(new HoopScoreEventMessage
            {
                HoopPos = Pos,
                ScorerUid = scorerUid,
                ScorerName = scorerName,
                Points = points,
                IsDunk = isDunk
            });
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("totalBaskets", TotalBasketsScored);
            tree.SetInt("totalPoints", TotalPoints);
            tree.SetDouble("lastScoredTimeMs", LastScoredTimeMs);
            tree.SetBool("isDunkable", IsDunkable);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            TotalBasketsScored = tree.GetInt("totalBaskets", 0);
            TotalPoints = tree.GetInt("totalPoints", 0);
            LastScoredTimeMs = tree.GetDouble("lastScoredTimeMs", 0);
            IsDunkable = tree.GetBool("isDunkable", true);
        }
    }
}
