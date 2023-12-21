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
using CustomSounds.Networking;
using BepInEx.Configuration;
using CustomSounds.Patches;

namespace CustomSounds
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const string PLUGIN_GUID = "CustomSounds";
        private const string PLUGIN_NAME = "Custom Sounds";
        private const string PLUGIN_VERSION = "2.2.0";

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

        public ConfigEntry<bool> configUseNetworking;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                logger = BepInEx.Logging.Logger.CreateLogSource(PLUGIN_GUID);

                configUseNetworking = Config.Bind("Experimental", "EnableNetworking", true, "Whether or not to use the networking built into this plugin. If set to true everyone in the lobby needs CustomSounds to join and also \"EnableNetworking\" set to true.");
                AcceptSyncKey = Config.Bind("Experimental", "AcceptSyncKey", new KeyboardShortcut(KeyCode.F8), "Key to accept audio sync.");

                harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll(typeof(TerminalParsePlayerSentencePatch));
                harmony.PatchAll(typeof(RemapKeyPatch));
                if (configUseNetworking.Value)
                {
                    harmony.PatchAll(typeof(NetworkObjectManager));
                }

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

                logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");
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

        public GameObject LoadNetworkPrefabFromEmbeddedResource()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = "CustomSounds.Bundle.audionetworkhandler";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Debug.LogError("Asset bundle not found in embedded resources.");
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                AssetBundle bundle = AssetBundle.LoadFromMemory(buffer);
                if (bundle == null)
                {
                    Debug.LogError("Failed to load AssetBundle from memory.");
                    return null;
                }

                GameObject prefab = bundle.LoadAsset<GameObject>("audionetworkhandler");
                return prefab;
            }
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
            return Path.Combine(GetCustomSoundsFolderPath(), "Temporary-Sync");
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
                string customTempSoundsPath = Plugin.Instance.GetCustomSoundsTempFolderPath();
                if (!Directory.Exists(customTempSoundsPath))
                {
                    Plugin.Instance.logger.LogInfo("\"Temporary-Sync\" folder not found. Creating it now.");
                    Directory.CreateDirectory(customTempSoundsPath);
                }
                File.WriteAllBytes(Path.Combine(customTempSoundsPath, audioFileName), byteArray);

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
            HashSet<string> restoredSounds = new HashSet<string>();

            foreach (string soundName in currentSounds)
            {
                string correctSoundName = soundName;
                if (soundName.Contains("-"))
                    correctSoundName = soundName.Substring(0, soundName.IndexOf("-"));

                if (!restoredSounds.Contains(correctSoundName))
                {
                    logger.LogInfo($"{correctSoundName} restored.");
                    SoundTool.RestoreAudioClip(correctSoundName);
                    restoredSounds.Add(correctSoundName);
                }
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

        private Dictionary<string, string> customSoundNames = new Dictionary<string, string>();

        public void ReloadSounds(bool serverSync, bool isTemporarySync)
        {
            oldSounds = new HashSet<string>(currentSounds);
            modifiedSounds.Clear();

            string pluginPath = Path.GetDirectoryName(Paths.PluginPath);
            currentSounds.Clear();

            customSoundNames.Clear();

            string customSoundsPath = GetCustomSoundsTempFolderPath();

            if (isTemporarySync)
            {
                logger.LogInfo($"Temporary folder: {customSoundsPath}");
                if (Directory.Exists(customSoundsPath))
                {
                    ProcessDirectory(customSoundsPath, serverSync, true);
                }
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

        private (string soundName, int? percentage, string customName) ParseSoundFileName(string fullSoundName)
        {
            string[] splitName = fullSoundName.Split('-');
            string lastElement = splitName[splitName.Length - 1].Replace(".wav", "");

            if (int.TryParse(lastElement, out int percentage))
            {
                string soundName = splitName[0];
                string customName = string.Join(" ", splitName.Skip(1).Take(splitName.Length - 2));
                return (soundName, percentage, customName);
            }
            else
            {
                return (splitName[0], null, string.Join(" ", splitName.Skip(1)).Replace(".wav", ""));
            }
        }

        private void ProcessSoundFiles(string directoryPath, string packName, bool serverSync, bool isTemporarySync)
        {
            foreach (string file in Directory.GetFiles(directoryPath, "*.wav"))
            {
                string fullSoundName = Path.GetFileNameWithoutExtension(file);
                var (soundName, percentage, customName) = ParseSoundFileName(fullSoundName);

                string soundKey = percentage.HasValue
                    ? $"{soundName}-{customName}-{percentage.Value}"
                    : $"{soundName}-{customName}";

                string newHash = CalculateMD5(file);

                if (!isTemporarySync && currentSounds.Contains(soundKey)) continue;

                if (soundHashes.TryGetValue(soundKey, out var oldHash) && oldHash != newHash)
                {
                    modifiedSounds.Add(soundKey);
                }

                AudioClip customSound = SoundTool.GetAudioClip(directoryPath, "", file);
                SoundTool.ReplaceAudioClip(soundName, customSound);

                soundHashes[soundKey] = newHash;
                currentSounds.Add(soundKey);
                soundPacks[soundName] = packName;

                string logInfo = $"[{packName}] {soundName} sound replaced!";
                if (percentage > 0)
                    logInfo += $" (Random chance: {percentage}%)";

                logger.LogInfo(logInfo);

                string customNameKey = soundName + (percentage.HasValue ? $"-{percentage.Value}-{customName}" : $"-{customName}");
                if (!string.IsNullOrEmpty(customName))
                {
                    customSoundNames[customNameKey] = customName;
                }


                if (serverSync)
                {
                    string filePath = Path.Combine(directoryPath, fullSoundName + ".wav");
                    logger.LogInfo($"[{filePath}] {fullSoundName + ".wav"}!");
                    AudioNetworkHandler.Instance.QueueAudioData(Plugin.SerializeWavToBytes(filePath), fullSoundName + ".wav");
                }
            }
        }

        public string ListAllSounds(bool isListing)
        {
            StringBuilder sb = new StringBuilder(isListing ? "Listing all currently loaded custom sounds:\n\n" : "Customsounds reloaded.\n\n");

            var soundsByPack = new Dictionary<string, List<string>>();

            Action<HashSet<string>, string> addSoundsToPack = (soundsSet, status) =>
            {
                foreach (var fullSoundName in soundsSet)
                {
                    var (baseSoundName, pct, customNamePart) = ParseSoundFileName(fullSoundName);
                    string percentageText = pct.HasValue ? $" (Random: {pct.Value}%)" : "";
                    string customNameText = "";

                    string keyForCustomName = baseSoundName + (pct.HasValue ? $"-{pct.Value}-{customNamePart}" : $"-{customNamePart}");
                    if (customSoundNames.TryGetValue(keyForCustomName, out string name))
                    {
                        customNameText = $" [{name}]";
                    }

                    string packName = soundPacks.ContainsKey(baseSoundName) ? soundPacks[baseSoundName] : "Unknown";

                    if (!soundsByPack.ContainsKey(packName))
                    {
                        soundsByPack[packName] = new List<string>();
                    }

                    string soundInfo = isListing ? $"{baseSoundName}{percentageText}{customNameText}" : $"{baseSoundName} ({status}){percentageText}{customNameText}";
                    soundsByPack[packName].Add(soundInfo);
                }
            };

            if (!isListing)
            {
                addSoundsToPack(new HashSet<string>(currentSounds.Except(oldSounds)), "N¹");
                addSoundsToPack(new HashSet<string>(oldSounds.Except(currentSounds)), "D²");
                addSoundsToPack(new HashSet<string>(oldSounds.Intersect(currentSounds).Except(modifiedSounds)), "A.E³");
                addSoundsToPack(new HashSet<string>(modifiedSounds), "M⁴");
            }
            else
                addSoundsToPack(new HashSet<string>(currentSounds), "N¹");

            foreach (var pack in soundsByPack.Keys)
            {
                sb.AppendLine($"{pack} :");
                foreach (var sound in soundsByPack[pack])
                {
                    sb.AppendLine($"- {sound}");
                }
                sb.AppendLine();
            }


            if (!isListing)
            {
                sb.AppendLine("Footnotes:");
                sb.AppendLine("¹ N = New");
                sb.AppendLine("² D = Deleted");
                sb.AppendLine("³ A.E = Already Existed");
                sb.AppendLine("⁴ M = Modified");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}