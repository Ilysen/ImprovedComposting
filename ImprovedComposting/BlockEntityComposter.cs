using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImprovedComposting
{
	public class BlockEntityComposter : BlockEntityGeneric
	{
		private WeatherSystemBase _wsys;
		private Vec3d _tmpPos = new();

		public static readonly ushort NumberOfHoursRequired = 48;
		public static readonly ushort LayerCapacity = 10;
		public static readonly ushort TurnInterval = 72;
		public static readonly float WetnessGainInRainPerSecond = 0.0002f;
		public static readonly float WetnessLossPerTick = 0.004f;

		public int Layers;
		public float DecompositionProgress = 0;
		public float CachedDecompositionRate;
		public float Wetness = 0.5f;
		public double LastTurn;
		public double LastUpdate;
		public bool LidClosed;

		private float IdealDecompositionRate => (float)Math.Round(1f / NumberOfHoursRequired, 4);

		public float DaysLeft()
		{
			if (CachedDecompositionRate <= 0f)
				return -1;
			float daysReq = NumberOfHoursRequired / Api.World.Calendar.HoursPerDay;
			float daysElapsed = (DecompositionProgress * Api.World.Calendar.HoursPerDay) * (CachedDecompositionRate * Api.World.Calendar.HoursPerDay);
			return Math.Min(99, (float)Math.Round(daysReq - daysElapsed, 2));
		}

		public override void Initialize(ICoreAPI api)
		{
			base.Initialize(api);
			_wsys = api.ModLoader.GetModSystem<WeatherSystemBase>();
			CachedDecompositionRate = GetDecompositionRate();
			if (api.Side != EnumAppSide.Client)
				RegisterGameTickListener(TickPerSecond, 1000);
		}

		private void TickPerSecond(float dt)
		{
			if (Layers != LayerCapacity)
				return;
			if (Api.World.BlockAccessor.GetRainMapHeightAt(Pos) <= Pos.Y && LidClosed)
			{
				_tmpPos.Set(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
				float precip = _wsys.GetPrecipitation(_tmpPos);
				// When exposed to rain, wetness increases by 0.01% per second
				if (precip > 0.04)
				{
					Wetness = (float)Math.Round(Math.Min(1, Wetness + (precip * WetnessGainInRainPerSecond)), 4);
					MarkDirty();
				}
			}
			if (LastUpdate == Math.Floor(Api.World.Calendar.TotalHours))
				return;
			LastUpdate = Math.Floor(Api.World.Calendar.TotalHours);
			CachedDecompositionRate = GetDecompositionRate();
			DecompositionProgress += CachedDecompositionRate;
			// Reduce wetness by 0.4% per hour
			Wetness = (float)Math.Round(Math.Max(0, Wetness - WetnessLossPerTick), 4);
			MarkDirty();
		}

		public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
		{
			if (DecompositionProgress >= 1f)
			{
				dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-ready"));
			}
			else if (Layers < LayerCapacity)
			{
				if (Layers < Math.Floor((double)LayerCapacity / 2))
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsbrowns"));
				else
				{
					if (Layers % 2 == 0)
						dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsgreens"));
					else
						dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsbrowns"));
				}
			}
			else
			{
				dsc.AppendLine($"Wetness {Wetness}");
				dsc.AppendLine($"CachedDecompositionRate {CachedDecompositionRate}");
				dsc.AppendLine($"LastTurn {LastTurn}");
				dsc.AppendLine($"LastUpdate {LastUpdate}");
				dsc.AppendLine("");
				double decompRate = Math.Round(CachedDecompositionRate / IdealDecompositionRate * 100d, 2);
				string color = ColorUtil.Int2Hex(GuiStyle.DamageColorGradient[(int)Math.Min(99, Math.Max(1, decompRate))]);
				float daysLeft = DaysLeft();
				if (daysLeft == -1)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-halted"));
				else
				{
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-decompositionprogress", Math.Round(DecompositionProgress * 100f, 2), Math.Round(DaysLeft(), 2), 2));
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-decompositionrate", color, decompRate));
				}
				if (Api.World.Calendar.TotalHours - LastTurn >= TurnInterval)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-needsturning"));
				if (Wetness <= 0.25f)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-toodry"));
				else if (Wetness >= 0.75f)
					dsc.AppendLine(ImprovedCompostingSystem.GetLang("info-composter-toowet"));
			}
			base.GetBlockInfo(forPlayer, dsc);
		}

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
					}
				}
			}
			if (Layers == LayerCapacity)
			{
				if (hotbarSlot.Empty)
				{
					if (shift)
						Wetness += 0.1f;
					else
					{
						LidClosed = !LidClosed;
						if (!LidClosed)
							byPlayer.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/barrelopen"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
						else
							byPlayer.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/barrelclosed"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
					}
				}
				else if (hotbarSlot.Itemstack.Collectible?.Tool == EnumTool.Shovel)
				{
					LastTurn = Api.World.Calendar.TotalHours;
					byPlayer.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/dirt"), blockSel.Position.X + blockSel.HitPosition.X, blockSel.Position.Y + blockSel.HitPosition.Y, blockSel.Position.Z + blockSel.HitPosition.Z, byPlayer, true, 8);
				}
			}
			return true;
		}

		private float GetDecompositionRate()
		{
			float baseRate = IdealDecompositionRate;
			var lastTurned = Api.World.Calendar.TotalHours - LastTurn;
			if (lastTurned >= TurnInterval)
			{
				float turnMult = (float)(1 - (TurnInterval * 2 / Math.Min(TurnInterval * 2, lastTurned)));
				baseRate *= turnMult;
			}
			var wetnessDiff = Math.Abs(Wetness - 0.5f);
			if (wetnessDiff >= 0.25f)
			{
				baseRate *= 1 - (2 * wetnessDiff);
			}
			return Math.Max(IdealDecompositionRate * 0.25f, (float)Math.Round(baseRate, 4));
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
	}
}
