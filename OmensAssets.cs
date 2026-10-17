using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FollowingSeasOmens
{
	/// <summary>
	/// The mod's own shaders, from <c>omens.bundle</c> next to the DLL (built from <c>Tools/OmensShaders</c>
	/// with the Unity editor). Loaded once, on first use, and kept for the life of the game; each shader is
	/// looked up, and what came of it logged, once.
	/// </summary>
	internal static class OmensAssets
	{
		private const string FileName = "omens.bundle";

		private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
		private static AssetBundle _bundle;
		private static bool _tried;

		/// <summary>The shader by its name in the bundle, or null when the bundle or the shader cannot be
		/// had or the shader does not run here.</summary>
		public static Shader Shader(string name)
		{
			if (!_tried)
			{
				_tried = true;
				string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", FileName);
				_bundle = File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
				if (_bundle == null) Plugin.Log.LogWarning("Could not load " + path);
				else Plugin.Log.LogInfo("Loaded " + FileName);
			}

			if (_bundle == null) return null;
			if (Shaders.TryGetValue(name, out var known)) return known;

			var shader = _bundle.LoadAsset<Shader>(name);
			if (shader == null) Plugin.Log.LogWarning("No shader '" + name + "' in " + FileName);
			else if (!shader.isSupported) Plugin.Log.LogWarning("Shader '" + name + "' does not run on this graphics device");
			else Plugin.Log.LogInfo("Shader '" + name + "' from " + FileName + " is in use");
			if (shader != null && !shader.isSupported) shader = null;
			Shaders[name] = shader;
			return shader;
		}
	}
}
