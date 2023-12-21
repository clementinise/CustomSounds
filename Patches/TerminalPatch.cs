using System.Linq;
using CustomSounds.Networking;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace CustomSounds.Patches
{
    [HarmonyPatch(typeof(Terminal), "ParsePlayerSentence")]
    public static class TerminalParsePlayerSentencePatch
    {
        public static bool Prefix(Terminal __instance, ref TerminalNode __result)
        {
            string[] inputLines = __instance.screenText.text.Split('\n');
            if (inputLines.Length == 0)
            {
                return true;
            }

            string[] commandWords = inputLines.Last().Trim().ToLower().Split(' ');
            if (commandWords.Length == 0 || (commandWords[0] != "customsounds" && commandWords[0] != "cs"))
            {
                return true;
            }

            Plugin.Instance.logger.LogInfo($"Received terminal command: {string.Join(" ", commandWords)}");

            if (commandWords.Length > 1 && (commandWords[0] == "customsounds" || commandWords[0] == "cs"))
            {
                switch (commandWords[1])
                {
                    case "reload":
                    case "rl":
                        Plugin.Instance.RevertSounds();
                        Plugin.Instance.ReloadSounds(false, false);
                        __result = CreateTerminalNode(Plugin.Instance.ListAllSounds(false));
                        return false;

                    case "revert":
                    case "rv":
                        Plugin.Instance.RevertSounds();
                        __result = CreateTerminalNode("Game sounds reverted to original.\n\n");
                        return false;

                    case "list":
                    case "l":
                        __result = CreateTerminalNode(Plugin.Instance.ListAllSounds(true));
                        return false;

                    case "help":
                    case "h":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode(
                                "CustomSounds commands \n(Can also be used with 'CS' as an alias).\n\n" +
                                ">CUSTOMSOUNDS LIST/L\nTo display all currently loaded sounds\n\n" +
                                ">CUSTOMSOUNDS RELOAD/RL\nTo reload and apply sounds from the 'CustomSounds' folder and its subfolders.\n\n" +
                                ">CUSTOMSOUNDS REVERT/RV\nTo unload all custom sounds and restore original game sounds\n\n" +
                                ">CUSTOMSOUNDS SYNC/S\nTo start the sync of custom sounds with clients\n\n" +
                                ">CUSTOMSOUNDS FORCE-UNSYNC/FU\nTo force the unsync process for all clients\n\n"
                            );
                        }
                        else
                        {
                            __result = CreateTerminalNode(
                                "CustomSounds commands \n(Can also be used with 'CS' as an alias).\n\n" +
                                ">CUSTOMSOUNDS LIST/L\nTo display all currently loaded sounds\n\n" +
                                ">CUSTOMSOUNDS RELOAD/RL\nTo reload and apply sounds from the 'CustomSounds' folder and its subfolders.\n\n" +
                                ">CUSTOMSOUNDS REVERT/RV\nTo unload all custom sounds and restore original game sounds\n\n" +
                                ">CUSTOMSOUNDS UNSYNC/U\nUnsyncs sounds sent by the host.\n\n"
                            );
                        }
                        return false;

                    case "sync":
                    case "s":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            if (Plugin.Instance.configUseNetworking.Value)
                            {
                                __result = CreateTerminalNode("Custom sound sync initiated. \nSyncing sounds with clients...\n\n");
                                Plugin.Instance.ReloadSounds(true, false);
                            }
                            else
                            {
                                __result = CreateTerminalNode("Custom sound sync is currently disabled. \nPlease enable network support in the plugin config to use this feature.\n\n");
                            }
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command can only be used by the host!\n\n");
                        }
                        return false;

                    case "unsync":
                    case "u":
                        if (!NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("Unsyncing custom sounds. \nTemporary files deleted and original sounds reloaded.\n\n");

                            Plugin.Instance.DeleteTempFolder();

                            Plugin.Instance.ReloadSounds(false, false);
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command cannot be used by the host!\n\n");
                        }
                        return false;

                    case "force-unsync":
                    case "fu":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("Forcing unsync for all clients. \nAll client-side temporary synced files have been deleted, and original sounds reloaded.\n\n");
                            AudioNetworkHandler.Instance.ForceUnsync();
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command can only be used by the host!\n\n");
                        }
                        return false;


                    default:
                        __result = CreateTerminalNode("Unknown customsounds command.\n\n");
                        return false;
                }
            }

            return true;
        }

        private static TerminalNode CreateTerminalNode(string message)
        {
            TerminalNode terminalNode = ScriptableObject.CreateInstance<TerminalNode>();
            terminalNode.displayText = message;
            terminalNode.clearPreviousText = true;
            return terminalNode;
        }
    }
}
