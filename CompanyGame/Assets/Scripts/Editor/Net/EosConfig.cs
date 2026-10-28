using System;
using System.IO;
using System.Linq;
using PlayEveryWare.EpicOnlineServices;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The EOS plugin config in StreamingAssets/EOS (git-ignored: it holds the client secret), written from CompanyGame/eos.local.json
/// (Docs/Multiplayer_Redesign.md 12.4; moved out of EosTestQA in 12.13 because every build with EOS needs it). The Windows
/// native plugin reads it before Unity starts, so it must be there before a build.
///   -executeMethod EosConfig.Write   (with -quit)
/// </summary>
public static class EosConfig
{
    [Serializable]
    sealed class LocalValues
    {
        public string ProductId, SandboxId, DeploymentId, ClientId, ClientSecret;
    }

    /// <summary>Writes the plugin's product, Windows and macOS config with the plugin's own Config classes. Never logs the values.</summary>
    [MenuItem("CompanyGame/Net/Write EOS Config")]
    public static void Write()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../eos.local.json"));
        if (!File.Exists(path)) throw new FileNotFoundException("no EOS values: copy eos.local.example.json to eos.local.json and fill it in", path);
        var local = JsonUtility.FromJson<LocalValues>(File.ReadAllText(path));

        var product = PlayEveryWare.EpicOnlineServices.Config.Get<ProductConfig>();
        string key = product.Clients.FirstOrDefault()?.Value?.EncryptionKey; // keep the key between writes (only Player/Title Data Storage use it)
        var credentials = new EOSClientCredentials(local.ClientId, local.ClientSecret, EOSClientCredentials.IsEncryptionKeyValid(key) ? key : EOSClientCredentials.GenerateEncryptionKey());
        var deployment = new Deployment { SandboxId = SandboxId.FromString(local.SandboxId), DeploymentId = Guid.Parse(local.DeploymentId) };
        product.ProductName = "CompanyGame";
        product.ProductVersion = "1";
        product.ProductId = Guid.Parse(local.ProductId);
        product.Clients.Clear();
        product.Clients.Add(credentials);
        product.Environments.Deployments.Clear();
        product.Environments.Sandboxes.Clear();
        product.Environments.AddDeployment(deployment);
        product.Write();

        var windows = PlayEveryWare.EpicOnlineServices.Config.Get<WindowsConfig>();
        windows.deployment = deployment;
        windows.clientCredentials = credentials;
        windows.isServer = false; // the server process logs in with a Device ID like a player (12.4)
        windows.platformOptionsFlags = WrappedPlatformFlags.DisableOverlay | WrappedPlatformFlags.DisableSocialOverlay; // no overlay in a windowless server
        windows.Write();

        var mac = PlayEveryWare.EpicOnlineServices.Config.Get<MacOSConfig>(); // the same settings for the macOS build
        mac.deployment = deployment;
        mac.clientCredentials = credentials;
        mac.isServer = false;
        mac.platformOptionsFlags = windows.platformOptionsFlags;
        mac.Write();
        AssetDatabase.Refresh();
        Debug.Log("[EosConfig] wrote EOS config to " + Path.GetDirectoryName(product.FilePath) + " (values not logged)");
    }
}
