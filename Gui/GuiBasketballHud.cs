using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Gui
{
    public class GuiBasketballHud : HudElement
    {
        public override string ToggleKeyCombinationCode => null!;

        public GuiBasketballHud(ICoreClientAPI capi) : base(capi)
        {
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (capi?.World?.Player?.Entity == null) return;

            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return;

            bool isCharging = dunkSystem.ClientIsChargingJump;
            bool hasHoopLock = dunkSystem.ClientLockedHoopPos != null;
            bool hasInterceptLock = !string.IsNullOrEmpty(dunkSystem.ClientLockedDunkerUid);

            // Only open / compose when there is something to show
            if (isCharging || hasHoopLock || hasInterceptLock)
            {
                if (!IsOpened())
                {
                    TryOpen();
                }
                SingleComposer?.GetCustomDraw("hudCanvas")?.Redraw();
            }
            else
            {
                if (IsOpened())
                {
                    TryClose();
                }
            }

            base.OnRenderGUI(deltaTime);
        }

        public override bool TryOpen()
        {
            ComposeHud();
            return base.TryOpen();
        }

        private void ComposeHud()
        {
            ClearComposers();

            double screenW = capi.Render.FrameWidth;
            double screenH = capi.Render.FrameHeight;

            ElementBounds dialogBounds = ElementBounds.Fixed(0, 0, screenW, screenH);

            var composer = capi.Gui.CreateCompo("basketballHud", dialogBounds)
                .AddDynamicCustomDraw(dialogBounds, DrawHudCanvas, "hudCanvas");

            SingleComposer = composer;
            SingleComposer.Compose();
        }

        private void DrawHudCanvas(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double w = capi.Render.FrameWidth;
            double h = capi.Render.FrameHeight;
            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return;

            double cx = w * 0.5;
            double cy = h * 0.5;

            // 1. Lock-On Reticle (Center Screen)
            if (dunkSystem.ClientLockedHoopPos != null)
            {
                // SLAM DUNK LOCK-ON
                ctx.Save();
                ctx.LineWidth = 2.5;

                // Diamond Reticle
                double r = 32.0;
                ctx.MoveTo(cx, cy - r);
                ctx.LineTo(cx + r, cy);
                ctx.LineTo(cx, cy + r);
                ctx.LineTo(cx - r, cy);
                ctx.ClosePath();

                ctx.SetSourceRGBA(1.0, 0.85, 0.15, 0.85); // Neon Gold
                ctx.StrokePreserve();
                ctx.SetSourceRGBA(1.0, 0.85, 0.15, 0.15);
                ctx.Fill();

                // Target Text
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(14.0);
                ctx.SetSourceRGBA(1.0, 0.95, 0.2, 0.95);
                ctx.MoveTo(cx - 55, cy + r + 20);
                ctx.ShowText("[ SLAM DUNK READY ]");

                ctx.Restore();
            }
            else if (!string.IsNullOrEmpty(dunkSystem.ClientLockedDunkerUid))
            {
                // INTERCEPTION LOCK-ON
                ctx.Save();
                ctx.LineWidth = 2.5;

                // Crosshair Reticle
                double r = 28.0;
                ctx.Arc(cx, cy, r, 0, Math.PI * 2);
                ctx.SetSourceRGBA(0.0, 0.85, 1.0, 0.85); // Cyan
                ctx.StrokePreserve();
                ctx.SetSourceRGBA(0.0, 0.85, 1.0, 0.15);
                ctx.Fill();

                // Target Text
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(14.0);
                ctx.SetSourceRGBA(0.2, 1.0, 1.0, 0.95);
                ctx.MoveTo(cx - 65, cy + r + 20);
                ctx.ShowText("[ INTERCEPT READY ]");

                ctx.Restore();
            }

            // 2. Jump Charge Bar (Bottom Center)
            if (dunkSystem.ClientIsChargingJump)
            {
                float charge = dunkSystem.ClientJumpCharge;
                double barWidth = 260.0;
                double barHeight = 16.0;
                double barX = cx - barWidth * 0.5;
                double barY = h - 140.0;

                ctx.Save();

                // Frame
                ctx.Rectangle(barX, barY, barWidth, barHeight);
                ctx.SetSourceRGBA(0.05, 0.05, 0.08, 0.85);
                ctx.FillPreserve();
                ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.7);
                ctx.LineWidth = 1.5;
                ctx.Stroke();

                // Fill gradient based on charge
                double fillW = barWidth * Math.Clamp(charge, 0f, 1f);
                if (fillW > 0)
                {
                    ctx.Rectangle(barX, barY, fillW, barHeight);
                    // Shift from Orange to Bright Yellow-Gold
                    ctx.SetSourceRGBA(1.0, 0.5 + charge * 0.45, 0.1, 0.95);
                    ctx.Fill();
                }

                // 50% Dunk Launch Threshold Notch
                ctx.MoveTo(barX + barWidth * 0.5, barY - 2);
                ctx.LineTo(barX + barWidth * 0.5, barY + barHeight + 2);
                ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.9);
                ctx.LineWidth = 2.0;
                ctx.Stroke();

                // Label
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                ctx.SetFontSize(12.0);
                if (charge >= 0.50f)
                {
                    ctx.SetSourceRGBA(1.0, 0.95, 0.2, 0.95);
                    ctx.MoveTo(cx - 60, barY - 6);
                    ctx.ShowText($"JUMP CHARGE: {(int)(charge * 100)}% [ DUNK READY ]");
                }
                else
                {
                    ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.85);
                    ctx.MoveTo(cx - 38, barY - 6);
                    ctx.ShowText($"JUMP CHARGE: {(int)(charge * 100)}%");
                }

                ctx.Restore();
            }
        }
    }
}
