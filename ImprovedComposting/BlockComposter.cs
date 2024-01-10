using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ImprovedComposting
{
	/// <summary>
	/// Block class for the compost bin block. This class is more or less empty, with the sole exception of handling interaction popups.
	/// </summary>
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
			else if (_composter.Layers < BlockEntityComposter.LayersNeeded)
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
					MouseButton = EnumMouseButton.Right,
					HotKeyCode = "shift"
				});
			}
			return interactions;
		}
	}
}
