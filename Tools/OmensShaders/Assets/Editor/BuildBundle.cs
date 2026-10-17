using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the mod's asset bundle (its own shaders) for Windows, Direct3D 11 as the game uses:
/// Unity.exe -batchmode -nographics -quit -projectPath Tools/OmensShaders -executeMethod BuildBundle.Run
/// The bundle lands in Bundles/omens.bundle at the repository root (committed, so the mod builds without
/// Unity); the mod's build copies it next to the DLL.
/// </summary>
public static class BuildBundle
{
	public static void Run()
	{
		PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
		PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
			new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });

		const string output = "../../Bundles";
		Directory.CreateDirectory(output);
		var build = new AssetBundleBuild
		{
			assetBundleName = "omens.bundle",
			assetNames = new[] { "Assets/Shaders/OmensMeteor.shader" },
			addressableNames = new[] { "Meteor" }
		};
		var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
			BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
			BuildTarget.StandaloneWindows64);
		if (manifest == null) throw new System.Exception("Asset bundle build failed");
		Debug.Log("Omens bundle built: " + Path.GetFullPath(Path.Combine(output, "omens.bundle")));
	}
}
