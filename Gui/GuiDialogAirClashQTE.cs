using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using BasketballAllstars.Network;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Gui
{
    /// <summary>
    /// Sleek, minimalist mid-air clash QTE display located halfway between top and center of the screen.
    /// Displays the 10-arrow sequence in a smoothly sliding horizontal strip where
    /// the active arrow is in the center, completed arrows slide left and turn green,
    /// making a single mistake immediately fails the clash, and the camera orbits
    /// smoothly from above during the clash and returns to normal when resolved.
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

        private float originalMouseYaw = 0f;
        private float originalMousePitch = 0f;
        private bool cameraOverridden = false;
        private bool switchedToThirdPerson = false;
        private float originalCameraOffsetZ = 0f;
        private float originalCameraOffsetY = 0f;

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
            if (capi.World.Player != null)
            {
                originalMouseYaw = capi.Input.MouseYaw;
                originalMousePitch = capi.Input.MousePitch;
                capi.Input.MousePitch = 0.55f; // Angled down from above (positive pitch tilts camera down looking from above)
                cameraOverridden = true;

                // If currently in FirstPerson, switch to 3rd person perspective
                if (capi.World.Player.CameraMode == EnumCameraMode.FirstPerson)
                {
                    foreach (var key in new[] { "perspective", "toggleperspective", "cameramode", "cyclecamera", "viewmode" })
                    {
                        if (capi.Input.HotKeys.TryGetValue(key, out var hk) && hk?.Handler != null)
                        {
                            hk.Handler(hk.CurrentMapping);
                            switchedToThirdPerson = true;
                            break;
                        }
                    }
                }
            }

            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ClearComposers();

            double width = 760;
            double height = 80;

            // Halfway between top (0%) and middle (50%) of screen = 25% down the screen
            double topOffset = Math.Max(capi.Gui.WindowBounds.InnerHeight * 0.25 - 40, 120);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterTop)
                .WithFixedAlignmentOffset(0, topOffset);

            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, width, height);

            var composer = capi.Gui.CreateCompo("airClashStrip", dialogBounds)
                .BeginChildElements(bgBounds)
                .AddDynamicCustomDraw(ElementBounds.Fixed(0, 0, width, height), DrawArrowStrip, "arrowCanvas")
                .EndChildElements();

            SingleComposer = composer;
            SingleComposer.Compose();
        }

        private static (double r, double g, double b) GetArrowColor(byte dir)
        {
            return dir switch
            {
                0 => (1.0, 0.22, 0.22),  // Up: Red
                1 => (1.0, 0.90, 0.15),  // Right: Yellow
                2 => (0.20, 0.60, 1.0),  // Down: Blue
                3 => (0.20, 0.95, 0.35), // Left: Green
                _ => (1.0, 1.0, 1.0)
            };
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

                var (dirR, dirG, dirB) = GetArrowColor(dir);

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
                        // Active Current Arrow: Glowing in its assigned color in center
                        ctx.Save();
                        ctx.Arc(slotX, centerY, 26.0, 0, Math.PI * 2.0);
                        ctx.SetSourceRGBA(dirR, dirG, dirB, 0.30);
                        ctx.Fill();
                        ctx.Restore();

                        DrawArrowIcon(ctx, slotX, centerY, dir, 1.28, dirR, dirG, dirB, 1.0);
                    }
                }
                else
                {
                    // Queued Upcoming Arrows: High visibility in assigned directional color
                    DrawArrowIcon(ctx, slotX, centerY, dir, 1.05, dirR, dirG, dirB, 0.85);
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

                // Play arrowswipe sound when inputting a quicktime arrow
                BasketballAudioParticles.PlayArrowSwipeSound(capi);

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

                    // Play random parry hit sound for immediate local responsive feedback
                    if (capi.World.Player?.Entity != null)
                    {
                        BasketballAudioParticles.PlayParryHitSound(capi.World, capi.World.Player.Entity.Pos.XYZ);
                    }

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

            // Orbit camera smoothly from an angle above the clash while active
            if (cameraOverridden && !isFinished)
            {
                capi.Input.MouseYaw += deltaTime * 1.5f;
                capi.Input.MousePitch = 0.55f;
            }

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
            // Ensure clash looping audio is halted
            BasketballAudioParticles.StopClashLoopingSounds();

            // Restore original camera angles, offset, and perspective
            if (cameraOverridden && capi.World.Player != null)
            {
                capi.Input.MouseYaw = originalMouseYaw;
                capi.Input.MousePitch = originalMousePitch;

                if (capi.Render?.CameraOffset != null)
                {
                    capi.Render.CameraOffset.Translation.Z = originalCameraOffsetZ;
                    capi.Render.CameraOffset.Translation.Y = originalCameraOffsetY;
                }

                if (switchedToThirdPerson)
                {
                    foreach (var key in new[] { "perspective", "toggleperspective", "cameramode", "cyclecamera", "viewmode" })
                    {
                        if (capi.Input.HotKeys.TryGetValue(key, out var hk) && hk?.Handler != null)
                        {
                            int safety = 0;
                            while (capi.World.Player.CameraMode != EnumCameraMode.FirstPerson && safety++ < 4)
                            {
                                hk.Handler(hk.CurrentMapping);
                            }
                            break;
                        }
                    }
                    switchedToThirdPerson = false;
                }

                cameraOverridden = false;
            }

            base.OnGuiClosed();
            Instance = null;
        }
    }
}
