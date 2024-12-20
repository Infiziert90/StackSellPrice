using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.Payloads;

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

		var agent = AgentItemDetail.Instance();
		var sortModule = ItemOrderModule.Instance();
		var inventoryAgent = InventoryManager.Instance();

		if (agent->ItemKind != ItemDetailKind.InventoryItem)
			return;

		// - 48: Inventory1
		// - 49: Inventory2
		// - 50: Inventory3
		// - 51: Inventory4
		// - 52: RetainerInventory1
		// - 53: RetainerInventory2
		// - 54: RetainerInventory3
		// - 55: RetainerInventory4
		// - 56: RetainerInventory5
		// - 69: SaddleBag1
		// - 70: SaddleBag2
		// - 71: PremiumSaddleBag1
		// - 72: PremiumSaddleB
		var inventoryType = agent->TypeOrId switch
		{
			48 => InventoryType.Inventory1,
			49 => InventoryType.Inventory2,
			50 => InventoryType.Inventory3,
			51 => InventoryType.Inventory4,
			52 or 53 or 54 or
			55 or 56 => InventoryType.RetainerPage1,
			69 => InventoryType.SaddleBag1,
			70 => InventoryType.SaddleBag2,
			71 => InventoryType.PremiumSaddleBag1,
			72 => InventoryType.PremiumSaddleBag2,
			_ => (InventoryType)99999,
		};

		// Type was something we don't process
		if ((uint)inventoryType == 99999)
			return;

		var index = (int)agent->Index;
		if (inventoryType is InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4)
		{
			index += (int)inventoryType * 35;
			var sortedItem = sortModule->InventorySorter->Items[index];

			index = sortedItem.Value->Slot;
			inventoryType = InventoryType.Inventory1 + sortedItem.Value->Page;
		}

		if (inventoryType is InventoryType.RetainerPage1)
		{
			var retainerSort = sortModule->RetainerSorter[sortModule->ActiveRetainerId].Value;

			// - 52: RetainerInventory1
			var offsetMulti = agent->TypeOrId - 52;
			index += (int)(35 * offsetMulti);

			var sortedItem = retainerSort->Items[index];
			index = sortedItem.Value->Slot;
			inventoryType = InventoryType.RetainerPage1 + sortedItem.Value->Page;
		}

		var item = inventoryAgent->GetInventorySlot(inventoryType, index);
		if (item == null)
			return;

		var itemRow = GameData.GetExcelSheet<Item>().GetRow(item->ItemId);
		if (itemRow.StackSize <= 1)
			return;

		var hq = item->Flags == InventoryItem.ItemFlags.HighQuality;
		var price = (double)itemRow.PriceLow;
		if (price <= 0)
			return;

		if (hq)
			price += Math.Ceiling(price / 10);

		var quantityAtk = atkBase->GetTextNodeById(34);
		if (quantityAtk == null || !quantityAtk->IsVisible())
			return;

		var quantity = item->Quantity;
		if (quantity > 1)
		{
			var builder = new SeStringBuilder();
			var addonText = GameData.GetExcelSheet<Addon>().GetRow(484).Text;
			foreach (var payload in addonText)
			{
				if (payload.MacroCode != MacroCode.Kilo)
				{
					builder.Append(payload);
					continue;
				}

				builder
					.Append($"{price}")
					.PushColorType(3)
					.Append($" (x{quantity:N0} = ")
					.PushColorType(529)
					.Append($"{price * quantity:N0}")
					.PopColorType()
					.Append(")")
					.PopColorType();
			}

			shopSellPriceAtk->SetText(builder.ToArray());
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
