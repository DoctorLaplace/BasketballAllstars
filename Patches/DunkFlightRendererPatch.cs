using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using BasketballAllstars.Systems;

namespace BasketballAllstars.Patches
{
    /// <summary>
    /// Harmony patches on EntityBehaviorPlayerPhysics, PlayerHeadController, and ModSystemGliding
    /// to maintain authoritative flight orientation (yaw, pitch, roll) and prevent 
    /// the vanilla engine physics and standing IK from zeroing or distorting 
    /// the player model during ridiculous slam dunks (360 spins, front flips, tomahawk slams).
    /// </summary>
    public static class DunkFlightRendererPatch
    {
        public static void InitClientPatches(Harmony harmony, ICoreClientAPI capi)
        {
            try
            {
                // 1. Patch EntityBehaviorPlayerPhysics.SetPlayerControls
                var physicsType = typeof(Vintagestory.GameContent.EntityBehaviorPlayerPhysics);
                var setControlsMethod = physicsType?.GetMethod("SetPlayerControls", BindingFlags.Public | BindingFlags.Instance);
                var physicsPostfix = typeof(DunkFlightRendererPatch).GetMethod(nameof(SetPlayerControlsPostfix), BindingFlags.Public | BindingFlags.Static);

                if (setControlsMethod != null && physicsPostfix != null)
                {
                    harmony.Patch(setControlsMethod, postfix: new HarmonyMethod(physicsPostfix));
                }
                else
                {
                    capi.Logger.Warning("[BasketballAllstars] Could not find EntityBehaviorPlayerPhysics.SetPlayerControls for dunk patch.");
                }

                // 2. Patch PlayerHeadController.AdjustHeadAngles
                var headType = typeof(PlayerHeadController);
                var adjustHeadMethod = headType?.GetMethod("AdjustHeadAngles", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var headPrefix = typeof(DunkFlightRendererPatch).GetMethod(nameof(AdjustHeadAnglesPrefix), BindingFlags.Public | BindingFlags.Static);

                if (adjustHeadMethod != null && headPrefix != null)
                {
                    harmony.Patch(adjustHeadMethod, prefix: new HarmonyMethod(headPrefix));
                }
                else
                {
                    capi.Logger.Warning("[BasketballAllstars] Could not find PlayerHeadController.AdjustHeadAngles for dunk patch.");
                }

                // 3. Patch PlayerHeadController.AdjustBodyAngles
                var adjustBodyMethod = headType?.GetMethod("AdjustBodyAngles", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var bodyPrefix = typeof(DunkFlightRendererPatch).GetMethod(nameof(AdjustBodyAnglesPrefix), BindingFlags.Public | BindingFlags.Static);

                if (adjustBodyMethod != null && bodyPrefix != null)
                {
                    harmony.Patch(adjustBodyMethod, prefix: new HarmonyMethod(bodyPrefix));
                }

                // 4. Patch ModSystemGliding.onClientTick to prevent vanilla glider item check from zeroing WalkPitch
                var gliderType = typeof(Vintagestory.GameContent.ModSystemGliding);
                var gliderTickMethod = gliderType?.GetMethod("onClientTick", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var gliderPrefix = typeof(DunkFlightRendererPatch).GetMethod(nameof(ModSystemGliding_onClientTick_Prefix), BindingFlags.Public | BindingFlags.Static);

                if (gliderTickMethod != null && gliderPrefix != null)
                {
                    harmony.Patch(gliderTickMethod, prefix: new HarmonyMethod(gliderPrefix));
                }
                // 5. Patch EntityPlayerShapeRenderer.loadModelMatrixForPlayer to apply authoritative dunk rotation and bypass yaw clamp
                var rendererType = typeof(Vintagestory.GameContent.EntityPlayerShapeRenderer);
                var loadModelMatrixMethod = rendererType?.GetMethod("loadModelMatrixForPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var loadModelMatrixPrefix = typeof(DunkFlightRendererPatch).GetMethod(nameof(LoadModelMatrixForPlayerPrefix), BindingFlags.Public | BindingFlags.Static);

                if (loadModelMatrixMethod != null && loadModelMatrixPrefix != null)
                {
                    harmony.Patch(loadModelMatrixMethod, prefix: new HarmonyMethod(loadModelMatrixPrefix));
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[BasketballAllstars] Failed to apply DunkFlightRendererPatch: {0}", ex);
            }
        }

        public static void LoadModelMatrixForPlayerPrefix(Vintagestory.GameContent.EntityPlayerShapeRenderer __instance, Entity entity)
        {
            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return;

            if (entity is EntityPlayer entityPlayer && dunkSystem.IsPlayerInTrajectory(entityPlayer.PlayerUID, out var traj))
            {
                dunkSystem.ApplyDunkStyleRotation(entityPlayer, traj);

                // Set bodyYawLerped and smoothedBodyYaw directly so fast spins/somersaults render without clamping
                var lerpField = typeof(Vintagestory.GameContent.EntityPlayerShapeRenderer).GetField("bodyYawLerped", BindingFlags.NonPublic | BindingFlags.Instance);
                lerpField?.SetValue(__instance, entityPlayer.BodyYaw);
                var smoothField = typeof(Vintagestory.GameContent.EntityPlayerShapeRenderer).GetField("smoothedBodyYaw", BindingFlags.NonPublic | BindingFlags.Instance);
                smoothField?.SetValue(__instance, entityPlayer.BodyYaw);
            }
        }

        public static bool ModSystemGliding_onClientTick_Prefix(float dt)
        {
            if (DunkTrajectorySystem.ClientInstance != null && DunkTrajectorySystem.ClientInstance.HasActiveClientTrajectory)
            {
                // Suppress vanilla ModSystemGliding from zeroing WalkPitch while in dunk flight
                return false;
            }
            return true;
        }

        public static void SetPlayerControlsPostfix(Vintagestory.GameContent.EntityBehaviorPlayerPhysics __instance, EntityPos pos, EntityControls controls, float dt)
        {
            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return;

            if (__instance.entity is EntityPlayer entityPlayer)
            {
                if (dunkSystem.IsPlayerInTrajectory(entityPlayer.PlayerUID, out var traj))
                {
                    controls.IsFlying = true;
                    controls.Gliding = true;
                    controls.Forward = false;
                    controls.Backward = false;
                    controls.Left = false;
                    controls.Right = false;
                    controls.Sprint = false;
                    controls.Jump = false;

                    // Apply style rotation (360 spin, front flip, or tomahawk slam)
                    dunkSystem.ApplyDunkStyleRotation(entityPlayer, traj);
                }
                else if (dunkSystem.ClientIsChargingJump && entityPlayer.PlayerUID == dunkSystem.LocalPlayerUid)
                {
                    // Suppress vanilla normal jump while charging super jump / slam dunk to prevent scrunching/hovering
                    controls.Jump = false;
                }
                else if ((dunkSystem.SuppressJumpUntilRelease || dunkSystem.WasSpaceHeld) && entityPlayer.PlayerUID == dunkSystem.LocalPlayerUid)
                {
                    controls.Jump = false;
                }
            }
        }

        public static bool AdjustHeadAnglesPrefix(PlayerHeadController __instance, EnumCameraMode cameraMode, float dt)
        {
            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return true;

            var entityField = typeof(PlayerHeadController).GetField("entityPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var entityPlayer = entityField?.GetValue(__instance) as EntityPlayer;
            if (entityPlayer != null && dunkSystem.IsPlayerInTrajectory(entityPlayer.PlayerUID, out _))
            {
                entityPlayer.Pos.HeadPitch = 0f;
                entityPlayer.Pos.HeadYaw = 0f;
                return false; // Skip standing head IK during dunk flight so neck stays aligned with body
            }
            return true;
        }

        public static bool AdjustBodyAnglesPrefix(PlayerHeadController __instance, float dt)
        {
            var dunkSystem = DunkTrajectorySystem.ClientInstance;
            if (dunkSystem == null) return true;

            var entityField = typeof(PlayerHeadController).GetField("entityPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var entityPlayer = entityField?.GetValue(__instance) as EntityPlayer;
            if (entityPlayer != null && dunkSystem.IsPlayerInTrajectory(entityPlayer.PlayerUID, out _))
            {
                // Keep body rotation controlled by trajectory system
                return false;
            }
            return true;
        }
    }
}
