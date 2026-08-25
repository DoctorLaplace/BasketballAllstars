using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using BasketballAllstars.Network;

namespace BasketballAllstars.Gui
{
    /// <summary>
    /// Sleek, minimalist mid-air clash QTE display located at the top center of the screen.
    /// Displays the 10-arrow sequence in a smoothly sliding horizontal strip where
    /// the active arrow is in the center, completed arrows slide left and turn green,
    /// and making a single mistake immediately fails the clash.
    /// </summary>
    public class GuiDialogAirClashQTE : GuiDialogGeneric
    {
        public static GuiDialogAirClashQTE? Instance { get; private set; }

        public override string ToggleKeyCombinationCode => null!;

        private readonly int duelId;
        private readonly string dunkerUid;
        private readonly string interceptorUid;
        private readonly byte[] sequence;
        private int myProgress = 0;
        private double scrollProgress = 0.0;
        private bool hasFailed = false;
        private bool isFinished = false;
        private float closeTimer = 0f;

        public GuiDialogAirClashQTE(ICoreClientAPI capi, AirClashStartMessage msg) : base("AERIAL CLASH!", capi)
        {
            duelId = msg.DuelId;
            dunkerUid = msg.DunkerUid;
            interceptorUid = msg.InterceptorUid;
            sequence = msg.QTESequence;
            Instance = this;
        }

        public static void OpenDuel(ICoreClientAPI capi, AirClashStartMessage msg)
        {
            Instance?.TryClose();
            var dialog = new GuiDialogAirClashQTE(capi, msg);
            dialog.TryOpen();
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ClearComposers();

            double width = 760;
            double height = 80;

            // Position near the top of the screen with comfortable top margin
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterTop)
                .WithFixedPadding(0, 50);

            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, width, height);

            var composer = capi.Gui.CreateCompo("airClashStrip", dialogBounds)
                .BeginChildElements(bgBounds)
                .AddDynamicCustomDraw(ElementBounds.Fixed(0, 0, width, height), DrawArrowStrip, "arrowCanvas")
                .EndChildElements();

            SingleComposer = composer;
            SingleComposer.Compose();
        }

        private void DrawArrowStrip(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double w = currentBounds.InnerWidth;
            double h = currentBounds.InnerHeight;
            double centerX = w * 0.5;
            double centerY = h * 0.5;
            double slotPitch = 56.0;

            for (int i = 0; i < sequence.Length; i++)
            {
                byte dir = sequence[i];
                double slotX = centerX + (i - scrollProgress) * slotPitch;

                if (slotX < -40 || slotX > w + 40) continue;

                if (i < myProgress)
                {
                    // Completed Arrow: Sleek vibrant green
                    DrawArrowIcon(ctx, slotX, centerY, dir, 1.0, 0.22, 0.95, 0.38, 0.95);
                }
                else if (i == myProgress)
                {
                    if (hasFailed)
                    {
                        // Failed Arrow: Bright Red
                        DrawArrowIcon(ctx, slotX, centerY, dir, 1.30, 1.0, 0.20, 0.20, 1.0);
                    }
                    else
                    {
                        // Active Current Arrow: Glowing crisp white in center, slightly enlarged
                        // Subtle soft glow background
                        ctx.Save();
                        ctx.Arc(slotX, centerY, 24.0, 0, Math.PI * 2.0);
                        ctx.SetSourceRGBA(1.0, 0.88, 0.20, 0.25);
                        ctx.Fill();
                        ctx.Restore();

                        DrawArrowIcon(ctx, slotX, centerY, dir, 1.25, 1.0, 1.0, 1.0, 1.0);
                    }
                }
                else
                {
                    // Upcoming Arrows: Semi-transparent sleek slate white
                    DrawArrowIcon(ctx, slotX, centerY, dir, 0.95, 0.82, 0.88, 0.95, 0.38);
                }
            }
        }

        private void DrawArrowIcon(Context ctx, double cx, double cy, byte dir, double scale, double r, double g, double b, double a)
        {
            ctx.Save();
            ctx.Translate(cx, cy);

            // Rotate based on direction: 0 = Up, 1 = Right, 2 = Down, 3 = Left
            double angle = dir * (Math.PI / 2.0);
            ctx.Rotate(angle);

            // Draw crisp modern polygon chevron arrow
            ctx.MoveTo(0, -15 * scale);
            ctx.LineTo(12 * scale, 1 * scale);
            ctx.LineTo(5 * scale, 1 * scale);
            ctx.LineTo(5 * scale, 13 * scale);
            ctx.LineTo(-5 * scale, 13 * scale);
            ctx.LineTo(-5 * scale, 1 * scale);
            ctx.LineTo(-12 * scale, 1 * scale);
            ctx.ClosePath();

            ctx.SetSourceRGBA(r, g, b, a);
            ctx.Fill();

            ctx.Restore();
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if (isFinished || hasFailed) return;

            byte? inputDir = null;
            if (args.KeyCode == (int)GlKeys.W || args.KeyCode == (int)GlKeys.Up) inputDir = 0;
            else if (args.KeyCode == (int)GlKeys.D || args.KeyCode == (int)GlKeys.Right) inputDir = 1;
            else if (args.KeyCode == (int)GlKeys.S || args.KeyCode == (int)GlKeys.Down) inputDir = 2;
            else if (args.KeyCode == (int)GlKeys.A || args.KeyCode == (int)GlKeys.Left) inputDir = 3;

            if (inputDir.HasValue && myProgress < sequence.Length)
            {
                args.Handled = true;
                if (inputDir.Value == sequence[myProgress])
                {
                    // Correct arrow!
                    myProgress++;

                    var channel = capi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                    channel?.SendPacket(new AirClashInputProgressMessage
                    {
                        DuelId = duelId,
                        CompletedInputs = myProgress
                    });

                    // Play crisp tick sound
                    capi.World.PlaySoundAt(new AssetLocation("game:sounds/tick"), capi.World.Player.Entity.Pos.X, capi.World.Player.Entity.Pos.Y, capi.World.Player.Entity.Pos.Z, null, true, 8f, 1.2f + myProgress * 0.08f);

                    if (myProgress >= sequence.Length)
                    {
                        isFinished = true;
                        closeTimer = 0.6f;
                    }
                }
                else
                {
                    // Single mistake fails the clash immediately!
                    hasFailed = true;
                    isFinished = true;
                    closeTimer = 0.6f;

                    // Send failure code (-1) to server to instantly award victory to the opponent
                    var channel = capi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                    channel?.SendPacket(new AirClashInputProgressMessage
                    {
                        DuelId = duelId,
                        CompletedInputs = -1
                    });

                    capi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodbreak"), capi.World.Player.Entity.Pos.X, capi.World.Player.Entity.Pos.Y, capi.World.Player.Entity.Pos.Z, null, true, 8f, 0.8f);
                }

                SingleComposer?.GetCustomDraw("arrowCanvas")?.Redraw();
            }
        }

        public void UpdateProgress(int dunkerProgress, int interceptorProgress)
        {
            // Optional progress sync if needed
        }

        public void ShowResult(bool dunkerWon)
        {
            isFinished = true;
            if (closeTimer <= 0f)
            {
                closeTimer = 0.5f;
            }
        }

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);

            // Smooth sliding animation of the arrow strip towards current arrow
            double targetScroll = myProgress;
            if (Math.Abs(scrollProgress - targetScroll) > 0.001)
            {
                scrollProgress += (targetScroll - scrollProgress) * Math.Min(deltaTime * 18.0, 1.0);
                SingleComposer?.GetCustomDraw("arrowCanvas")?.Redraw();
            }

            if (isFinished)
            {
                closeTimer -= deltaTime;
                if (closeTimer <= 0f)
                {
                    TryClose();
                }
            }
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            Instance = null;
        }
    }
}
