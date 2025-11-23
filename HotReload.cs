using System;
using System.IO;
using System.Net;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PimDeWitte.UnityMainThreadDispatcher;
using Verse;
using UnityEngine;
using System.Linq;
using LudeonTK;


namespace RimWorldHotReload
{
	[StaticConstructorOnStartup]
	public static class HotReloadCore
	{
		public static List<HotReloadApp> mods = [];

		private static HotReloadServer server = null;
		private static FileSystemWatcher watcher = null;

		static HotReloadCore()
		{
			// Trigger play data reload
			foreach (var mod in LoadedModManager.RunningMods)
			{
				var path = mod.RootDir;
				var configPath = Path.Combine(path, "rimworldHotReload.json");
				if (!File.Exists(configPath)) continue;

				HotReloadConfig config = null;
				try
				{
					var txt = File.ReadAllText(configPath);
					config = JsonUtility.FromJson<HotReloadConfig>(txt); // validate JSON
					if (config.enabled == false) continue;
				}
				catch (Exception ex)
				{
					Log.Error("[HotReload] Failed to read rimworldHotReload.json: " + ex);
					continue;
				}

				mods.Add(new HotReloadApp
				{
					modContentPack = mod,
					config = config
				});
			}

			if (mods.Count == 0)
			{
				Log.Message("======================");
				Log.Message("* RimWorld Hot Reload *");
				Log.Warning("No mods with rimworldHotReload.json found, not starting server.");
				Log.Warning("Create a rimworldHotReload.json file in your mod folder to enable hot reload.");
				Log.Warning("Example content:");
				Log.Message("{");
				Log.Message("  \"enabled\": false,");
				Log.Message("  \"assets\": true,");
				Log.Message("  \"defs\": true");
				Log.Message("  \"watch\": true");
				Log.Message("  \"api\": false");
				Log.Message("}");
				Log.Warning("`enabled`: Enable hot reload for this mod.");
				Log.Warning("`assets`: Enable asset hot reload for this mod. (default true)");
				Log.Warning("`defs`: Enable def hot reload for this mod. (default true)");
				Log.Warning("`watch`: Enable file watch and auto reload for this mod. (default true)");
				Log.Warning("`api`: Enable API for this mod. (default false)");
				Log.Message("Made by xiaoeyun's RimWorld Hot Reload");
				Log.Message("======================");
				return;
			}

			// try start HTTP listener
			if (mods.Any(m => m.config.api)) server = new HotReloadServer();

			// Print startup banner to in-game log
			Log.Message("======================");
			Log.Message("* RimWorld Hot Reload *");
			Log.Message("Mods: " + string.Join(", ", mods.Select(m => m.modContentPack.Name)));
			Log.Message("Manual trigger: Debug Actions -> Mods -> Hot Reload");
			if (server != null)
			{
				Log.Message("Web trigger: GET http://localhost:8700/");
				Log.Message("API trigger: POST http://localhost:8700/hot-reload");
			}
			if (watcher != null)
			{
				Log.Message("Auto reload: Enabled");
			}
			Log.Message("Made by xiaoeyun's RimWorld Hot Reload");
			Log.Message("======================");


			// run background accept loop
			var dispatcher = new GameObject();
			dispatcher.AddComponent<UnityMainThreadDispatcher>();

			Task.Run(server.HandleHttpRequestLoop);
		}


		[DebugAction("Mods", "Hot Reload", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
		public static void TriggerReload()
		{
			UnityMainThreadDispatcher.Instance().Enqueue(() => ReloadMods());
		}

		public static void ReloadMods(List<HotReloadApp> HotReloadApps = null)
		{
			HotReloadApps ??= mods;

			if (HotReloadApps.Any(m => m.config.defs))
			{
				Log.Message("[HotReload] Reload Defs");
				PlayDataLoader.HotReloadDefs();
			}

			foreach (var HotReloadApp in HotReloadApps)
			{
				if (HotReloadApp.config.assets == false) continue;

				var audioClips = HotReloadApp.modContentPack.GetContentHolder<AudioClip>();
				Log.Message("[HotReload] Reload AudioClips");
				audioClips.ReloadAll();

				var textures = HotReloadApp.modContentPack.GetContentHolder<Texture2D>();
				Log.Message("[HotReload] Reload Textures");
				textures.ReloadAll();

				var stringAssets = HotReloadApp.modContentPack.GetContentHolder<string>();
				Log.Message("[HotReload] Reload String Assets");
				stringAssets.ReloadAll();
			}
		}
	}

	public class HotReloadServer
	{
		private const string UrlPrefix = "http://localhost:8700/";
		private HttpListener listener;

		public HotReloadServer()
		{

			try
			{
				listener = new HttpListener();
				listener.Prefixes.Add(UrlPrefix);
				listener.Start();
			}
			catch (Exception ex)
			{
				Log.Warning("[HotReload] Failed to start HTTP listener: " + ex);
				listener = null;
			}
		}

		public async Task HandleHttpRequestLoop()
		{
			while (listener != null && listener.IsListening)
			{
				HttpListenerContext ctx = await listener.GetContextAsync().ConfigureAwait(false);
				HandleContext(ctx);
			}
		}

		private void HandleContext(HttpListenerContext ctx)
		{
			var req = ctx.Request;
			var res = ctx.Response;

			if (req.HttpMethod == "GET" && req.RawUrl == "/")
			{
				string html = "<html><head><meta charset=\"utf-8\"></head><body>" +
					"<h3>RimWorld Mod Maker - Hot Reload</h3>" +
					"<button onclick=\"fetch('/hot-reload',{method:'POST'})\">Hot Reload</button>" +
					"</body></html>";
				Response(res, html, "html");
				return;
			}

			if (req.HttpMethod == "POST" && req.RawUrl.StartsWith("/hot-reload", StringComparison.OrdinalIgnoreCase))
			{
				// Read Json array from body 
				// It should be like { "mods": ["ModName1", "ModName2"] }
				// If empty, reload all mods
				var mods = new List<HotReloadApp>();
				if (req.ContentLength64 > 0)
				{
					using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
					var body = reader.ReadToEnd();
					try
					{
						var request = JsonUtility.FromJson<RequestReloadMods>(body);

						// map mod names to HotReloadApp
						foreach (var modName in request.mods)
						{
							var mod = HotReloadCore.mods.FirstOrDefault(m => m.modContentPack.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
							if (mod != null)
							{
								mods.Add(mod);
							}
							else
							{
								Log.Warning("[HotReload] Mod not found or not enabled for hot reload: " + modName);
							}
						}
					}
					catch (Exception ex)
					{
						Log.Error("[HotReload] Failed to parse request body: " + ex);
						Response(res, "Bad Request: Invalid JSON", "plain", 400);
						return;
					}
				}
				else
				{
					mods = HotReloadCore.mods;
				}

				Log.Message("[HotReload] Triggered via HTTP POST /hot-reload");
				UnityMainThreadDispatcher.Instance().Enqueue(() => HotReloadCore.ReloadMods(mods));
				Response(res, "OK: Reload triggered", "plain");
				return;
			}

			Response(res, "Not Found", "plain", 404);
		}

		private static void Response(HttpListenerResponse res, string content, string contentType, int statusCode = 200)
		{
			byte[] buffer = Encoding.UTF8.GetBytes(content);
			res.ContentType = "text/" + contentType + "; charset=utf-8";
			res.ContentLength64 = buffer.Length;
			res.StatusCode = statusCode;
			res.OutputStream.Write(buffer, 0, buffer.Length);
			res.OutputStream.Close();
		}

	}

	public class HotReloadWatcher
    {
    }

	[System.Serializable]
	public class HotReloadConfig
	{
		public bool enabled = true;
		public bool assets = true;
		public bool defs = true;
		public bool watch = true;
		public bool api = false;
	}

	public class HotReloadApp
	{
		public ModContentPack modContentPack;
		public HotReloadConfig config;
	}

	public class RequestReloadMods
	{
		public List<string> mods;
	}
}

// Utils
namespace PimDeWitte.UnityMainThreadDispatcher
{
	/// Author: Pim de Witte (pimdewitte.com) and contributors, https://github.com/PimDeWitte/UnityMainThreadDispatcher
	/// <summary>
	/// A thread-safe class which holds a queue with actions to execute on the next Update() method. It can be used to make calls to the main thread for
	/// things such as UI Manipulation in Unity. It was developed for use in combination with the Firebase Unity plugin, which uses separate threads for event handling
	/// </summary>
	public class UnityMainThreadDispatcher : MonoBehaviour
	{

		private static readonly Queue<Action> _executionQueue = new Queue<Action>();

		public void Update()
		{
			lock (_executionQueue)
			{
				while (_executionQueue.Count > 0)
				{
					_executionQueue.Dequeue().Invoke();
				}
			}
		}

		/// <summary>
		/// Locks the queue and adds the IEnumerator to the queue
		/// </summary>
		/// <param name="action">IEnumerator function that will be executed from the main thread.</param>
		public void Enqueue(IEnumerator action)
		{
			lock (_executionQueue)
			{
				_executionQueue.Enqueue(() =>
				{
					StartCoroutine(action);
				});
			}
		}

		/// <summary>
		/// Locks the queue and adds the Action to the queue
		/// </summary>
		/// <param name="action">function that will be executed from the main thread.</param>
		public void Enqueue(Action action)
		{
			Enqueue(ActionWrapper(action));
		}

		/// <summary>
		/// Locks the queue and adds the Action to the queue, returning a Task which is completed when the action completes
		/// </summary>
		/// <param name="action">function that will be executed from the main thread.</param>
		/// <returns>A Task that can be awaited until the action completes</returns>
		public Task EnqueueAsync(Action action)
		{
			var tcs = new TaskCompletionSource<bool>();

			void WrappedAction()
			{
				try
				{
					action();
					tcs.TrySetResult(true);
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
				}
			}

			Enqueue(ActionWrapper(WrappedAction));
			return tcs.Task;
		}


		IEnumerator ActionWrapper(Action a)
		{
			a();
			yield return null;
		}


		private static UnityMainThreadDispatcher _instance = null;

		public static bool Exists()
		{
			return _instance != null;
		}

		public static UnityMainThreadDispatcher Instance()
		{
			if (!Exists())
			{
				throw new Exception("UnityMainThreadDispatcher could not find the UnityMainThreadDispatcher object. Please ensure you have added the MainThreadExecutor Prefab to your scene.");
			}
			return _instance;
		}


		void Awake()
		{
			if (_instance == null)
			{
				_instance = this;
				DontDestroyOnLoad(this.gameObject);
			}
		}

		void OnDestroy()
		{
			_instance = null;
		}


	}
}