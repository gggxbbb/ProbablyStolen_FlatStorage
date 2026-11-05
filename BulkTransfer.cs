using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class BulkTransferResult
    {
        public int MovedUnits { get; }
        public string StoppedReason { get; }
        public bool Completed => string.IsNullOrEmpty(StoppedReason);

        internal BulkTransferResult(int movedUnits, string stoppedReason = null)
        { MovedUnits = movedUnits; StoppedReason = stoppedReason; }
    }

    internal static class BulkTransfer
    {
        internal static BulkTransferResult Execute(IReadOnlyList<BulkTransferStep> steps, Func<GameInventory, bool> targetAccessible,
            Func<GameItem, bool> sourceAccessible)
        {
            int moved = 0;
            var remainingUnits = steps.Select(step => step.Item).Distinct().ToDictionary(item => item, item => item.unitCount);
            foreach (var step in steps)
            {
                var target = step.Target;
                if (!targetAccessible(target) || !sourceAccessible(step.Item) || step.Item.parentInventory != step.SourceInventory ||
                    step.Item.uniqueId != step.ItemId || step.Item.identifier != step.Identifier ||
                    step.Item.unitCount != remainingUnits[step.Item] || !MayMove(step.Item, target) ||
                    step.Item.MaxNumRemove() < step.Units)
                    return Stopped(moved, step.Item);
                SlotMarker slot;
                if (step.StackItem != null || step.StackPlacement != null)
                {
                    var destination = step.StackItem ?? step.StackPlacement.AcceptedItem;
                    if (destination == null || destination.parentInventory != target ||
                        !destination.MayStack(step.Item)) return Stopped(moved, step.Item);
                    slot = destination.GetStackSlot(step.Item);
                }
                else
                {
                    if (target is GameGridScrollableInventory scrollable && scrollable.alwaysStack &&
                        target.childItems.Any(existing => existing.MayStack(step.Item))) return Stopped(moved, step.Item);
                    slot = target.TryInventorySlot(step.Item, step.Units, step.Shape);
                    if (slot != null && !BulkShape.Equal(slot.itemGridShape, step.Shape)) return Stopped(moved, step.Item);
                }
                if (slot == null || slot.inventory != target || slot.numTransfer < step.Units)
                    return Stopped(moved, step.Item);
                int accepted = slot.TryAcceptOnce(step.Units);
                if (accepted != step.Units)
                    throw new InvalidOperationException(ModText.Get("bulk.native_failure", step.Item.GetDisplayName(), moved, accepted, step.Units));
                step.AcceptedItem = slot.targetItem ?? slot.item;
                remainingUnits[step.Item] -= accepted;
                moved += accepted;
            }
            return new BulkTransferResult(moved);
        }

        private static BulkTransferResult Stopped(int moved, GameItem item) =>
            new BulkTransferResult(moved, ModText.Get("bulk.stopped", item.GetDisplayName()));

        internal static bool SameSteps(IReadOnlyList<BulkTransferStep> steps, IReadOnlyList<BulkTransferStep> other)
        {
            if (steps.Count != other.Count) return false;
            var stepList = steps.ToList();
            var otherList = other.ToList();
            for (int i = 0; i < steps.Count; i++)
            {
                var a = steps[i]; var b = other[i];
                if (a.Item != b.Item || a.Target != b.Target || a.SourceInventory != b.SourceInventory || a.StackItem != b.StackItem || a.Units != b.Units ||
                    !BulkShape.Equal(a.Shape, b.Shape) ||
                    stepList.IndexOf(a.StackPlacement) != otherList.IndexOf(b.StackPlacement)) return false;
            }
            return true;
        }
        internal static GameInventory[] Inventories(IEnumerable<GameInventory> inventories)
        {
            if (inventories == null) throw new ArgumentNullException(nameof(inventories));
            var result = inventories.Distinct().ToArray();
            if (result.Any(inventory => inventory == null)) throw new ArgumentException("Inventory references must be present.", nameof(inventories));
            return result;
        }

        internal static bool MayMove(GameItem item, GameInventory target)
        {
            if (item == null || item.parentInventory == null || item.parentInventory == target || item.unitCount <= 0 ||
                item is GameItemElement element && element.IsDestroyed()) return false;
            if (GraphUtils.FindParentEquals(target, item) || !GeneralHelper.IsItemOwned(item) ||
                ItemPinHelper.IsPinned(item) || GeneralHelper.IsItemLocked(item)) return false;
            if (GraphUtils.FindParentsType<PixelWindow>(target).Any(window => window.isLocked) ||
                GraphUtils.FindParentsType<PixelWindow>(item.parentInventory).Any(window => window.isLocked)) return false;
            return GeneralHelper.MayPlayerInsertInto(target) && !target.IsInsertLocked() &&
                item.MayRemove() && target.MayHaveValidInventorySlot(item) && item.MaxNumRemove() > 0;
        }

        internal static SlotMarker FindPlacement(GameInventory target, GameItem item, int amount, BulkPlacementReservations occupied)
        {
            int width, height;
            if (target is GameGridInventory grid) { width = grid.inventoryShape.width; height = grid.inventoryShape.height; }
            else if (target is GameGridScrollableInventory scrollable) { width = scrollable.width; height = scrollable.height; }
            else
            {
                if (occupied.Count > 0) return null;
                var slot = target.TryInventorySlot(item, amount, item.modifiedShape);
                return slot != null && slot.numTransfer > 0 ? slot : null;
            }
            int turns = EmporiumOption.SortAutoRotate ? 4 : 1;
            for (int turn = 0; turn < turns; turn++)
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                GridShape shape = new GridShapeBuilder(item.modifiedShape)
                    .SetTransform(x, y, item.modifiedShape.flipped, (item.modifiedShape.orientation + turn) % 4).Build();
                if (occupied.Intersects(shape)) continue;
                var slot = target.TryInventorySlot(item, amount, shape);
                if (slot != null && slot.numTransfer > 0) return slot;
            }
            return null;
        }
    }

    internal sealed class BulkTransferDestination
    {
        internal readonly GameInventory Target;
        private readonly List<BulkStackReservation> reservations;
        private readonly BulkPlacementReservations occupied = new BulkPlacementReservations();

        internal BulkTransferDestination(GameInventory target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            reservations = target.childItems.Select(item => new BulkStackReservation(item)).ToList();
        }

        internal int Allocate(GameItem item, int requested, List<BulkTransferStep> steps)
        {
            if (!BulkTransfer.MayMove(item, Target)) return 0;
            int remaining = Math.Min(requested, item.MaxNumRemove());
            int available = remaining;
            foreach (var reservation in reservations)
            {
                if (remaining <= 0) break;
                int amount = Math.Min(remaining, reservation.Available(item, Target));
                if (amount <= 0) continue;
                steps.Add(new BulkTransferStep(item, Target, amount, reservation.Item, reservation.Placement));
                reservation.ReservedUnits += amount;
                remaining -= amount;
            }
            while (remaining > 0)
            {
                if (Target is GameGridScrollableInventory scrollable && scrollable.alwaysStack &&
                    reservations.Any(reservation => reservation.Compatible(item))) break;
                var slot = BulkTransfer.FindPlacement(Target, item, remaining, occupied);
                if (slot == null || slot.numTransfer <= 0) break;
                int amount = Math.Min(remaining, slot.numTransfer);
                var step = new BulkTransferStep(item, Target, amount, slot.itemGridShape.Clone());
                steps.Add(step); occupied.Add(step.Shape);
                reservations.Add(new BulkStackReservation(step));
                remaining -= amount;
            }
            return available - remaining;
        }
    }

    internal sealed class BulkTransferStep
    {
        internal readonly GameItem Item, StackItem;
        internal readonly GameInventory SourceInventory, Target;
        internal readonly BulkTransferStep StackPlacement;
        internal readonly int Units;
        internal readonly int ItemId;
        internal readonly string Identifier;
        internal readonly GridShape Shape;
        internal GameItem AcceptedItem;

        internal BulkTransferStep(GameItem item, GameInventory target, int units, GridShape shape)
        { Item = item; ItemId = item.uniqueId; Identifier = item.identifier; Target = target; SourceInventory = item.parentInventory; Units = units; Shape = shape; }

        internal BulkTransferStep(GameItem item, GameInventory target, int units, GameItem stackItem, BulkTransferStep stackPlacement)
        { Item = item; ItemId = item.uniqueId; Identifier = item.identifier; Target = target; SourceInventory = item.parentInventory; Units = units; StackItem = stackItem; StackPlacement = stackPlacement; }
    }

    internal sealed class BulkStackReservation
    {
        internal readonly GameItem Item;
        internal readonly BulkTransferStep Placement;
        internal int ReservedUnits;

        internal BulkStackReservation(GameItem item) { Item = item; }
        internal BulkStackReservation(BulkTransferStep placement) { Placement = placement; ReservedUnits = placement.Units; }

        internal bool Compatible(GameItem item) => (Item ?? Placement.Item).MayStack(item);

        internal int Available(GameItem item, GameInventory target)
        {
            if (!Compatible(item)) return 0;
            if (Item != null)
            {
                var slot = Item.GetStackSlot(item);
                return slot == null || slot.inventory != target ? 0 : Math.Max(0, slot.numTransfer - ReservedUnits);
            }
            var placement = target.TryInventorySlot(item, item.unitCount, Placement.Shape);
            if (placement == null || placement.numTransfer <= 0) return 0;
            return Math.Max(0, item.MaxNumAdd(placement) - ReservedUnits);
        }
    }

    internal sealed class BulkPlacementReservations
    {
        private readonly List<GridShape> shapes = new List<GridShape>();
        internal int Count => shapes.Count;
        internal bool Intersects(GridShape shape) => shapes.Any(reserved => reserved.Intersects(shape));
        internal void Add(GridShape shape) { shapes.Add(shape.Clone()); }
    }

    internal static class BulkShape
    {
        internal static bool Equal(GridShape a, GridShape b)
        {
            if (a == null || b == null) return a == b;
            if (a.minX != b.minX || a.minY != b.minY || a.width != b.width || a.height != b.height ||
                a.orientation != b.orientation || a.flipped != b.flipped || a.GetOutsideBounds() != b.GetOutsideBounds()) return false;
            for (int y = 0; y < a.height; y++)
            for (int x = 0; x < a.width; x++)
                if (a.GetLocal(x, y) != b.GetLocal(x, y)) return false;
            return true;
        }
    }

    internal sealed class BulkTransferSnapshot
    {
        private readonly PlayerStore player;
        private readonly string identity, scene;
        private readonly BulkInventorySnapshot[] inventories;
        private readonly BulkItemSnapshot[] items;
        private readonly KeyValuePair<PixelWindow, bool>[] windows;

        internal BulkTransferSnapshot(IEnumerable<GameInventory> source, Func<GameItem, bool> observeState)
        {
            player = PlayerStore.Instance;
            identity = player == null ? null : player.runID + ":" + player.saveSlotId;
            scene = StorageDiscovery.SceneKey;
            var sourceList = source.ToList();
            inventories = sourceList.Select(inventory => new BulkInventorySnapshot(inventory)).ToArray();
            items = sourceList.SelectMany(inventory => inventory.childItems.Concat(GraphUtils.FindParentsType<GameItem>(inventory)))
                .Distinct().Select(item => new BulkItemSnapshot(item, observeState(item))).ToArray();
            windows = sourceList.SelectMany(inventory => GraphUtils.FindParentsType<PixelWindow>(inventory)).Distinct()
                .Select(window => new KeyValuePair<PixelWindow, bool>(window, window.isLocked)).ToArray();
        }

        internal bool Matches() => player == PlayerStore.Instance && player != null && !SaveManager.IsDecoding &&
            identity == player.runID + ":" + player.saveSlotId && scene == StorageDiscovery.SceneKey &&
            inventories.All(inventory => inventory.Matches()) && items.All(item => item.Matches()) &&
            windows.All(window => window.Key.isLocked == window.Value);
    }

    internal sealed class BulkInventorySnapshot
    {
        private readonly GameInventory inventory;
        private readonly GraphNodeStorage parent;
        private readonly GameItem[] items;
        private readonly bool insertLocked, removeLocked;
        private readonly JToken state;
        private readonly GridShape shape;
        private readonly int width, height;

        internal BulkInventorySnapshot(GameInventory inventory)
        {
            this.inventory = inventory; parent = inventory.parent;
            items = inventory.childItems.ToArray();
            insertLocked = inventory.IsInsertLocked(); removeLocked = inventory.IsRemoveLocked();
            state = JToken.FromObject(inventory.dict);
            if (inventory is GameGridInventory grid) shape = grid.inventoryShape.Clone();
            if (inventory is GameGridScrollableInventory scrollable) { width = scrollable.width; height = scrollable.height; }
        }

        internal bool Matches() => inventory.parent == parent && new HashSet<GameItem>(inventory.childItems).SetEquals(items) &&
            inventory.IsInsertLocked() == insertLocked && inventory.IsRemoveLocked() == removeLocked &&
            JToken.DeepEquals(state, JToken.FromObject(inventory.dict)) &&
            (!(inventory is GameGridInventory grid) || BulkShape.Equal(shape, grid.inventoryShape)) &&
            (!(inventory is GameGridScrollableInventory scrollable) || width == scrollable.width && height == scrollable.height);
    }

    internal sealed class BulkItemSnapshot
    {
        private readonly GameItem item;
        private readonly GameInventory inventory;
        private readonly int units, id;
        private readonly string identifier;
        private readonly string[] types;
        private readonly bool observeState;
        private readonly GridShape shape;
        private readonly JToken state, modifiedState, features;
        private static readonly JsonSerializer FeatureSerializer = JsonSerializer.Create(new JsonSerializerSettings
            { ContractResolver = new BulkFeatureContractResolver() });

        internal BulkItemSnapshot(GameItem item, bool observeState)
        {
            this.item = item; inventory = item.parentInventory; units = item.unitCount; id = item.uniqueId; identifier = item.identifier;
            this.observeState = observeState;
            shape = item.modifiedShape?.Clone();
            // 机器保持身份与占格检查，其运行进度不参与普通物品收纳。
            if (observeState)
            {
                types = item.GetGameItemType().OrderBy(type => type, StringComparer.Ordinal).ToArray();
                state = item.state == null ? null : JToken.FromObject(item.state.dict);
                modifiedState = item.modifiedState == null ? null : JToken.FromObject(item.modifiedState.dict);
                features = JToken.FromObject(item.itemFeatures, FeatureSerializer);
            }
        }

        internal bool Matches() => (!(item is GameItemElement element) || !element.IsDestroyed()) &&
            inventory == item.parentInventory && units == item.unitCount && id == item.uniqueId && identifier == item.identifier &&
            BulkShape.Equal(shape, item.modifiedShape) &&
            (!observeState || types.SequenceEqual(item.GetGameItemType().OrderBy(type => type, StringComparer.Ordinal)) &&
            JToken.DeepEquals(state, item.state == null ? null : JToken.FromObject(item.state.dict)) &&
            JToken.DeepEquals(modifiedState, item.modifiedState == null ? null : JToken.FromObject(item.modifiedState.dict)) &&
            JToken.DeepEquals(features, JToken.FromObject(item.itemFeatures, FeatureSerializer)));
    }

}
