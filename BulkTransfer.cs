using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class BulkTransferRow
    {
        public GameItem Item { get; }
        public string Name { get; }
        public int Units { get; }
        public int PlannedUnits { get; internal set; }
        public bool CanMove => PlannedUnits > 0;
        public string Reason { get; internal set; }

        internal BulkTransferRow(GameItem item)
        {
            Item = item;
            Name = item.GetDisplayName();
            Units = item.unitCount;
        }
    }

    internal sealed class BulkTransferResult
    {
        public int MovedUnits { get; }
        public string StoppedReason { get; }
        public bool Completed => string.IsNullOrEmpty(StoppedReason);

        internal BulkTransferResult(int movedUnits, string stoppedReason = null)
        { MovedUnits = movedUnits; StoppedReason = stoppedReason; }
    }

    internal sealed class BulkTransferPlan
    {
        private readonly List<BulkTransferStep> steps;
        private readonly BulkTransferSnapshot snapshot;
        private readonly GameInventory[] inventories;
        private bool consumed;

        public GameInventory Target { get; }
        public IReadOnlyList<BulkTransferRow> Rows { get; }
        public string UnavailableReason { get; }
        public int PlannedUnits => Rows.Sum(row => row.PlannedUnits);
        public int BlockedUnits => Rows.Sum(row => row.Units - row.PlannedUnits);
        public bool CanExecute => !consumed && steps.Count > 0 && string.IsNullOrEmpty(UnavailableReason);

        internal BulkTransferPlan(GameInventory target, GameInventory[] inventories,
            List<BulkTransferRow> rows, List<BulkTransferStep> steps, string unavailableReason)
        {
            Target = target;
            this.inventories = inventories;
            Rows = rows.AsReadOnly();
            this.steps = steps;
            UnavailableReason = unavailableReason;
            snapshot = new BulkTransferSnapshot(inventories);
        }

        public BulkTransferResult Execute(IEnumerable<GameInventory> currentAccessibleInventories)
        {
            if (!CanExecute) return new BulkTransferResult(0, UnavailableReason ?? ModText.Get("bulk.preview_expired"));
            consumed = true;
            var current = BulkTransfer.Inventories(currentAccessibleInventories);
            if (!new HashSet<GameInventory>(current).SetEquals(inventories) || !snapshot.Matches())
                return new BulkTransferResult(0, ModText.Get("bulk.preview_expired"));

            var refreshed = BulkTransfer.Preview(Target, current);
            if (!SameSteps(refreshed.steps))
                return new BulkTransferResult(0, ModText.Get("bulk.preview_expired"));

            int moved = 0;
            foreach (var step in steps)
            {
                if (step.Item.parentInventory != step.SourceInventory || !BulkTransfer.MayMove(step.Item, Target) ||
                    step.Item.MaxNumRemove() < step.Units)
                    return Stopped(moved, step.Item);
                SlotMarker slot;
                if (step.StackItem != null || step.StackPlacement != null)
                {
                    var destination = step.StackItem ?? step.StackPlacement.AcceptedItem;
                    if (destination == null || destination.parentInventory != Target ||
                        !destination.MayStack(step.Item)) return Stopped(moved, step.Item);
                    slot = destination.GetStackSlot(step.Item);
                }
                else
                {
                    if (Target is GameGridScrollableInventory scrollable && scrollable.alwaysStack &&
                        Target.childItems.Any(existing => existing.MayStack(step.Item))) return Stopped(moved, step.Item);
                    slot = Target.TryInventorySlot(step.Item, step.Units, step.Shape);
                    if (slot != null && !BulkShape.Equal(slot.itemGridShape, step.Shape)) return Stopped(moved, step.Item);
                }
                if (slot == null || slot.inventory != Target || slot.numTransfer < step.Units)
                    return Stopped(moved, step.Item);
                int accepted = slot.TryAcceptOnce(step.Units);
                if (accepted != step.Units)
                    throw new InvalidOperationException(ModText.Get("bulk.native_failure", step.Item.GetDisplayName(), moved, accepted, step.Units));
                step.AcceptedItem = slot.targetItem ?? slot.item;
                moved += accepted;
            }
            return new BulkTransferResult(moved);
        }

        private static BulkTransferResult Stopped(int moved, GameItem item) =>
            new BulkTransferResult(moved, ModText.Get("bulk.stopped", item.GetDisplayName()));

        private bool SameSteps(IReadOnlyList<BulkTransferStep> other)
        {
            if (steps.Count != other.Count) return false;
            var otherList = other.ToList();
            for (int i = 0; i < steps.Count; i++)
            {
                var a = steps[i]; var b = other[i];
                if (a.Item != b.Item || a.StackItem != b.StackItem || a.Units != b.Units ||
                    !BulkShape.Equal(a.Shape, b.Shape) ||
                    steps.IndexOf(a.StackPlacement) != otherList.IndexOf(b.StackPlacement)) return false;
            }
            return true;
        }
    }

    internal static class BulkTransfer
    {
        public static BulkTransferPlan Preview(GameInventory target, IEnumerable<GameInventory> accessibleInventories)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var inventories = Inventories(accessibleInventories);
            var rows = new List<BulkTransferRow>();
            var steps = new List<BulkTransferStep>();
            string unavailable = !inventories.Contains(target) || !GeneralHelper.MayPlayerInsertInto(target) || target.IsInsertLocked()
                ? ModText.Get("bulk.target_unavailable") : null;
            if (unavailable != null) return new BulkTransferPlan(target, inventories, rows, steps, unavailable);
            var kinds = new HashSet<string>(target.childItems.Select(item => item.identifier).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            if (kinds.Count == 0)
                return new BulkTransferPlan(target, inventories, rows, steps, ModText.Get("bulk.target_empty"));

            var reservations = target.childItems.Select(item => new BulkStackReservation(item)).ToList();
            var occupied = new BulkPlacementReservations();
            var candidates = inventories.Where(inventory => inventory != target).SelectMany(inventory => inventory.childItems)
                .Distinct().Where(item => item.parentInventory != target && kinds.Contains(item.identifier))
                .OrderBy(item => item.identifier, StringComparer.Ordinal).ThenBy(item => item.uniqueId).ToList();
            foreach (var item in candidates)
            {
                var row = new BulkTransferRow(item);
                rows.Add(row);
                if (!MayMove(item, target)) { row.Reason = ModText.Get("bulk.restricted"); continue; }
                int remaining = Math.Min(item.unitCount, item.MaxNumRemove());
                foreach (var reservation in reservations)
                {
                    if (remaining <= 0) break;
                    int available = reservation.Available(item, target);
                    int amount = Math.Min(remaining, available);
                    if (amount <= 0) continue;
                    steps.Add(new BulkTransferStep(item, amount, reservation.Item, reservation.Placement));
                    reservation.ReservedUnits += amount;
                    row.PlannedUnits += amount;
                    remaining -= amount;
                }
                while (remaining > 0)
                {
                    if (target is GameGridScrollableInventory scrollable && scrollable.alwaysStack &&
                        reservations.Any(reservation => reservation.Compatible(item))) break;
                    var slot = FindPlacement(target, item, remaining, occupied);
                    if (slot == null || slot.numTransfer <= 0) break;
                    int amount = Math.Min(remaining, slot.numTransfer);
                    var step = new BulkTransferStep(item, amount, slot.itemGridShape.Clone());
                    steps.Add(step);
                    occupied.Add(step.Shape);
                    reservations.Add(new BulkStackReservation(step));
                    row.PlannedUnits += amount;
                    remaining -= amount;
                }
                if (row.PlannedUnits < row.Units)
                    row.Reason = ModText.Get(row.PlannedUnits == 0 ? "bulk.no_space" : "bulk.partial", row.Units - row.PlannedUnits);
            }
            if (rows.Count == 0) unavailable = ModText.Get("bulk.no_matches");
            return new BulkTransferPlan(target, inventories, rows, steps, unavailable);
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

        private static SlotMarker FindPlacement(GameInventory target, GameItem item, int amount, BulkPlacementReservations occupied)
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

    internal sealed class BulkTransferStep
    {
        internal readonly GameItem Item, StackItem;
        internal readonly GameInventory SourceInventory;
        internal readonly BulkTransferStep StackPlacement;
        internal readonly int Units;
        internal readonly GridShape Shape;
        internal GameItem AcceptedItem;

        internal BulkTransferStep(GameItem item, int units, GridShape shape)
        { Item = item; SourceInventory = item.parentInventory; Units = units; Shape = shape; }

        internal BulkTransferStep(GameItem item, int units, GameItem stackItem, BulkTransferStep stackPlacement)
        { Item = item; SourceInventory = item.parentInventory; Units = units; StackItem = stackItem; StackPlacement = stackPlacement; }
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

        internal BulkTransferSnapshot(IEnumerable<GameInventory> source)
        {
            player = PlayerStore.Instance;
            identity = player == null ? null : player.runID + ":" + player.saveSlotId;
            scene = StorageDiscovery.SceneKey;
            var sourceList = source.ToList();
            inventories = sourceList.Select(inventory => new BulkInventorySnapshot(inventory)).ToArray();
            items = sourceList.SelectMany(inventory => inventory.childItems.Concat(GraphUtils.FindParentsType<GameItem>(inventory)))
                .Distinct().Select(item => new BulkItemSnapshot(item)).ToArray();
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
        private readonly GridShape shape;
        private readonly JToken state, modifiedState;

        internal BulkItemSnapshot(GameItem item)
        {
            this.item = item; inventory = item.parentInventory; units = item.unitCount; id = item.uniqueId; identifier = item.identifier;
            shape = item.modifiedShape?.Clone();
            state = item.state == null ? null : JToken.FromObject(item.state.dict);
            modifiedState = item.modifiedState == null ? null : JToken.FromObject(item.modifiedState.dict);
        }

        internal bool Matches() => (!(item is GameItemElement element) || !element.IsDestroyed()) &&
            inventory == item.parentInventory && units == item.unitCount && id == item.uniqueId && identifier == item.identifier &&
            BulkShape.Equal(shape, item.modifiedShape) &&
            JToken.DeepEquals(state, item.state == null ? null : JToken.FromObject(item.state.dict)) &&
            JToken.DeepEquals(modifiedState, item.modifiedState == null ? null : JToken.FromObject(item.modifiedState.dict));
    }
}
