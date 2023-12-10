using Unity.Netcode;
using UnityEngine;
using CustomSounds;
using System.Collections.Generic;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine.UIElements;
using BepInEx;
using System.IO;
using System;

namespace CustomSoundsComponents
{
    public class AudioNetworkHandler : NetworkBehaviour
    {
        public static AudioNetworkHandler Instance { get; private set; }

        private List<byte[]> receivedAudioSegments = new List<byte[]>();
        private string audioFileName;

        private int totalAudioFiles;
        private int processedAudioFiles;
        private int totalSegments;
        private int processedSegments;

        private bool isRequestingSync;

        private float[] progressThresholds = new float[] { 0.25f, 0.50f, 0.75f, 1.0f };
        private int lastThresholdIndex = -1;

        private bool hasAcceptedSync = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(this.gameObject);
                Debug.Log("AudioNetworkHandler instance created.");
            }
            else
            {
                Destroy(this.gameObject);
                Debug.Log("Extra AudioNetworkHandler instance destroyed.");
            }
        }

        private void Update()
        {
            if (isRequestingSync)
            {
                if (CustomSounds.Plugin.AcceptSyncKey.Value.IsPressed())
                {
                    Plugin.Instance.ShowCustomTip("CustomSounds Sync", "Sync request accepted successfully!", false);
                    hasAcceptedSync = true;
                    isRequestingSync = false;
                }
            }
        }

        public void QueueAudioData(byte[] audioData, string audioName)
        {
            var audioSegments = Plugin.SplitAudioData(audioData);
            totalSegments += audioSegments.Count;
            audioQueue.Enqueue(new AudioData(audioSegments, audioName));
            if (!isSendingAudio)
            {
                totalAudioFiles = audioQueue.Count;
                processedAudioFiles = 0;
                StartCoroutine(SendAudioDataQueue());
            }
        }

        private struct AudioData
        {
            public List<byte[]> Segments;
            public string FileName;

            public AudioData(List<byte[]> segments, string fileName)
            {
                Segments = segments;
                FileName = fileName;
            }
        }

        private Queue<AudioData> audioQueue = new Queue<AudioData>();
        private bool isSendingAudio = false;

        private IEnumerator SendAudioDataQueue()
        {
            isSendingAudio = true;
            totalSegments = 0;
            processedSegments = 0;
            lastThresholdIndex = -1;

            RequestSyncWithClients();

            yield return new WaitForSeconds(6.0f);

            isRequestingSync = false;
            DisplayStartingSyncMessageClientRpc();

            yield return new WaitForSeconds(2.0f);

            while (audioQueue.Count > 0)
            {
                var audioData = audioQueue.Dequeue();
                yield return StartCoroutine(SendAudioDataCoroutine(audioData.Segments, audioData.FileName));
                processedAudioFiles++;
                UpdateProgress();
            }

            NotifyClientsQueueCompletedServerRpc();

            isSendingAudio = false;
        }
        private void RequestSyncWithClients()
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                RequestSyncClientRpc(client.ClientId);
            }
        }


        [ClientRpc]
        private void RequestSyncClientRpc(ulong clientId)
        {
            if (IsServer) return;

            Plugin.Instance.ShowCustomTip("CustomSounds Sync", $"Press {CustomSounds.Plugin.AcceptSyncKey.Value} to accept the audio sync request.", false);
            isRequestingSync = true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void NotifyClientsQueueCompletedServerRpc(ServerRpcParams rpcParams = default)
        {
            ProcessLastAudioFileClientRpc();
        }


        public void ForceUnsync()
        {
            ForceUnsyncClientsClientRpc();
        }

        [ClientRpc]
        private void ForceUnsyncClientsClientRpc()
        {
            if (IsServer)
            {
                Debug.Log("Forcing all clients to delete Temporary-Sync folder.");
            }
            else
            {
                Plugin.Instance.ShowCustomTip("CustomSounds Sync", "The CustomSounds sync has been reset by the host.\nTemporary files deleted and original sounds reloaded", false);

                Plugin.Instance.DeleteTempFolder();
                Plugin.Instance.ReloadSounds(false, false);
            }
        }

        [ClientRpc]
        private void ProcessLastAudioFileClientRpc()
        {
            if (!hasAcceptedSync) return;

            hasAcceptedSync = false;

            ProcessLastAudioFile();

            Debug.Log("Reloading all sounds.");
            Plugin.Instance.ReloadSounds(false, true);
            Debug.Log("All sounds reloaded!");
        }

        private void ProcessLastAudioFile()
        {
            if (receivedAudioSegments.Count > 0 && !string.IsNullOrEmpty(audioFileName))
            {
                var completeAudioData = Plugin.CombineAudioSegments(receivedAudioSegments);
                Plugin.DeserializeBytesToWav(completeAudioData, audioFileName);

                receivedAudioSegments.Clear();
                audioFileName = null;
            }
        }

        public void SendAudioData(byte[] audioData, string audioName)
        {
            var audioSegments = Plugin.SplitAudioData(audioData);
            audioFileName = audioName;
            SendAudioMetaDataToClientsServerRpc(audioSegments.Count, audioName);
            StartCoroutine(SendAudioDataCoroutine(audioSegments, audioName));
        }


        [ServerRpc]
        public void SendAudioMetaDataToClientsServerRpc(int totalSegments, string fileName)
        {
            Debug.Log($"Sending metadata to clients: {totalSegments} segments, file name: {fileName}");
            ReceiveAudioMetaDataClientRpc(totalSegments, fileName);
        }

        private IEnumerator SendAudioDataCoroutine(List<byte[]> audioSegments, string audioName)
        {
            foreach (var segment in audioSegments)
            {
                SendBytesToServerRpc(segment, audioName);
                processedSegments++;
                UpdateProgress();
                yield return new WaitForSeconds(0.2f);
            }
        }

        [ServerRpc]
        public void SendBytesToServerRpc(byte[] audioSegment, string audioName)
        {
            Debug.Log("Sending segment to server: " + audioName);
            ReceiveBytesClientRpc(audioSegment, audioName);
        }

        [ClientRpc]
        public void ReceiveAudioMetaDataClientRpc(int totalSegments, string fileName)
        {
            Debug.Log($"Received metadata on client: {totalSegments} segments expected, file name: {fileName}");
            audioFileName = fileName;
            receivedAudioSegments.Clear();
        }

        [ClientRpc]
        public void ReceiveBytesClientRpc(byte[] audioSegment, string audioName)
        {
            if (IsServer) return;

            if (!hasAcceptedSync) return;

            if (!string.IsNullOrEmpty(audioName) && audioFileName != audioName)
            {
                ProcessLastAudioFile();
                audioFileName = audioName;
            }

            receivedAudioSegments.Add(audioSegment);
        }


        [ClientRpc]
        private void DisplayStartingSyncMessageClientRpc()
        {
            if (hasAcceptedSync)
            {
                Plugin.Instance.ShowCustomTip("CustomSounds Sync", "Starting audio synchronization. Please wait...", false);
            }
            else if (IsServer)
            {
                Plugin.Instance.ShowCustomTip("CustomSounds Sync", "Initiating audio synchronization. Sending files to clients...", false);
            }
        }

        private void UpdateProgress()
        {
            float progress = (float)processedSegments / totalSegments;
            int currentThresholdIndex = GetCurrentThresholdIndex(progress);

            if (currentThresholdIndex > lastThresholdIndex)
            {
                lastThresholdIndex = currentThresholdIndex;
                UpdateProgressClientRpc(progress);
            }
        }

        private int GetCurrentThresholdIndex(float progress)
        {
            for (int i = progressThresholds.Length - 1; i >= 0; i--)
            {
                if (progress >= progressThresholds[i])
                    return i;
            }
            return -1;
        }

        [ClientRpc]
        private void UpdateProgressClientRpc(float progress)
        {

            if (IsServer || hasAcceptedSync)
            {
                string progressBar = GenerateProgressBar(progress);
                if (progress < 1.0f)
                {
                    if (!IsServer)
                    {
                        Plugin.Instance.ShowCustomTip("CustomSounds Sync", "Sounds transfer progression:\n" + progressBar, false);
                    }
                }
                else
                {
                    if (IsServer)
                    {
                        Plugin.Instance.ShowCustomTip("CustomSounds Sync", "All sounds have been successfully sent!", false);
                    }
                    else
                    {
                        Plugin.Instance.ShowCustomTip("CustomSounds Sync", "All sounds have been successfully received!", false);
                    }
                }
            }
        }

        private string GenerateProgressBar(float progress)
        {
            int barSize = 26;
            progress = Mathf.Clamp(progress, 0f, 1f);
            int progressBars = (int)(progress * barSize);
            return "[" + new string('#', progressBars) + new string('-', barSize - progressBars) + "] " + (int)(progress * 100) + "%";
        }
    }
}