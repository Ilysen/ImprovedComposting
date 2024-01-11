using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImprovedComposting
{
	/// <summary>
	/// Block entity for the compost bin block. This handles basically all of the logic except for interaction popups.
	/// </summary>
	public class BlockEntityComposter : BlockEntityGeneric
	{
		private static readonly bool DEBUG = false;

		#region Balancing
		/// <summary>
		/// How long it takes for a batch of compost to complete at an optimal rate throughout.
		/// </summary>
		public static readonly ushort NumberOfHoursRequired = 480;

		/// <summary>
		/// A composter needs to be filled with this many layers. (half of this number - 1) will be browns, and the rest will alternate between browns and greens.
		/// </summary>
		public static readonly ushort LayersNeeded = 10;

		/// <summary>
		/// How long the composter can go without being turned before it starts to slow down.
		/// </summary>
		public static readonly ushort TurnInterval = 72;

		/// <summary>
		/// How much wetness the composter will gain every tick under the rain. This is multiplied by precipitation level.
		/// </summary>
		public static readonly float WetnessGainFromRainPerTick = 0.01f;

		/// <summary>
		/// How much wetness the composter will lose every hour.
		/// </summary>
		public static readonly float WetnessLossPerTick = 0.004f;

		/// <summary>
		/// One liter of water will increment wetness by this much.
		/// </summary>
		public static readonly float WaterConversionRatio = 0.05f;
		#endregion

		#region Logic fields
		/// <summary>
		/// The number of completed layers in this composter.
		/// </summary>
		public int Layers;

		/// <summary>
		/// How close the composter is to finishing. When this value is 1 or over, the compost is ready to remove.
		/// </summary>
		public float DecompositionProgress = 0;

		/// <summary>
		/// We cache decomposition rate here to avoid having to do costly calculations in block info calculation.
		/// </summary>
		public float CachedDecompositionRate;

		/// <summary>
		/// The wetness of the compost, from 0 to 1. Anything below 0.25 is too dry, anything above 0.75 is too wet.
		/// </summary>
		public float Wetness = 0.5f;

		/// <summary>
		/// The hour in which the compost was last turned.
		/// </summary>
		public double LastTurn;

		/// <summary>
		/// The hour that we last ran an update on.
		/// </summary>
		public double LastUpdate;

		/// <summary>
		/// Whether or not the lid is closed.
		/// </summary>
		public bool LidClosed;
		#endregion

		#region Private fields
		private BlockComposter _block;
		private WeatherSystemBase _wsys;
		private Vec3d _tmpPos = new();
		#endregion

		#region Properties and getters
		private bool NeedsBrowns => Layers < Math.Floor((double)LayersNeeded / 2) || Layers % 2 == 0;
		private float IdealDecompositionRate => (float)Math.Round(1f / NumberOfHoursRequired, 4);

		/// <summary>
		/// Returns how many days remain at the current decomposition rate before the composter finishes.
		/// </summary>
		private float DaysLeft
		{
			get
			{
				float daysElapsed = (((1 - DecompositionProgress) * NumberOfHoursRequired) * (IdealDecompositionRate / CachedDecompositionRate)) / Api.World.Calendar.HoursPerDay;
				if (DEBUG)
					Api.World.Logger.Event($"Ran DaysLeft with the following data and result: (((1 - {DecompositionProgress}) * {NumberOfHoursRequired}) * ({IdealDecompositionRate} / {CachedDecompositionRate})) / {Api.World.Calendar.HoursPerDay} == {daysElapsed}");
				return Math.Min(99, (float)Math.Round(daysElapsed, 1));
			}
		}
		#endregion

		#region Overrides -- Initialize, serialization, etc
		public override void Initialize(ICoreAPI api)
		{
			base.Initialize(api);
			_block = Block as BlockComposter;
			_wsys = api.ModLoader.GetModSystem<WeatherSystemBase>();
			CalculateDecompRate();
			if (api.Side != EnumAppSide.Client)
				RegisterGameTickListener(TickPerSecond, 1000);
		}

		public override void ToTreeAttributes(ITreeAttribute tree)
		{
			base.ToTreeAttributes(tree);
			tree.SetInt(nameof(Layers), Layers);
			tree.SetFloat(nameof(CachedDecompositionRate), CachedDecompositionRate);
			tree.SetFloat(nameof(DecompositionProgress), DecompositionProgress);
			tree.SetFloat(nameof(Wetness), Wetness);
			tree.SetDouble(nameof(LastTurn), LastTurn);
			tree.SetDouble(nameof(LastUpdate), LastUpdate);
		}

		public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
		{
			base.FromTreeAttributes(tree, worldAccessForResolve);
			Layers = tree.GetInt(nameof(Layers));
			CachedDecompositionRate = tree.GetFloat(nameof(CachedDecompositionRate));
			DecompositionProgress = tree.GetFloat(nameof(DecompositionProgress));
			Wetness = tree.GetFloat(nameof(Wetness));
			LastTurn = tree.GetDouble(nameof(LastTurn));
			LastUpdate = tree.GetDouble(nameof(LastUpdate));
		}

		public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
		{
			if (DecompositionProgress >= 1f)
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-ready"));
			else if (Layers < LayersNeeded)
			{
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-layers", Layers, LayersNeeded));
				if (NeedsBrowns)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsbrowns"));
				else
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsgreens"));
			}
			else
			{
				if (DEBUG)
				{
					dsc.AppendLine($"=== DEBUG INFO ===");
					dsc.AppendLine($"Wetness {Wetness}");
					dsc.AppendLine($"CachedDecompositionRate {CachedDecompositionRate}");
					dsc.AppendLine($"LastTurn {LastTurn}");
					dsc.AppendLine($"LastUpdate {LastUpdate}");
					dsc.AppendLine($"==================");
					dsc.AppendLine("");
				}

				double decompRate = Math.Round(CachedDecompositionRate / IdealDecompositionRate * 100d, 2);
				string decompRateColor = ColorUtil.Int2Hex(GuiStyle.DamageColorGradient[(int)Math.Min(99, Math.Max(1, decompRate))]);
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-decompositionprogress", Math.Round(DecompositionProgress * 100f, 1), DaysLeft));
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-decompositionrate", decompRateColor, decompRate));

				// This code is a bit complicated, but the goal is to sharply "worsen" the color gradient when wetness is out of the comfortable bounds compared to when it's within
				// Anywhere from 25% to 75% will appear at bare minimum as yellow-green; beyond those points will quickly turn to dark red to indicate that it's now a bad thing
				float wetnessDiff = Math.Abs(Wetness - 0.5f);
				float wetnessGradient = 1 - (wetnessDiff > 0.25f ? wetnessDiff * 2 : wetnessDiff);
				string wetnessColor = ColorUtil.Int2Hex(GuiStyle.DamageColorGradient[(int)Math.Min(99, Math.Max(1, wetnessGradient * 100f))]);
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-wetness", wetnessColor, Math.Round(Wetness * 100f, 1)));

				double lastTurned = Api.World.Calendar.TotalHours - LastTurn;
				if (lastTurned < Api.World.Calendar.HoursPerDay + 1)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-lastturnedrecently"));
				else
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-lastturned", Math.Round(lastTurned / Api.World.Calendar.HoursPerDay, 2)));

				if (LidClosed)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-lidclosed"));
				else
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-lidopen"));

				if (Api.World.Calendar.TotalHours - LastTurn >= TurnInterval)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsturning"));
				if (Wetness < 0.25f)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-toodry"));
				else if (Wetness > 0.75f)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-toowet"));
			}
			base.GetBlockInfo(forPlayer, dsc);
		}
		#endregion

		#region Logic
		/// <summary>
		/// The meat of the composter's update logic is handled in here, including wetness, progress, and all that good stuff.
		/// </summary>
		private void TickPerSecond(float dt)
		{
			if (Layers != LayersNeeded)
				return;
			int floorHrs = (int)Math.Floor(Api.World.Calendar.TotalHours);
			if (LastUpdate == floorHrs)
				return;
			bool exposedToRain = Api.World.BlockAccessor.GetRainMapHeightAt(Pos) <= Pos.Y && !LidClosed;
			for (int i = (int)LastUpdate + 1; i <= floorHrs; i++)
			{
				if (DEBUG)
					Api.World.Logger.Event($"Parsing update for hour {i}/{floorHrs}");
				CalculateDecompRate(i);
				DecompositionProgress += CachedDecompositionRate;
				if (exposedToRain)
				{
					_tmpPos.Set(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
					float precip = _wsys.GetPrecipitation(_tmpPos.X, _tmpPos.Y, _tmpPos.Z, i / Api.World.Calendar.HoursPerDay);
					if (DEBUG)
						Api.World.Logger.Event($"Precipitation at day {i / Api.World.Calendar.HoursPerDay}: {precip}");
					if (precip > 0.04)
						Wetness = (float)Math.Round(Math.Min(1, Wetness + (precip * WetnessGainFromRainPerTick)), 4);
				}
				Wetness = (float)Math.Round(Math.Max(0, Wetness - WetnessLossPerTick), 4);
			}
			LastUpdate = floorHrs;
			MarkDirty();
		}

		/// <summary>
		/// Handles player interaction with the composter.
		/// </summary>
		internal bool OnBlockInteractStart(IPlayer byPlayer, BlockSelection blockSel)
		{
			bool shift = byPlayer.Entity.Controls.ShiftKey;
			var hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
			if (DecompositionProgress >= 1f)
			{
				if (hotbarSlot.Empty)
				{
					ItemStack compost = new(Api.World.GetItem(new AssetLocation("compost")), 64);
					if (byPlayer.InventoryManager.TryGiveItemstack(compost, true))
					{
						Layers = 0;
						DecompositionProgress = 0;
						byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/block/dirt"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
					}
				}
			}
			if (Layers == LayersNeeded)
			{
				if (hotbarSlot.Empty)
				{
					if (shift)
						Wetness += 0.1f;
					else
					{
						LidClosed = !LidClosed;
						if (!LidClosed)
							byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/block/barrelopen"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
						else
							byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/player/seal"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
					}
				}
				else if (hotbarSlot.Itemstack.Collectible?.Tool == EnumTool.Shovel)
				{
					LastTurn = Api.World.Calendar.TotalHours;
					byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/block/dirt"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
				}
				else if (hotbarSlot.Itemstack.Collectible is BlockLiquidContainerBase blcb && blcb.AllowHeldLiquidTransfer && !blcb.IsEmpty(hotbarSlot.Itemstack) && Wetness < 1f)
				{
					ItemStack liquid = blcb.GetContent(hotbarSlot.Itemstack);
					if (DEBUG)
						Api.World.Logger.Event($"Checking liquid stack of ID: {liquid.Collectible.Code} or {liquid.Item}, amount {liquid.StackSize}");
					if (liquid.Item.WildCardMatch("waterportion")) 
					{
						int targetLiters = (int)Math.Min(blcb.GetCurrentLitres(hotbarSlot.Itemstack), Math.Ceiling(((Wetness < 0.75f ? 0.75f : 1f) - Wetness) / WaterConversionRatio));
						if (DEBUG)
							Api.World.Logger.Event($"Target liters: {targetLiters}");
						if (targetLiters != 0)
						{
							if (DEBUG)
								Api.World.Logger.Event($"Consuming {targetLiters} liters of {liquid.Item} to restore {targetLiters * WaterConversionRatio} wetness");
							ItemStack consumed = blcb.TryTakeLiquid(hotbarSlot.Itemstack, targetLiters);
							blcb.DoLiquidMovedEffects(byPlayer, consumed, targetLiters, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
							// This is for anti-frustration; we waste a little bit of water and cap off wetness from going too high unless we're already above the maximum optimal range
							Wetness = Math.Min(Wetness < 0.75f ? 0.75f : 1f, Wetness + (targetLiters * WaterConversionRatio));
							MarkDirty();
						}
					}
				}
			}
			else if (!hotbarSlot.Empty)
			{
				string key = NeedsBrowns ? "browns" : "greens";
				if (_block != null && _block.Attributes["compostables"].Exists)
				{
					foreach (var stack in Block.Attributes["compostables"].AsObject<Dictionary<string, JsonItemStack[]>>()[key])
					{
						if (hotbarSlot.Itemstack.Collectible.WildCardMatch(stack.Code) && hotbarSlot.Itemstack.StackSize >= stack.Quantity)
						{
							AssetLocation sndId = new("sounds/effect/squish1");
							if (hotbarSlot.Itemstack.Collectible.Attributes?["placeSound"].Exists == true)
								sndId = AssetLocation.Create(hotbarSlot.Itemstack.Collectible.Attributes["placeSound"].AsString(), hotbarSlot.Itemstack.Collectible.Code.Domain).WithPathPrefixOnce("sounds/");
							Api.World.PlaySoundAt(sndId, Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5, byPlayer, true, 12);
							AddLayer();
							if (byPlayer.Entity.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
							{
								hotbarSlot.TakeOut(stack.Quantity);
								hotbarSlot.MarkDirty();
							}
							break;
						}
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Increments <see cref="Layers"/> by 1. If <c>Layers == LayersNeeded</c> afterwards, we handle the logic to properly initialize decomposition.
		/// <br/>This is probably a bit yucky and maybe could be handled better by a get{} on Layers, but this'll do for now.
		/// </summary>
		private void AddLayer()
		{
			Layers++;
			if (Layers == LayersNeeded)
			{
				Wetness = 0.5f;
				LastTurn = Api.World.Calendar.TotalHours;
				LastUpdate = Math.Floor(LastTurn);
				CalculateDecompRate();
			}
		}

		/// <summary>
		/// Calculates the decomposition rate and assigns the result to <see cref="CachedDecompositionRate"/>.
		/// <br/><br/>Decomposition rate is affected by turning and wetness:
		/// <list type="bullet">
		/// <item>A penalty is applied when the bin hasn't been turned for <see cref="TurnInterval"/> hours, starting at 0% and scaling to 50% after <c>TurnInterval * 2</c> hours</item>
		/// <item>A penalty is applied for wetness above or below optimal value, starting at 0% when <c>Wetness</c> is at 0.25 or 0.75 and scaling to 50% when <c>Wetness</c> is at 0 or 1</item>
		/// </list>
		/// </summary>
		private void CalculateDecompRate(double timestamp = 0)
		{
			if (timestamp == 0)
				timestamp = Api.World.Calendar.TotalHours;
			float baseRate = IdealDecompositionRate;
			var lastTurned = timestamp - LastTurn;
			if (lastTurned >= TurnInterval)
			{
				float turnMult = (float)((TurnInterval * 2 / Math.Min(TurnInterval * 2, lastTurned)) - 1);
				baseRate *= Math.Max(0.5f, turnMult);
			}
			var wetnessDiff = Math.Abs(Wetness - 0.5f);
			if (wetnessDiff > 0.25f)
				baseRate *= Math.Max(0.5f, 1 - (2 * wetnessDiff));
			CachedDecompositionRate = Math.Max(IdealDecompositionRate * 0.25f, (float)Math.Round(baseRate, 4));
		}
		#endregion
	}
}
