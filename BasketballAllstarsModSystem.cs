using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using BasketballAllstars.Blocks;
using BasketballAllstars.Entities;
using BasketballAllstars.Gui;
using BasketballAllstars.Items;
using BasketballAllstars.Network;
using BasketballAllstars.Systems;

namespace BasketballAllstars
{
    public class BasketballAllstarsModSystem : ModSystem
    {
        public const string CHANNEL_NAME = "basketballallstars";

        public static ICoreClientAPI? Capi { get; private set; }
        public static ICoreServerAPI? Sapi { get; private set; }
        public static BasketballAllstarsModSystem? Instance { get; private set; }

        private DunkTrajectorySystem? dunkTrajectorySystem;
        private AirClashSystem? airClashSystem;
        private BasketballGameState? gameState;
        private GuiBasketballHud? hudElement;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            Instance = this;

            // Register Items, Blocks, BlockEntities, and Entities
            api.RegisterItemClass("ItemBasketball", typeof(ItemBasketball));
            api.RegisterItemClass("ItemBasketballDummy", typeof(ItemBasketballDummy));
            api.RegisterBlockClass("BlockBasketball", typeof(BlockBasketball));
            api.RegisterBlockClass("BlockHoop", typeof(BlockHoop));
            api.RegisterBlockEntityClass("BlockEntityHoop", typeof(BlockEntityHoop));
            api.RegisterEntity("EntityBasketball", typeof(EntityBasketball));
            api.RegisterEntity("EntityBasketballDummy", typeof(EntityBasketballDummy));

            // Register Network Channel & Packets
            api.Network.RegisterChannel(CHANNEL_NAME)
                .RegisterMessageType<JumpChargeMessage>()
                .RegisterMessageType<DunkStartRequestMessage>()
                .RegisterMessageType<InterceptStartRequestMessage>()
                .RegisterMessageType<TrajectorySyncMessage>()
                .RegisterMessageType<AirClashStartMessage>()
                .RegisterMessageType<AirClashInputProgressMessage>()
                .RegisterMessageType<AirClashDuelProgressSyncMessage>()
                .RegisterMessageType<AirClashResultMessage>()
                .RegisterMessageType<BallStealEventMessage>()
                .RegisterMessageType<HoopScoreEventMessage>()
                .RegisterMessageType<TrajectoryCancelMessage>();

            // Initialize Core Systems
            dunkTrajectorySystem = new DunkTrajectorySystem(api);
            airClashSystem = new AirClashSystem(api);
            gameState = new BasketballGameState(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Sapi = api;

            dunkTrajectorySystem?.Start();
            gameState?.Start();

            // Set Network Handlers
            var channel = api.Network.GetChannel(CHANNEL_NAME);
            if (channel != null)
            {
                channel.SetMessageHandler<DunkStartRequestMessage>(OnDunkStartRequest)
                       .SetMessageHandler<InterceptStartRequestMessage>(OnInterceptStartRequest)
                       .SetMessageHandler<AirClashInputProgressMessage>(OnAirClashInputProgress);
            }

            // Cleanup on player events
            api.Event.PlayerDeath += (player, damageSource) =>
            {
                dunkTrajectorySystem?.CancelTrajectory(player.PlayerUID);
            };
            api.Event.PlayerDisconnect += (player) =>
            {
                dunkTrajectorySystem?.CancelTrajectory(player.PlayerUID);
            };

            // Register chat command for convenient testing
            api.ChatCommands.Create("allstars")
                .WithDescription("Basketball Allstars debug, kit, and dummy command")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("dummy")
                    .WithDescription("Spawns a Basketball Practice Dummy in front of you")
                    .HandleWith((args) =>
                    {
                        var player = args.Caller.Player as IServerPlayer;
                        if (player?.Entity == null) return TextCommandResult.Success();

                        Vec3f lookVecF = player.Entity.Pos.GetViewVector().Normalize();
                        Vec3d spawnPos = player.Entity.Pos.XYZ.AddCopy(lookVecF.X * 2.0, 0, lookVecF.Z * 2.0);

                        EntityProperties dummyType = api.World.GetEntityType(new AssetLocation("basketballallstars:basketballdummy"));
                        if (dummyType != null)
                        {
                            Entity entity = api.World.ClassRegistry.CreateEntity(dummyType);
                            if (entity is EntityBasketballDummy dummy)
                            {
                                dummy.Pos.SetPos(spawnPos);
                                dummy.Pos.Yaw = player.Entity.Pos.Yaw + GameMath.PI - (float)GameMath.PIHALF; // Face the player
                                dummy.HasBall = false;

                                api.World.SpawnEntity(dummy);
                                api.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), dummy, player);
                                player.SendMessage(0, "Spawned Basketball Practice Dummy!", EnumChatType.Notification);
                            }
                        }
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
                .HandleWith((args) =>
                {
                    var player = args.Caller.Player as IServerPlayer;
                    if (player?.Entity == null) return TextCommandResult.Success();

                    // Give a basketball
                    Item ballItem = api.World.GetItem(new AssetLocation("basketballallstars:basketball"));
                    if (ballItem != null)
                    {
                        player.InventoryManager.TryGiveItemstack(new ItemStack(ballItem, 1), true);
                    }

                    // Give a hoop block
                    Block hoopBlock = api.World.GetBlock(new AssetLocation("basketballallstars:hoop-north"));
                    if (hoopBlock != null)
                    {
                        player.InventoryManager.TryGiveItemstack(new ItemStack(hoopBlock, 2), true);
                    }

                    // Give a practice dummy item
                    Item dummyItem = api.World.GetItem(new AssetLocation("basketballallstars:basketballdummy"));
                    if (dummyItem != null)
                    {
                        player.InventoryManager.TryGiveItemstack(new ItemStack(dummyItem, 2), true);
                    }

                    player.SendMessage(0, "Granted Basketball Allstars kit (Basketball, Hoops, and Practice Dummies).", EnumChatType.Notification);
                    return TextCommandResult.Success();
                });

            api.Logger.Notification("[BasketballAllstars] Server systems initialized.");
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Capi = api;

            // Initialize Client Harmony Patches for Smooth Dunk Flight & Visual Rotations
            var clientHarmony = new HarmonyLib.Harmony("basketballallstars.client");
            Patches.DunkFlightRendererPatch.InitClientPatches(clientHarmony, api);

            dunkTrajectorySystem?.Start();
            gameState?.Start();

            // Register Client Network Handlers
            var channel = api.Network.GetChannel(CHANNEL_NAME);
            if (channel != null)
            {
                channel.SetMessageHandler<TrajectorySyncMessage>(msg => dunkTrajectorySystem?.OnClientTrajectorySync(msg))
                       .SetMessageHandler<TrajectoryCancelMessage>(msg => dunkTrajectorySystem?.OnClientTrajectoryCancel(msg))
                       .SetMessageHandler<AirClashStartMessage>(msg => airClashSystem?.OnClientStartClash(msg))
                       .SetMessageHandler<AirClashDuelProgressSyncMessage>(msg => airClashSystem?.OnClientClashProgress(msg))
                       .SetMessageHandler<AirClashResultMessage>(msg => airClashSystem?.OnClientClashResult(msg));
            }

            // Register In-Game HUD
            hudElement = new GuiBasketballHud(api);

            api.Logger.Notification("[BasketballAllstars] Client systems initialized.");
        }

        // ========================================================================
        // Server Network Message Handlers
        // ========================================================================

        private void OnDunkStartRequest(IServerPlayer fromPlayer, DunkStartRequestMessage msg)
        {
            dunkTrajectorySystem?.StartDunkTrajectory(fromPlayer, msg.TargetHoopPos, msg.ChargeAmount, msg.DunkStyle, msg.Revolutions);
        }

        private void OnInterceptStartRequest(IServerPlayer fromPlayer, InterceptStartRequestMessage msg)
        {
            dunkTrajectorySystem?.StartInterceptTrajectory(fromPlayer, msg.TargetPlayerUid, msg.ChargeAmount);
        }

        private void OnAirClashInputProgress(IServerPlayer fromPlayer, AirClashInputProgressMessage msg)
        {
            airClashSystem?.HandleClientInputProgress(fromPlayer, msg.DuelId, msg.CompletedInputs);
        }
    }
}
