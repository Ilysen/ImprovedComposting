using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ImprovedComposting
{
	class ImprovedCompostingSystem : ModSystem
	{
		public static string ID => "ava_improvedcomposting";

		public override void Start(ICoreAPI api)
		{
			base.Start(api);
			api.RegisterBlockClass(nameof(BlockComposter), typeof(BlockComposter));
			api.RegisterBlockEntityClass(nameof(BlockEntityComposter), typeof(BlockEntityComposter));
			api.World.Logger.Event("Started ImprovedComposting!");
		}

		/// <summary>
		/// Wrapper for <see cref="Lang.Get(string, object[])"/> that automatically prepends the mod ID, as defined in <see cref="ID"/>.
		/// </summary>
		public static string GetLang(string key, params object[] args) => Lang.Get($"{ID}:{key}", args);
	}
}
