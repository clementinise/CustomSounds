using BepInEx;
using BepInEx.Logging;
using LCSoundTool;
using UnityEngine;
using System.IO;
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using CustomSoundsComponents;
using Object = UnityEngine.Object;
using HarmonyLib.Tools;
using TerminalApi;
using static UnityEngine.Rendering.ReloadAttribute;
using BepInEx.Configuration;

namespace CustomSounds
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const string PLUGIN_GUID = "CustomSounds";
        private const string PLUGIN_NAME = "Custom Sounds";
        private const string PLUGIN_VERSION = "1.3.0";

        public static Plugin Instance;
        internal ManualLogSource logger;
        private Harmony harmony;

        public HashSet<string> currentSounds = new HashSet<string>();
        public HashSet<string> oldSounds = new HashSet<string>();
        public HashSet<string> modifiedSounds = new HashSet<string>();
        public Dictionary<string, string> soundHashes = new Dictionary<string, string>();
        public Dictionary<string, string> soundPacks = new Dictionary<string, string>();
        public static bool Initialized { get; private set; }

        public static ConfigEntry<KeyboardShortcut> AcceptSyncKey;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                logger = BepInEx.Logging.Logger.CreateLogSource(PLUGIN_GUID);
                logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");

                harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll();

                AcceptSyncKey = Config.Bind("General", "AcceptSyncKey", new KeyboardShortcut(KeyCode.F8), "Key to accept audio sync.");

                modifiedSounds = new HashSet<string>();

                string customSoundsPath = GetCustomSoundsFolderPath();
                if (!Directory.Exists(customSoundsPath))
                {
                    logger.LogInfo("\"CustomSounds\" folder not found. Creating it now.");
                    Directory.CreateDirectory(customSoundsPath);
                }

                string configPath = Path.Combine(Paths.BepInExConfigPath);

                try
                {
                    var lines = File.ReadAllLines(configPath).ToList();

                    int index = lines.FindIndex(line => line.StartsWith("HideManagerGameObject"));
                    if (index != -1)
                    {
                        logger.LogInfo("\"hideManagerGameObject\" value not correctly set. Fixing it now.");
                        lines[index] = "HideManagerGameObject = true";
                    }

                    File.WriteAllLines(configPath, lines);
                }
                catch (Exception e)
                {
                    logger.LogError($"Error modifying config file: {e.Message}");
                }

                var types = Assembly.GetExecutingAssembly().GetTypes();
                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var method in methods)
                    {
                        var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                        if (attributes.Length > 0)
                        {
                            method.Invoke(null, null);
                        }
                    }
                }
            }
        }


        internal void Start() => this.Initialize();

        internal void OnDestroy() => this.Initialize();

        internal void Initialize()
        {
            if (Plugin.Initialized)
                return;
            Plugin.Initialized = true;

            DeleteTempFolder();

            ReloadSounds(false, false);
        }

        private void OnApplicationQuit()
        {
            DeleteTempFolder();
        }

        public void DeleteTempFolder()
        {
            string customSoundsPath = GetCustomSoundsTempFolderPath();
            if (Directory.Exists(customSoundsPath))
            {
                try
                {
                    Directory.Delete(customSoundsPath, true);
                    logger.LogInfo("Temporary-Sync folder deleted successfully.");
                }
                catch (Exception e)
                {
                    logger.LogError($"Error deleting Temporary-Sync folder: {e.Message}");
                }
            }
        }

        public string GetCustomSoundsFolderPath()
        {
            return Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "CustomSounds");
        }

        public string GetCustomSoundsTempFolderPath()
        {
            return Path.Combine(GetCustomSoundsFolderPath(), "Temp");
        }

        public static byte[] SerializeWavToBytes(string filePath)
        {
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
                return null;
            }
        }

        public static string SerializeWavToBase64(string filePath)
        {
            try
            {
                byte[] audioBytes = File.ReadAllBytes(filePath);
                return Convert.ToBase64String(audioBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
                return null;
            }
        }


        public static void DeserializeBytesToWav(byte[] byteArray, string audioFileName)
        {
            try
            {
                string customSoundsPath = Plugin.Instance.GetCustomSoundsTempFolderPath();
                string tempSyncPath = Path.Combine(customSoundsPath, "CustomSounds");
                string tempSyncPath2 = Path.Combine(tempSyncPath, "Temporary-Sync");
                if (!Directory.Exists(tempSyncPath2))
                {
                    Plugin.Instance.logger.LogInfo("\"Temporary-Sync\" folder not found. Creating it now.");
                    Directory.CreateDirectory(tempSyncPath2);
                }
                File.WriteAllBytes(Path.Combine(tempSyncPath2, audioFileName), byteArray);

                Console.WriteLine($"WAV file \"{audioFileName}\" created!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }
        }

        public static List<byte[]> SplitAudioData(byte[] audioData, int maxSegmentSize = 62000)
        {
            var segments = new List<byte[]>();
            for (int i = 0; i < audioData.Length; i += maxSegmentSize)
            {
                int segmentSize = Mathf.Min(maxSegmentSize, audioData.Length - i);
                byte[] segment = new byte[segmentSize];
                System.Array.Copy(audioData, i, segment, 0, segmentSize);
                segments.Add(segment);
            }
            return segments;
        }

        public static byte[] CombineAudioSegments(List<byte[]> segments)
        {
            var combinedData = new List<byte>();
            foreach (var segment in segments)
            {
                combinedData.AddRange(segment);
            }
            return combinedData.ToArray();
        }

        public void ShowCustomTip(string header, string body, bool isWarning)
        {
            HUDManager.Instance.DisplayTip(header, body, isWarning);
        }

        public void ForceUnsync()
        {
            DeleteTempFolder();
            ReloadSounds(false, false);
        }

        public void RevertSounds()
        {
            foreach (string soundName in currentSounds)
            {
                logger.LogInfo($"{soundName} restored.");
                SoundTool.RestoreAudioClip(soundName);
            }

            logger.LogInfo("Original game sounds restored.");
        }
        public static string CalculateMD5(string filename)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public void ReloadSounds(bool serverSync, bool isTemporarySync)
        {
            foreach (string soundName in currentSounds)
            {
                SoundTool.RestoreAudioClip(soundName);
            }

            oldSounds = new HashSet<string>(currentSounds);
            modifiedSounds.Clear();

            string pluginPath = Path.GetDirectoryName(Paths.PluginPath);
            currentSounds.Clear();


            string customSoundsPath = GetCustomSoundsTempFolderPath();

            logger.LogInfo($"Temporary folder: {customSoundsPath}");
            if (Directory.Exists(customSoundsPath) && isTemporarySync)
            {
                ProcessDirectory(customSoundsPath, serverSync, true);
            }

            ProcessDirectory(pluginPath, serverSync, false);
        }

        private void ProcessDirectory(string directoryPath, bool serverSync, bool isTemporarySync)
        {
            foreach (var subDirectory in Directory.GetDirectories(directoryPath, "CustomSounds", SearchOption.AllDirectories))
            {
                string packName = Path.GetFileName(Path.GetDirectoryName(subDirectory));
                ProcessSoundFiles(subDirectory, packName, serverSync, isTemporarySync);

                foreach (var subDirectory2 in Directory.GetDirectories(subDirectory))
                {
                    string packName2 = Path.GetFileName(subDirectory2);
                    ProcessSoundFiles(subDirectory2, packName2, serverSync, isTemporarySync);
                }
            }
        }

        private void ProcessSoundFiles(string directoryPath, string packName, bool serverSync, bool isTemporarySync)
        {
            foreach (string file in Directory.GetFiles(directoryPath, "*.wav"))
            {
                string soundName = Path.GetFileNameWithoutExtension(file);
                string newHash = CalculateMD5(file);

                if (!isTemporarySync && modifiedSounds.Contains(soundName)) return;

                if (soundHashes.TryGetValue(soundName, out var oldHash) && oldHash != newHash)
                {
                    modifiedSounds.Add(soundName);
                }

                AudioClip customSound = SoundTool.GetAudioClip(directoryPath, "", file);
                SoundTool.ReplaceAudioClip(soundName, customSound);

                soundHashes[soundName] = newHash;
                currentSounds.Add(soundName);
                soundPacks[soundName] = packName;
                logger.LogInfo($"[{packName}] {soundName} sound replaced!");

                if (serverSync)
                {
                    logger.LogInfo($"[{Path.Combine(directoryPath, soundName + ".wav")}] {soundName + ".wav"}!");
                    AudioNetworkHandler.Instance.QueueAudioData(Plugin.SerializeWavToBytes(Path.Combine(directoryPath, soundName + ".wav")), soundName + ".wav");
                }
            }
        }

        public string GetSoundChanges()
        {
            StringBuilder sb = new StringBuilder("Customsounds reloaded.\n\n");

            var newSoundsSet = new HashSet<string>(currentSounds.Except(oldSounds));
            var deletedSoundsSet = new HashSet<string>(oldSounds.Except(currentSounds));
            var existingSoundsSet = new HashSet<string>(oldSounds.Intersect(currentSounds).Except(modifiedSounds));
            var modifiedSoundsSet = new HashSet<string>(modifiedSounds);

            var soundsByPack = new Dictionary<string, List<string>>();

            Action<HashSet<string>, string> addSoundsToPack = (soundsSet, status) =>
            {
                foreach (var sound in soundsSet)
                {
                    string packName = soundPacks[sound];
                    if (!soundsByPack.ContainsKey(packName))
                    {
                        soundsByPack[packName] = new List<string>();
                    }
                    soundsByPack[packName].Add($"{sound} ({status})");
                }
            };

            addSoundsToPack(newSoundsSet, "New");
            addSoundsToPack(deletedSoundsSet, "Deleted");
            addSoundsToPack(modifiedSoundsSet, "Modified");
            addSoundsToPack(existingSoundsSet, "Already Existed");

            foreach (var pack in soundsByPack.Keys)
            {
                sb.AppendLine($"{pack} :");
                foreach (var sound in soundsByPack[pack])
                {
                    sb.AppendLine($"- {sound}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string ListAllSounds()
        {
            StringBuilder sb = new StringBuilder("Listing all currently loaded custom sounds:\n\n");

            var soundsByPack = new Dictionary<string, List<string>>();

            foreach (var sound in currentSounds)
            {
                string packName = soundPacks[sound];
                if (!soundsByPack.ContainsKey(packName))
                {
                    soundsByPack[packName] = new List<string>();
                }
                soundsByPack[packName].Add(sound);
            }

            foreach (var pack in soundsByPack.Keys)
            {
                sb.AppendLine($"{pack} :");
                foreach (var sound in soundsByPack[pack])
                {
                    sb.AppendLine($"- {sound}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

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
            if (commandWords.Length == 0 || commandWords[0] != "customsounds")
            {
                return true;
            }

            Plugin.Instance.logger.LogInfo($"Received terminal command: {string.Join(" ", commandWords)}");

            if (commandWords.Length > 1 && commandWords[0] == "customsounds")
            {
                switch (commandWords[1])
                {
                    case "reload":
                        Plugin.Instance.ReloadSounds(false, false);
                        __result = CreateTerminalNode(Plugin.Instance.GetSoundChanges());
                        return false;

                    case "revert":
                        Plugin.Instance.RevertSounds();
                        __result = CreateTerminalNode("Game sounds reverted to original.");
                        return false;

                    case "list":
                        __result = CreateTerminalNode(Plugin.Instance.ListAllSounds());
                        return false;

                    case "help":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("CustomSounds commands.\n\n>CUSTOMSOUNDS LIST\nTo displays all currently loaded sounds\n\n>CUSTOMSOUNDS RELOAD\nTo reloads and applies sounds from the 'CustomSounds' folder and its subfolders.\n\n>CUSTOMSOUNDS REVERT\nTo unloads all custom sounds and restores original game sounds\n\n>CUSTOMSOUNDS SYNC\nStarts the sync of custom sounds with clients\n\n>CUSTOMSOUNDS FORCE-UNSYNC\nForces the unsync process for all clients");
                        }
                        else
                        {
                            __result = CreateTerminalNode("CustomSounds commands.\n\n>CUSTOMSOUNDS LIST\nTo displays all currently loaded sounds\n\n>CUSTOMSOUNDS RELOAD\nTo reloads and applies sounds from the 'CustomSounds' folder and its subfolders.\n\n>CUSTOMSOUNDS REVERT\nTo unloads all custom sounds and restores original game sounds\n\n>CUSTOMSOUNDS UNSYNC\nUnsyncs sounds sent by the host.");
                        }
                        return false;

                    case "sync":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("Custom sound sync initiated. \nSyncing sounds with clients...");
                            Plugin.Instance.ReloadSounds(true, false);
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command can only be used by the host!");
                        }
                        return false;

                    case "unsync":
                        if (!NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("Unsyncing custom sounds. \nTemporary files deleted and original sounds reloaded.");

                            Plugin.Instance.DeleteTempFolder();

                            Plugin.Instance.ReloadSounds(false, false);
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command cannot be used by the host!");
                        }
                        return false;

                    case "force-unsync":
                        if (NetworkManager.Singleton.IsHost)
                        {
                            __result = CreateTerminalNode("Forcing unsync for all clients. \nAll client-side temporary synced files have been deleted, and original sounds reloaded.");
                            AudioNetworkHandler.Instance.ForceUnsync();
                        }
                        else
                        {
                            __result = CreateTerminalNode("/!\\ ERROR /!\\ \nThis command can only be used by the host!");
                        }
                        return false;


                    default:
                        __result = CreateTerminalNode("Unknown customsounds command.");
                        return false;
                }
            }

            return true;
        }

        private static TerminalNode CreateTerminalNode(string message)
        {
            return new TerminalNode
            {
                displayText = message,
                clearPreviousText = true
            };
        }
    }


    [HarmonyPatch]
    public class NetworkObjectManager
    {

        [HarmonyPatch(typeof(GameNetworkManager), "Start")]
        [HarmonyPrefix]
        public static void Init()
        {
            if (networkPrefab != null) return;
            var bundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "audionetworkhandler"));
            networkPrefab = bundle.LoadAsset<GameObject>("assets/audionetworkhandler.prefab");

            networkPrefab.AddComponent<AudioNetworkHandler>();

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
            Plugin.Instance.logger.LogInfo("Created AudioNetworkHandler prefab");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), "Awake")]
        static void SpawnNetworkHandler()
        {
            try
            {
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    Plugin.Instance.logger.LogInfo("Spawning network handler");
                    networkHandlerHost = Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
                    if (networkHandlerHost.GetComponent<NetworkObject>().IsSpawned)
                    {
                        Debug.Log("NetworkObject is spawned and active.");
                    }
                    else
                    {
                        Debug.Log("Failed to spawn NetworkObject.");
                    }

                    networkHandlerHost.GetComponent<NetworkObject>().Spawn(true);

                    if (AudioNetworkHandler.Instance != null)
                    {
                        Debug.Log("Successfully accessed AudioNetworkHandler instance.");
                    }
                    else
                    {
                        Debug.Log("AudioNetworkHandler instance is null.");
                    }

                }
            }
            catch
            {
                Plugin.Instance.logger.LogError("Failed to spawned network handler");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        static void DestroyNetworkHandler()
        {
            try
            {
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    Plugin.Instance.logger.LogInfo("Destroying network handler");
                    Object.Destroy(networkHandlerHost);
                    networkHandlerHost = null;
                }
            }
            catch
            {
                Plugin.Instance.logger.LogError("Failed to destroy network handler");
            }
        }

        static GameObject networkPrefab;
        static GameObject networkHandlerHost;
    }
}