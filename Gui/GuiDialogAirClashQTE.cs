using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using BasketballAllstars.Network;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Gui
{
    public class GuiDialogAirClashQTE : GuiDialogGeneric
    {
        public static GuiDialogAirClashQTE? Instance { get; private set; }

        public override string ToggleKeyCombinationCode => null!;

        private readonly int duelId;
        private readonly string dunkerUid;
        private readonly string interceptorUid;
        private readonly byte[] sequence;
        private int myProgress = 0;
        private int opponentProgress = 0;
        private float errorShakeTimer = 0f;
        private string resultMessage = "";
        private float resultCloseTimer = 0f;
        private bool isWinner = false;

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

            double width = 640;
            double height = 220;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, width, height);

            var composer = capi.Gui.CreateCompo("airClashGui", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .BeginChildElements(bgBounds)
                .AddDynamicCustomDraw(ElementBounds.Fixed(10, 10, width - 20, height - 20), DrawClashCanvas, "clashCanvas")
                .EndChildElements();

            SingleComposer = composer;
            SingleComposer.Compose();
        }

        private void DrawClashCanvas(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double w = currentBounds.InnerWidth;
            double h = currentBounds.InnerHeight;

            // Background subtle gradient
            ctx.SetSourceRGBA(0.05, 0.05, 0.08, 0.85);
            ctx.Paint();

            // Title Banner
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(22.0);

            if (!string.IsNullOrEmpty(resultMessage))
            {
                if (isWinner)
                {
                    ctx.SetSourceRGBA(0.2, 1.0, 0.3, 1.0);
                    ctx.MoveTo(w * 0.5 - 90, 35);
                    ctx.ShowText("VICTORY! CLASH WON!");
                }
                else
                {
                    ctx.SetSourceRGBA(1.0, 0.25, 0.25, 1.0);
                    ctx.MoveTo(w * 0.5 - 90, 35);
                    ctx.ShowText("DEFLECTED! CLASH LOST!");
                }
            }
            else
            {
                ctx.SetSourceRGBA(1.0, 0.85, 0.1, 1.0);
                ctx.MoveTo(w * 0.5 - 110, 35);
                ctx.ShowText("MID-AIR CLASH! PARRY DUEL!");
            }

            // Subtitle
            ctx.SetFontSize(13.0);
            ctx.SetSourceRGBA(0.8, 0.8, 0.85, 0.9);
            ctx.MoveTo(w * 0.5 - 140, 58);
            ctx.ShowText("Input the 10 directional arrow keys (WASD / Arrows) rapidly!");

            // Draw 10 Arrow Buttons
            double startX = 30;
            double arrowY = 80;
            double slotSize = 52;
            double spacing = 6;

            for (int i = 0; i < 10; i++)
            {
                double x = startX + i * (slotSize + spacing);
                byte dir = sequence[i];

                // Shake offset on current slot if in error
                double offsetX = 0;
                if (i == myProgress && errorShakeTimer > 0f)
                {
                    offsetX = Math.Sin(errorShakeTimer * 40.0) * 4.0;
                }

                // Slot box
                ctx.Rectangle(x + offsetX, arrowY, slotSize, slotSize);

                if (i < myProgress)
                {
                    // Completed slot (green)
                    ctx.SetSourceRGBA(0.15, 0.85, 0.25, 0.95);
                    ctx.FillPreserve();
                    ctx.SetSourceRGBA(0.8, 1.0, 0.8, 1.0);
                    ctx.LineWidth = 2.0;
                    ctx.Stroke();
                }
                else if (i == myProgress)
                {
                    // Active current slot (glowing gold or error red)
                    if (errorShakeTimer > 0f)
                    {
                        ctx.SetSourceRGBA(0.9, 0.2, 0.2, 0.95);
                    }
                    else
                    {
                        ctx.SetSourceRGBA(1.0, 0.75, 0.1, 0.95);
                    }
                    ctx.FillPreserve();
                    ctx.SetSourceRGBA(1.0, 1.0, 1.0, 1.0);
                    ctx.LineWidth = 3.0;
                    ctx.Stroke();
                }
                else
                {
                    // Pending slot (dark gray)
                    ctx.SetSourceRGBA(0.2, 0.22, 0.28, 0.85);
                    ctx.FillPreserve();
                    ctx.SetSourceRGBA(0.4, 0.45, 0.55, 0.8);
                    ctx.LineWidth = 1.5;
                    ctx.Stroke();
                }

                // Draw Directional Arrow
                DrawArrowIcon(ctx, x + offsetX + slotSize * 0.5, arrowY + slotSize * 0.5, dir, i <= myProgress);
            }

            // Duel Progress Bar (You vs Opponent)
            double barY = 155;
            double barWidth = w - 60;
            double barHeight = 14;

            ctx.Rectangle(startX, barY, barWidth, barHeight);
            ctx.SetSourceRGBA(0.1, 0.12, 0.16, 0.9);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.3, 0.35, 0.45, 0.8);
            ctx.LineWidth = 1.0;
            ctx.Stroke();

            // Your progress fill (Cyan)
            double myFillWidth = (myProgress / 10.0) * barWidth;
            if (myFillWidth > 0)
            {
                ctx.Rectangle(startX, barY, myFillWidth, barHeight * 0.5);
                ctx.SetSourceRGBA(0.0, 0.85, 0.95, 0.9);
                ctx.Fill();
            }

            // Opponent progress fill (Orange)
            double oppFillWidth = (opponentProgress / 10.0) * barWidth;
            if (oppFillWidth > 0)
            {
                ctx.Rectangle(startX, barY + barHeight * 0.5, oppFillWidth, barHeight * 0.5);
                ctx.SetSourceRGBA(0.95, 0.45, 0.05, 0.9);
                ctx.Fill();
            }

            // Progress labels
            ctx.SetFontSize(12.0);
            ctx.SetSourceRGBA(0.0, 0.95, 1.0, 1.0);
            ctx.MoveTo(startX, barY + 30);
            ctx.ShowText($"YOU: {myProgress}/10");

            ctx.SetSourceRGBA(1.0, 0.55, 0.1, 1.0);
            ctx.MoveTo(w - startX - 110, barY + 30);
            ctx.ShowText($"OPPONENT: {opponentProgress}/10");
        }

        private void DrawArrowIcon(Context ctx, double cx, double cy, byte dir, bool isHighlighted)
        {
            ctx.Save();
            ctx.Translate(cx, cy);

            // Rotate based on direction: 0 = Up, 1 = Right, 2 = Down, 3 = Left
            double angle = dir * (Math.PI / 2.0);
            ctx.Rotate(angle);

            // Draw clean polygon arrow
            ctx.MoveTo(0, -14);
            ctx.LineTo(12, 2);
            ctx.LineTo(5, 2);
            ctx.LineTo(5, 13);
            ctx.LineTo(-5, 13);
            ctx.LineTo(-5, 2);
            ctx.LineTo(-12, 2);
            ctx.ClosePath();

            if (isHighlighted)
            {
                ctx.SetSourceRGBA(1.0, 1.0, 1.0, 1.0);
            }
            else
            {
                ctx.SetSourceRGBA(0.65, 0.7, 0.78, 0.85);
            }

            ctx.Fill();
            ctx.Restore();
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if (!string.IsNullOrEmpty(resultMessage)) return;

            byte? inputDir = null;
            if (args.KeyCode == (int)GlKeys.W || args.KeyCode == (int)GlKeys.Up) inputDir = 0;
            else if (args.KeyCode == (int)GlKeys.D || args.KeyCode == (int)GlKeys.Right) inputDir = 1;
            else if (args.KeyCode == (int)GlKeys.S || args.KeyCode == (int)GlKeys.Down) inputDir = 2;
            else if (args.KeyCode == (int)GlKeys.A || args.KeyCode == (int)GlKeys.Left) inputDir = 3;

            if (inputDir.HasValue && myProgress < 10)
            {
                args.Handled = true;
                if (inputDir.Value == sequence[myProgress])
                {
                    // Correct input!
                    myProgress++;
                    errorShakeTimer = 0f;

                    // Send progress to server
                    var channel = capi.Network.GetChannel(BasketballAllstarsModSystem.CHANNEL_NAME);
                    channel?.SendPacket(new AirClashInputProgressMessage
                    {
                        DuelId = duelId,
                        CompletedInputs = myProgress
                    });

                    // Play success click sound
                    capi.World.PlaySoundAt(new AssetLocation("game:sounds/tick"), capi.World.Player.Entity.Pos.X, capi.World.Player.Entity.Pos.Y, capi.World.Player.Entity.Pos.Z, null, true, 8f, 1.2f + myProgress * 0.08f);

                    SingleComposer?.GetCustomDraw("clashCanvas")?.Redraw();
                }
                else
                {
                    // Incorrect input!
                    errorShakeTimer = 0.25f;
                    capi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodbreak"), capi.World.Player.Entity.Pos.X, capi.World.Player.Entity.Pos.Y, capi.World.Player.Entity.Pos.Z, null, true, 8f, 0.8f);
                    SingleComposer?.GetCustomDraw("clashCanvas")?.Redraw();
                }
            }
        }

        public void UpdateProgress(int dunkerProgress, int interceptorProgress)
        {
            bool amDunker = capi.World.Player.PlayerUID == dunkerUid;
            opponentProgress = amDunker ? interceptorProgress : dunkerProgress;
            SingleComposer?.GetCustomDraw("clashCanvas")?.Redraw();
        }

        public void ShowResult(bool dunkerWon)
        {
            bool amDunker = capi.World.Player.PlayerUID == dunkerUid;
            isWinner = (amDunker && dunkerWon) || (!amDunker && !dunkerWon);
            resultMessage = isWinner ? "VICTORY!" : "DEFEAT!";
            resultCloseTimer = 1.4f;

            SingleComposer?.GetCustomDraw("clashCanvas")?.Redraw();
        }

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);

            if (errorShakeTimer > 0f)
            {
                errorShakeTimer -= deltaTime;
                SingleComposer?.GetCustomDraw("clashCanvas")?.Redraw();
            }

            if (!string.IsNullOrEmpty(resultMessage))
            {
                resultCloseTimer -= deltaTime;
                if (resultCloseTimer <= 0f)
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
