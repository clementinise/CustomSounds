using System;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine.InputSystem;
using UnityEngine;
using CustomSounds.Networking;
using BepInEx.Configuration;

namespace CustomSounds.Patches
{
    [HarmonyPatch]
    public class RemapKeyPatch
    {
        public Harmony _harmony;
        public static InputActionAsset asset;
        static string defaultKey = "<Keyboard>/" + Plugin.AcceptSyncKey.Value;
        static string path = Application.persistentDataPath + "/AcceptSyncButton.txt";
        public static string actionName = "AcceptSyncAction";

        public static void UpdateInputActionAsset(string thing)
        {
            asset = InputActionAsset.FromJson(@"
            {
                ""maps"" : [
                    {
                        ""name"" : ""CustomSounds"",
                        ""actions"": [
                            {""name"": """ + actionName + @""", ""type"" : ""button""}
                        ],
                        ""bindings"" : [
                            {""path"" : """ + thing + @""", ""action"": """ + actionName + @"""}
                        ]
                    }
                ]
            }");
        }

        [HarmonyPatch(typeof(KepRemapPanel), "LoadKeybindsUI")]
        [HarmonyPrefix]
        public static void AddRemappableKey(KepRemapPanel __instance)
        {
            Debug.Log($"[CustomSounds] Default key for sync is {defaultKey}");

            for (int index1 = 0; index1 < __instance.remappableKeys.Count; ++index1)
            {
                if (__instance.remappableKeys[index1].ControlName == "Accept Sync") return;
            }

            RemappableKey fl = new RemappableKey();
            UpdateInputActionAsset(defaultKey);

            if (asset == null)
            {
                Debug.LogError("InputActionAsset is null.");
                return;
            }

            var action = asset.FindAction("CustomSounds/" + actionName);
            if (action == null)
            {
                Debug.LogError("Action 'CustomSounds/AcceptSyncAction' not found.");
                return;
            }

            InputActionReference inp = InputActionReference.Create(asset.FindAction("CustomSounds/" + actionName));
            fl.ControlName = "Accept Sync";
            fl.currentInput = inp;

            __instance.remappableKeys.Add(fl);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        [HarmonyPostfix]
        public static void ReadInput(PlayerControllerB __instance)
        {
            if ((!__instance.IsOwner || !__instance.isPlayerControlled || __instance.IsServer && !__instance.isHostPlayerObject) && !__instance.isTestingPlayer)
                return;
            if (!Application.isFocused) return;

            if (!asset || !asset.enabled) { UpdateInputActionAsset(defaultKey); asset.Enable(); }

            var action = asset.FindAction("CustomSounds/" + actionName);

            if (action != null && action.WasPressedThisFrame())
            {
                if (AudioNetworkHandler.isRequestingSync)
                {
                    Plugin.Instance.ShowCustomTip("CustomSounds Sync", "Sync request accepted successfully!", false);
                    AudioNetworkHandler.hasAcceptedSync = true;
                    AudioNetworkHandler.isRequestingSync = false;
                }
            }
        }

        [HarmonyPatch(typeof(IngamePlayerSettings), "CompleteRebind")]
        [HarmonyPrefix]
        public static void SavingActionRebind(IngamePlayerSettings __instance)
        {
            if (__instance.rebindingOperation.action.name != actionName) return;

            string bindingString = __instance.rebindingOperation.action.ToString();
            string keyString = bindingString.Split(new[] { '[', ']' })[1];
            string keyCodeString = keyString.Replace("/Keyboard/", "");

            defaultKey = keyString;

            try
            {
                KeyCode keyCode = (KeyCode)Enum.Parse(typeof(KeyCode), keyCodeString, true);

                Plugin.AcceptSyncKey.Value = new KeyboardShortcut(keyCode);
            }
            catch (Exception ex)
            {
                Debug.LogError("Erreur lors de la conversion de la chaîne en KeyCode: " + ex.Message);
            }

            UpdateInputActionAsset(defaultKey);
        }


        [HarmonyPatch(typeof(KepRemapPanel), "ResetKeybindsUI")]
        [HarmonyPrefix]
        public static void ResetAcceptAction(KepRemapPanel __instance)
        {
            Plugin.AcceptSyncKey.Value = new KeyboardShortcut(KeyCode.F8);

            defaultKey = "<Keyboard>/f8";
            UpdateInputActionAsset(defaultKey);

            bool keyFound = UpdateAcceptSyncKeybind(__instance, "<Keyboard>/f8");
        }

        static bool UpdateAcceptSyncKeybind(KepRemapPanel panel, string defaultBinding)
        {
            foreach (var key in panel.remappableKeys)
            {
                if (key.ControlName == "Accept Sync")
                {
                    key.currentInput = InputActionReference.Create(asset.FindAction("CustomSounds/AcceptSyncAction"));
                    Debug.Log("[CustomSounds] Found and updated 'Accept Sync' keybinding");
                    return true;
                }
            }
            Debug.Log("[CustomSounds] 'Accept Sync' keybinding not found");
            return false;
        }
    }
}