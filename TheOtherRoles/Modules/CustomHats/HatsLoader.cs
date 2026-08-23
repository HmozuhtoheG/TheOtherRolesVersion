using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx.Unity.IL2CPP.Utils;
using TheOtherRoles.Utilities;
using UnityEngine;
using UnityEngine.Networking;
using static TheOtherRoles.Modules.CustomHats.CustomHatManager;

namespace TheOtherRoles.Modules.CustomHats
{
    public class HatsLoader : MonoBehaviour
    {
        private bool isRunning;

        public void FetchHats()
        {
            if (isRunning) return;
            this.StartCoroutine(CoFetchHats());
        }

        [HideFromIl2Cpp]
        private IEnumerator CoFetchHats()
        {
            isRunning = true;
            var www = new UnityWebRequest();
            www.SetMethod(UnityWebRequest.UnityWebRequestMethod.Get);
            www.timeout = 15;
            TheOtherRolesPlugin.Logger.LogMessage($"Download manifest at: {RepositoryUrl}/{ManifestFileName}");
            www.SetUrl($"{RepositoryUrl}/{ManifestFileName}");
            www.downloadHandler = new DownloadHandlerBuffer();
            var operation = www.SendWebRequest();

            while (!operation.isDone)
            {
                yield return new WaitForEndOfFrame();
            }

            if (www.isNetworkError || www.isHttpError)
            {
                TheOtherRolesPlugin.Logger.LogError(www.error);
            }
            else
            {
                try
                {
                    UnregisteredHats.AddRange(ParseManifest(www.downloadHandler.text));
                }
                catch (Exception ex)
                {
                    TheOtherRolesPlugin.Logger.LogError($"Failed to parse remote manifest: {ex.Message}");
                }
            }
            www.downloadHandler.Dispose();
            www.Dispose();

            if (!Directory.Exists(HatsDirectory)) Directory.CreateDirectory(HatsDirectory);

            var localManifestPath = Path.Combine(HatsDirectory, ManifestFileName);
            if (File.Exists(localManifestPath))
            {
                TheOtherRolesPlugin.Logger.LogMessage($"Loading local manifest at: {localManifestPath}");
                try
                {
                    var localText = File.ReadAllText(localManifestPath);
                    foreach (var localHat in ParseManifest(localText))
                    {
                        UnregisteredHats.RemoveAll(h => h.Name == localHat.Name);
                        UnregisteredHats.Add(localHat);
                    }
                }
                catch (Exception ex)
                {
                    TheOtherRolesPlugin.Logger.LogError($"Failed to load local manifest: {ex.Message}");
                }
            }

            var toDownload = GenerateDownloadList(UnregisteredHats);
            UnregisteredHats.AddRange(CustomHatManager.loadBundledHats());
            if (EventUtility.isEnabled) UnregisteredHats.AddRange(CustomHatManager.loadHorseHats());

            TheOtherRolesPlugin.Logger.LogMessage($"I'll download {toDownload.Count} hat files");

            foreach (var fileName in toDownload)
            {
                yield return CoDownloadHatAsset(fileName);
            }

            yield return CoRegisterHatsWhenReady();

            isRunning = false;
        }

        private static List<CustomHat> ParseManifest(string jsonText)
        {
            var response = JsonSerializer.Deserialize<SkinsConfigFile>(jsonText, new JsonSerializerOptions
            {
                AllowTrailingCommas = true
            });
            return response?.Hats != null ? SanitizeHats(response) : new List<CustomHat>();
        }

        private static IEnumerator CoDownloadHatAsset(string fileName)
        {
            var www = new UnityWebRequest();
            www.SetMethod(UnityWebRequest.UnityWebRequestMethod.Get);
            www.timeout = 15;
            fileName = fileName.Replace(" ", "%20");
            TheOtherRolesPlugin.Logger.LogMessage($"Downloading {fileName} hat");
            www.SetUrl($"{RepositoryUrl}/hats/{fileName}");
            www.downloadHandler = new DownloadHandlerBuffer();
            var operation = www.SendWebRequest();

            while (!operation.isDone)
            {
                yield return new WaitForEndOfFrame();
            }

            if (www.isNetworkError || www.isHttpError)
            {
                TheOtherRolesPlugin.Logger.LogError(www.error);
                yield break;
            }

            var filePath = Path.Combine(HatsDirectory, fileName);
            filePath = filePath.Replace("%20", " ");
            var persistTask = File.WriteAllBytesAsync(filePath, www.downloadHandler.GetUnstrippedData());
            while (!persistTask.IsCompleted)
            {
                if (persistTask.Exception != null)
                {
                    TheOtherRolesPlugin.Logger.LogError(persistTask.Exception.Message);
                    break;
                }

                yield return new WaitForEndOfFrame();
            }

            www.downloadHandler.Dispose();
            www.Dispose();
        }

        private IEnumerator CoRegisterHatsWhenReady()
        {
            while (DestroyableSingleton<HatManager>.Instance == null || DestroyableSingleton<HatManager>.Instance.allHats == null)
            {
                TheOtherRolesPlugin.Logger.LogMessage("Waiting for HatManager to be ready...");
                yield return new WaitForEndOfFrame();
            }

            TheOtherRolesPlugin.Logger.LogMessage("HatManager ready, registering hats...");
            RegisterAllHats();
        }
    }
}
