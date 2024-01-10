using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ImprovedComposting
{
	public class BlockComposter : Block
	{
		public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
		{
			if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityComposter be)
				return be.OnBlockInteractStart(byPlayer, blockSel);
			return base.OnBlockInteractStart(world, byPlayer, blockSel);
		}

		public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
		{
			BlockEntityComposter _composter = null;
			if (selection.Position != null)
				_composter = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityComposter;
			if (_composter == null)
				return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
			WorldInteraction[] interactions = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
			if (_composter.DecompositionProgress >= 1f)
			{
				interactions.Append(new WorldInteraction()
				{
					ActionLangCode = "blockhelp-composter-harvest",
					MouseButton = EnumMouseButton.Right
				});
			}
			else if (_composter.Layers < BlockEntityComposter.LayerCapacity)
			{
				interactions.Append(new WorldInteraction()
				{
					ActionLangCode = "blockhelp-composter-add",
					MouseButton = EnumMouseButton.Right,
					HotKeyCode = "shift"
				});
			}
			else
			{
				interactions.Append(new WorldInteraction()
				{
					ActionLangCode = _composter.LidClosed ? "blockhelp-composter-openlid" : "blockhelp-composter-closelid",
					MouseButton = EnumMouseButton.Right
				});
				interactions.Append(new WorldInteraction()
				{
					ActionLangCode = "blockhelp-composter-turn",
					MouseButton = EnumMouseButton.Right
				});
			}
			return interactions;
		}
	}
}
