using CustomSounds.Networking;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomSounds.Patches
{
    [HarmonyPatch]
    public class NetworkObjectManager
    {

        [HarmonyPatch(typeof(GameNetworkManager), "Start")]
        [HarmonyPrefix]
        public static void Init()
        {
            if (networkPrefab != null) return;
            networkPrefab = Plugin.Instance.LoadNetworkPrefabFromEmbeddedResource();

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
