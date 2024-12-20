using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace StackSellPrice;

public class Plugin : IDalamudPlugin
{
	private bool disposed;

	[PluginService] public static IDataManager GameData { get; private set; } = null!;
	[PluginService] public static IPluginLog Log { get; private set; } = null!;
	[PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

	public Plugin()
	{
		AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "ItemDetail", ModifyTooltip);

		Log.Information("Registered tooltip construction handler!");
	}

	private unsafe void ModifyTooltip(AddonEvent _, AddonArgs args) {
		var atkBase = (AtkUnitBase*)args.Addon;
		if (atkBase == null || !atkBase->IsVisible)
			return;

		var shopSellPriceAtk = atkBase->GetTextNodeById(48);
		if (shopSellPriceAtk == null || !shopSellPriceAtk->IsVisible())
			return;

		var itemId = AgentItemDetail.Instance()->ItemId;
		// 0: nothing
		// 1 - 499,999: NQ
		// 500,000 - 999,999: collectibles
		// 1,000,000 - 1,499,999: HQ
		// 1,500,000 - 1,999,999: -n/a-
		// 2,000,000+: quest/event
		if (itemId is not (>= 1 and < 500_000) and not (>= 1_000_000 and < 1_500_000))
			return;

		var hq = false;
		if (itemId is >= 1_000_000 and < 1_500_000)
		{
			itemId -= 1_000_000;
			hq = true;
		}
		else if (itemId is (>= 5604 and <= 5723) or (>= 18006 and <= 18029) or (>= 25186 and <= 25198) or (>= 26727 and <= 26739) or (>= 33917 and <= 33942))
		{
			hq = true; // hack for materia prices being handled as if they're HQ even though they can't actually /be/ HQ, thanks SE, what the fuck
		}

		double price = GameData.GetExcelSheet<Item>().GetRow(itemId).PriceLow;
		if (price <= 0)
			return;

		if (hq)
			price += Math.Ceiling(price / 10);

		var quantityAtk = atkBase->GetTextNodeById(34);
		if (quantityAtk == null || !quantityAtk->IsVisible())
			return;

		var quantityLine = new ReadOnlySeString(quantityAtk->NodeText).ExtractText();
		uint quantity;
		var parts = quantityLine.Split('/');
		if (parts.Length > 1)
		{
			if (!uint.TryParse(parts[0], out quantity))
				return;
		}
		else
		{
			return;
		}

		if (quantity > 1)
		{
			var builder = new SeStringBuilder();
			builder
				.AddText($"{price}{SeIconChar.Gil.ToIconString()}")
				.AddUiForeground($" (x{quantity:N0} = ", 3)
				.AddUiForeground($"{price * quantity:N0}{SeIconChar.Gil.ToIconString()}", 529)
				.AddUiForeground(")", 3);

			shopSellPriceAtk->SetText(builder.BuiltString.EncodeWithNullTerminator());
		}
	}

	#region IDisposable
	protected virtual void Dispose(bool disposing)
	{
		if (disposed)
			return;

		disposed = true;

		if (disposing)
		{
			AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, "ItemDetail", ModifyTooltip);
			Log.Information("Unregistered tooltip construction handler!");
		}

		Log.Information("Goodbye friend :)");
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
	#endregion
}
