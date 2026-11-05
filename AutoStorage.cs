using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class AutoStorageDestination
    {
        public string Label { get; }
        public int Units { get; }
        public double Similarity { get; }
        internal AutoStorageDestination(string label, int units, double similarity)
        { Label = label; Units = units; Similarity = similarity; }
    }

    internal sealed class AutoStorageRow
    {
        public GameItem Item { get; }
        public string Name { get; }
        public int Units { get; }
        public int PlannedUnits { get; }
        public IReadOnlyList<AutoStorageDestination> Destinations { get; }
        public string Reason { get; }
        internal AutoStorageRow(GameItem item, IEnumerable<AutoStorageDestination> destinations, string reason)
        {
            Item = item; Name = item.GetDisplayName(true); Units = item.unitCount;
            Destinations = destinations.ToArray(); PlannedUnits = Destinations.Sum(destination => destination.Units);
            Reason = reason;
        }
    }

    internal sealed class AutoStoragePlan
    {
        public IReadOnlyList<AutoStorageRow> Rows { get; }
        public int PlannedUnits => Rows.Sum(row => row.PlannedUnits);
        public int BlockedUnits => Rows.Sum(row => row.Units - row.PlannedUnits);
        public string UnavailableReason { get; }
        public bool CanExecute => !consumed && string.IsNullOrEmpty(UnavailableReason) && PlannedUnits > 0;
        private readonly AutoStorageScope scope;
        private readonly AutoStorageTradeSnapshot trade;
        private readonly BulkTransferSnapshot snapshot;
        private readonly IReadOnlyList<BulkTransferStep> steps;
        private bool consumed;

        internal AutoStoragePlan(AutoStorageScope scope, IEnumerable<AutoStorageRow> rows,
            IReadOnlyList<BulkTransferStep> steps, string unavailableReason)
        {
            this.scope = scope; Rows = rows.ToArray();
            this.steps = steps; UnavailableReason = unavailableReason;
            if (scope != null)
            {
                snapshot = new BulkTransferSnapshot(scope.AllInventories, AutoStorage.TracksItemState);
                trade = new AutoStorageTradeSnapshot();
            }
        }

        public BulkTransferResult Execute(IEnumerable<StorageSource> currentSources)
        {
            if (consumed) return new BulkTransferResult(0, ModText.Get("bulk.used"));
            consumed = true;
            if (!string.IsNullOrEmpty(UnavailableReason)) return new BulkTransferResult(0, UnavailableReason);
            if (!AutoStorage.Available || !trade.Matches() || !snapshot.Matches())
                return new BulkTransferResult(0, ModText.Get("bulk.changed"));
            var currentScope = AutoStorage.Collect(currentSources);
            if (!scope.Same(currentScope)) return new BulkTransferResult(0, ModText.Get("bulk.changed"));
            var rebuilt = AutoStorage.Build(currentScope);
            if (!BulkTransfer.SameSteps(steps, rebuilt.Steps))
                return new BulkTransferResult(0, ModText.Get("bulk.changed"));
            return BulkTransfer.Execute(steps, target => AutoStorage.Available && trade.SameContext() &&
                currentScope.Targets.Any(container => container.Inventories.Contains(target) &&
                    AutoStorage.IsDestination(container.Source) &&
                    new StorageCatalog(new[] { container.Source }).Inventories.Contains(target)),
                item => currentScope.Sources.Contains(item.parentInventory) &&
                    (!AutoStorage.IsCounter(item.parentInventory) || PlayerStore.Instance.currentClientInstance == null));
        }
    }

    internal static class AutoStorage
    {
        private static readonly HashSet<string> Fixtures = new HashSet<string>(StringComparer.Ordinal)
            { "dossier", "trashcan", "cassette_player", "faucet", "drain" };

        internal static bool Available => EmporiumEntry.Instance != null && PlayerStore.Instance != null &&
            !SaveManager.IsDecoding && !ModHelper.IsOutsideStore();

        public static AutoStoragePlan Preview(IEnumerable<StorageSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (!Available) return new AutoStoragePlan(null, Array.Empty<AutoStorageRow>(),
                Array.Empty<BulkTransferStep>(), ModText.Get("auto.unavailable"));
            var scope = Collect(sources);
            var result = Build(scope);
            string reason = scope.Sources.Count == 0 ? ModText.Get("auto.no_sources") :
                result.Rows.Count == 0 ? ModText.Get("auto.no_items") : null;
            return new AutoStoragePlan(scope, result.Rows, result.Steps, reason);
        }

        internal static AutoStorageScope Collect(IEnumerable<StorageSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var sourceList = sources.OrderBy(source => source.Id, StringComparer.Ordinal).ToArray();
            var entry = EmporiumEntry.Instance;
            var loose = new List<GameInventory>();
            void AddRoot(string id, PixelWindow window, GameInventory inventory)
            {
                if (sourceList.Any(source => source.Id == id && source.Owner == null && source.Window == window &&
                    !window.isLocked && new StorageCatalog(new[] { source }).Inventories.Contains(inventory)))
                    loose.Add(inventory);
            }
            AddRoot("root:main", entry.invWindow, entry.invElement);
            AddRoot("root:front", entry.frontInvWindow, entry.frontInvinvElement);
            AddRoot("root:back", entry.backInvWindow, entry.backInvinvElement);
            var targets = new List<AutoStorageContainer>();
            foreach (var source in sourceList.Where(IsDestination))
            {
                var inventories = new StorageCatalog(new[] { source }).Inventories.ToArray();
                if (inventories.Length > 0) targets.Add(new AutoStorageContainer(source, inventories));
            }
            return new AutoStorageScope(loose, targets);
        }

        internal static bool IsDestination(StorageSource source)
        {
            if (source.Owner == null || source.Window == null || source.Window.isLocked) return false;
            var owner = source.Owner;
            return IsOrdinaryContainer(owner) && GeneralHelper.IsItemOwned(owner) &&
                OpenedContainers.CanExpose(owner) && owner.contentWindow == source.Window &&
                !GeneralHelper.IsItemLocked(owner) &&
                (PlayerStore.Instance.currentClientInstance == null ||
                    !GraphUtils.FindParentsType<GameInventory>(owner).Any(IsCounter)) &&
                !GraphUtils.FindParentsType<GameItem>(owner).Any(item => IsMachine(item) || IsFixture(item));
        }

        internal static bool IsCounter(GameInventory inventory) => inventory == EmporiumEntry.Instance.frontInvinvElement ||
            inventory == EmporiumEntry.Instance.backInvinvElement;

        private static bool IsMachine(GameItem item) => item.IsGameItemType(TypeHelper.MACHINE) || item.IsTag(MachineHelper.STANDARD_MACHINE_TAG);
        private static bool IsFixture(GameItem item) => Fixtures.Contains(item.identifier);
        private static bool HasContainerKind(GameItem item) => item.IsTag(ContainerHelper.CONTAINER_TAG) || item.IsTag(ContainerHelper.BACKPACK_TAG) ||
            item.IsTag(ContainerHelper.POUCH_TAG) || item.IsTag(ContainerHelper.SAFE_TAG) || item.IsGameItemType(TypeHelper.STORAGE);
        internal static bool IsOrdinaryContainer(GameItem item) => IsPresent(item) && HasContainerKind(item) &&
            !IsMachine(item) && !IsFixture(item);
        private static bool IsPresent(GameItem item) => item != null && item.unitCount > 0 &&
            (!(item is GameItemElement element) || !element.IsDestroyed());
        private static bool IsLooseItem(GameItem item) => IsPresent(item) && GeneralHelper.IsItemOwned(item) &&
            !HasContainerKind(item) && !IsMachine(item) && !IsFixture(item);
        internal static bool TracksItemState(GameItem item) => IsLooseItem(item) || IsOrdinaryContainer(item);

        internal static AutoStorageBuild Build(AutoStorageScope scope)
        {
            var rows = new List<AutoStorageRow>();
            var steps = new List<BulkTransferStep>();
            var destinations = scope.Targets.SelectMany(container => container.Inventories).Distinct()
                .ToDictionary(inventory => inventory, inventory => new BulkTransferDestination(inventory));
            var candidates = scope.Targets.Select(container => new StorageAffinityCandidate(container.Source.Id,
                StorageAffinity.Container(container.Inventories.SelectMany(inventory => inventory.childItems)
                    .Where(IsLooseItem).Select(item => new StorageAffinitySample(Vector(item), OccupiedCells(item)))))).ToArray();
            var byId = scope.Targets.ToDictionary(container => container.Source.Id, StringComparer.Ordinal);
            foreach (var inventory in scope.Sources)
            foreach (var item in inventory.childItems.Where(IsLooseItem).OrderBy(item => item.identifier, StringComparer.Ordinal)
                .ThenBy(item => item.uniqueId).ThenBy(item => item.modifiedShape.minY).ThenBy(item => item.modifiedShape.minX))
            {
                var rowDestinations = new List<AutoStorageDestination>();
                string reason = null;
                if (IsCounter(inventory) && PlayerStore.Instance.currentClientInstance != null)
                    reason = ModText.Get("auto.client_present");
                else
                {
                    var matches = StorageAffinity.Rank(Vector(item), candidates);
                    if (matches.Count == 0) reason = ModText.Get("auto.no_match");
                    else
                    {
                        int remaining = item.unitCount;
                        int allowance = Math.Min(remaining, item.MaxNumRemove());
                        bool permitted = false;
                        foreach (var match in matches)
                        {
                            var container = byId[match.Id];
                            int moved = 0;
                            foreach (var target in container.Inventories)
                            {
                                if (allowance <= 0) break;
                                if (BulkTransfer.MayMove(item, target)) permitted = true;
                                int allocated = destinations[target].Allocate(item, allowance, steps);
                                remaining -= allocated; allowance -= allocated; moved += allocated;
                            }
                            if (moved > 0) rowDestinations.Add(new AutoStorageDestination(container.Source.Label, moved, match.Similarity));
                            if (allowance <= 0) break;
                        }
                        if (remaining > 0) reason = ModText.Get(permitted && allowance > 0 ? "auto.capacity" : "bulk.restricted", remaining);
                    }
                }
                rows.Add(new AutoStorageRow(item, rowDestinations, reason));
            }
            return new AutoStorageBuild(rows, steps);
        }

        private static StorageAffinityVector Vector(GameItem item)
        {
            bool contraband = ContrabandHelper.IsContraband(item);
            // 原生等级查询会补建缺失标签，预览只读取已有标签。
            int level = contraband && item.modifiedState.dict.ContainsKey(ContrabandHelper.CONTRABAND_LEVEL)
                ? ContrabandHelper.GetContrabandLevel(item) : 0;
            return StorageAffinity.Item(item.GetGameItemType(), contraband, level, item.IsTag(StolenHelper.STOLEN_TAG),
                GeneralHelper.IsHazardousWaste(item));
        }

        private static int OccupiedCells(GameItem item)
        {
            int count = 0;
            for (int y = 0; y < item.modifiedShape.height; y++)
            for (int x = 0; x < item.modifiedShape.width; x++)
                if (item.modifiedShape.GetLocal(x, y) != 0) count++;
            if (count == 0) throw new InvalidOperationException("An item must occupy at least one cell.");
            return count;
        }
    }

    internal sealed class AutoStorageContainer
    {
        internal readonly StorageSource Source;
        internal readonly GameInventory[] Inventories;
        internal AutoStorageContainer(StorageSource source, GameInventory[] inventories) { Source = source; Inventories = inventories; }
    }

    internal sealed class AutoStorageScope
    {
        internal readonly IReadOnlyList<GameInventory> Sources;
        internal readonly IReadOnlyList<AutoStorageContainer> Targets;
        internal IEnumerable<GameInventory> AllInventories => Sources.Concat(Targets.SelectMany(target => target.Inventories)).Distinct();
        internal AutoStorageScope(IReadOnlyList<GameInventory> sources, IReadOnlyList<AutoStorageContainer> targets)
        { Sources = sources; Targets = targets; }
        internal bool Same(AutoStorageScope other) => Sources.SequenceEqual(other.Sources) && Targets.Count == other.Targets.Count &&
            Targets.Zip(other.Targets, (a, b) => a.Source.Id == b.Source.Id && a.Source.Owner == b.Source.Owner &&
                a.Source.Window == b.Source.Window && a.Inventories.SequenceEqual(b.Inventories)).All(same => same);
    }

    internal sealed class AutoStorageBuild
    {
        internal readonly IReadOnlyList<AutoStorageRow> Rows;
        internal readonly IReadOnlyList<BulkTransferStep> Steps;
        internal AutoStorageBuild(IReadOnlyList<AutoStorageRow> rows, IReadOnlyList<BulkTransferStep> steps)
        { Rows = rows; Steps = steps; }
    }

    internal sealed class AutoStorageTradeSnapshot
    {
        private readonly PlayerStore player = PlayerStore.Instance;
        private readonly EmporiumEntry entry = EmporiumEntry.Instance;
        private readonly string runId = PlayerStore.Instance.runID;
        private readonly int saveSlot = PlayerStore.Instance.saveSlotId;
        private readonly StoreClientInstance client = PlayerStore.Instance.currentClientInstance;
        private readonly StoreClient blueprint;
        private readonly StoreClient.ClientIntent intent;
        private readonly bool businessComplete, arrived;
        private readonly GameItem negotiatedItem;
        private readonly int budget;

        internal AutoStorageTradeSnapshot()
        {
            blueprint = client?.GetClientBlueprint();
            if (blueprint != null) { intent = blueprint.clientIntent; businessComplete = blueprint.isBusinessComplete; budget = blueprint.GetBudget(); }
            arrived = PlayerStore.Instance.isClientArrived;
            negotiatedItem = PlayerStore.Instance.currentNegociatedItem;
        }
        internal bool SameContext() => PlayerStore.Instance == player && EmporiumEntry.Instance == entry &&
            player.runID == runId && player.saveSlotId == saveSlot &&
            player.currentClientInstance == client && client?.GetClientBlueprint() == blueprint &&
            PlayerStore.Instance.isClientArrived == arrived &&
            (blueprint == null || blueprint.clientIntent == intent && blueprint.isBusinessComplete == businessComplete);
        internal bool Matches() => SameContext() && PlayerStore.Instance.currentNegociatedItem == negotiatedItem &&
            (blueprint == null || blueprint.GetBudget() == budget);
    }
}
