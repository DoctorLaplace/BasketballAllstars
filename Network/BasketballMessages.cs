using System;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace BasketballAllstars.Network
{
    public enum DirectionalKey : byte
    {
        Up = 0,     // W or Up Arrow
        Right = 1,  // D or Right Arrow
        Down = 2,   // S or Down Arrow
        Left = 3    // A or Left Arrow
    }

    [ProtoContract]
    public class JumpChargeMessage
    {
        [ProtoMember(1)]
        public float ChargePercent { get; set; }

        [ProtoMember(2)]
        public bool IsCharging { get; set; }
    }

    [ProtoContract]
    public class DunkStartRequestMessage
    {
        [ProtoMember(1)]
        public BlockPos TargetHoopPos { get; set; }

        [ProtoMember(2)]
        public float ChargeAmount { get; set; }

        [ProtoMember(3)]
        public int DunkStyle { get; set; }

        [ProtoMember(4)]
        public int Revolutions { get; set; } = 1;
    }

    [ProtoContract]
    public class InterceptStartRequestMessage
    {
        [ProtoMember(1)]
        public string TargetPlayerUid { get; set; } = "";

        [ProtoMember(2)]
        public float ChargeAmount { get; set; }
    }

    [ProtoContract]
    public class TrajectorySyncMessage
    {
        [ProtoMember(1)]
        public string PlayerUid { get; set; } = "";

        [ProtoMember(2)]
        public Vec3d StartPos { get; set; } = new Vec3d();

        [ProtoMember(3)]
        public Vec3d TargetPos { get; set; } = new Vec3d();

        [ProtoMember(4)]
        public double StartTotalMs { get; set; }

        [ProtoMember(5)]
        public float DurationSeconds { get; set; }

        [ProtoMember(6)]
        public float ArcHeight { get; set; }

        [ProtoMember(7)]
        public bool IsDunk { get; set; }

        [ProtoMember(8)]
        public int DunkStyle { get; set; }

        [ProtoMember(9)]
        public int Revolutions { get; set; } = 1;
    }

    [ProtoContract]
    public class AirClashStartMessage
    {
        [ProtoMember(1)]
        public int DuelId { get; set; }

        [ProtoMember(2)]
        public string DunkerUid { get; set; } = "";

        [ProtoMember(3)]
        public string InterceptorUid { get; set; } = "";

        [ProtoMember(4)]
        public byte[] QTESequence { get; set; } = Array.Empty<byte>();

        [ProtoMember(5)]
        public Vec3d ClashPos { get; set; } = new Vec3d();
    }

    [ProtoContract]
    public class AirClashInputProgressMessage
    {
        [ProtoMember(1)]
        public int DuelId { get; set; }

        [ProtoMember(2)]
        public int CompletedInputs { get; set; }
    }

    [ProtoContract]
    public class AirClashDuelProgressSyncMessage
    {
        [ProtoMember(1)]
        public int DuelId { get; set; }

        [ProtoMember(2)]
        public int DunkerProgress { get; set; }

        [ProtoMember(3)]
        public int InterceptorProgress { get; set; }
    }

    [ProtoContract]
    public class AirClashResultMessage
    {
        [ProtoMember(1)]
        public int DuelId { get; set; }

        [ProtoMember(2)]
        public string WinnerUid { get; set; } = "";

        [ProtoMember(3)]
        public string LoserUid { get; set; } = "";

        [ProtoMember(4)]
        public bool DunkerWon { get; set; }

        [ProtoMember(5)]
        public Vec3d ClashPos { get; set; } = new Vec3d();
    }

    [ProtoContract]
    public class BallStealEventMessage
    {
        [ProtoMember(1)]
        public string StealerUid { get; set; } = "";

        [ProtoMember(2)]
        public string VictimUid { get; set; } = "";
    }

    [ProtoContract]
    public class HoopScoreEventMessage
    {
        [ProtoMember(1)]
        public BlockPos HoopPos { get; set; } = new BlockPos(0, 0, 0, 0);

        [ProtoMember(2)]
        public string ScorerUid { get; set; } = "";

        [ProtoMember(3)]
        public string ScorerName { get; set; } = "";

        [ProtoMember(4)]
        public int Points { get; set; }

        [ProtoMember(5)]
        public bool IsDunk { get; set; }
    }

    [ProtoContract]
    public class TrajectoryCancelMessage
    {
        [ProtoMember(1)]
        public string PlayerUid { get; set; } = "";

        [ProtoMember(2)]
        public Vec3d ReleaseMotion { get; set; } = new Vec3d();
    }
}
