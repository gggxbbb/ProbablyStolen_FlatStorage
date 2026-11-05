using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class StorageAffinityVector
    {
        internal readonly IReadOnlyDictionary<string, double> Values;
        internal readonly IReadOnlyCollection<string> Categories;
        internal readonly string Signature;

        internal StorageAffinityVector(IDictionary<string, double> values, IEnumerable<string> categories)
        {
            var sorted = new SortedDictionary<string, double>(values, StringComparer.Ordinal);
            Values = sorted;
            Categories = categories.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Signature = string.Join("|", sorted.Select(pair => pair.Key + "=" + pair.Value.ToString("R", CultureInfo.InvariantCulture)));
        }
    }

    internal readonly struct StorageAffinitySample
    {
        internal readonly StorageAffinityVector Vector;
        internal readonly int OccupiedCells;
        internal StorageAffinitySample(StorageAffinityVector vector, int occupiedCells)
        {
            Vector = vector ?? throw new ArgumentNullException(nameof(vector));
            if (occupiedCells <= 0) throw new ArgumentOutOfRangeException(nameof(occupiedCells));
            OccupiedCells = occupiedCells;
        }
    }

    internal sealed class StorageAffinityCandidate
    {
        internal readonly string Id;
        internal readonly StorageAffinityVector Vector;
        internal StorageAffinityCandidate(string id, StorageAffinityVector vector)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A container identity is required.", nameof(id));
            Id = id; Vector = vector ?? throw new ArgumentNullException(nameof(vector));
        }
    }

    internal readonly struct StorageAffinityMatch
    {
        internal readonly string Id;
        internal readonly double Similarity;
        internal StorageAffinityMatch(string id, double similarity) { Id = id; Similarity = similarity; }
    }

    internal static class StorageAffinity
    {
        private static readonly HashSet<string> AttributeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "CONTRABAND", "RESTRICTED_1", "RESTRICTED_2", "RESTRICTED_3", "MAKESHIFT", "LOWTECH",
            "MIDLINE", "HIGHEND", "RARE", "LUXURY_ITEM", "NOT_FOR_RESALE", "MODDABLE",
            "ALL_TYPE", "ALL_TYPE_EXCEPT_CONTRABAND", "STORAGE", "SUPPLY_CRATE", "MACHINE", "POUCH_TAG"
        };

        internal static StorageAffinityVector Item(IEnumerable<string> itemTypes, bool contraband,
            int contrabandLevel, bool stolen, bool hazardous)
        {
            if (itemTypes == null) throw new ArgumentNullException(nameof(itemTypes));
            if (contrabandLevel < 0 || contrabandLevel > 4) throw new ArgumentOutOfRangeException(nameof(contrabandLevel));
            var types = itemTypes.Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.Ordinal)
                .OrderBy(type => type, StringComparer.Ordinal).ToArray();
            var categories = types.Where(type => !AttributeTypes.Contains(type)).ToArray();
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string category in categories) values.Add("category:" + category, 1 / Math.Sqrt(categories.Length));
            foreach (string attribute in types.Where(AttributeTypes.Contains))
                if (attribute != "CONTRABAND" && attribute != "ALL_TYPE" && attribute != "ALL_TYPE_EXCEPT_CONTRABAND")
                    values.Add("attribute:" + attribute, .25);
            values.Add(contraband ? "status:contraband" : "status:legal", .5);
            values.Add("contraband-level:" + (contraband ? contrabandLevel : 0).ToString(CultureInfo.InvariantCulture), .25);
            values.Add(stolen ? "status:stolen" : "status:owned_origin", .25);
            values.Add(hazardous ? "status:hazardous" : "status:ordinary", .25);
            return Normalize(values, categories);
        }

        internal static StorageAffinityVector Container(IEnumerable<StorageAffinitySample> contents)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sample in contents.OrderBy(sample => sample.Vector.Signature, StringComparer.Ordinal)
                .ThenBy(sample => sample.OccupiedCells))
            {
                foreach (string category in sample.Vector.Categories) categories.Add(category);
                foreach (var pair in sample.Vector.Values)
                {
                    values.TryGetValue(pair.Key, out double previous);
                    values[pair.Key] = previous + pair.Value * sample.OccupiedCells;
                }
            }
            return Normalize(values, categories);
        }

        internal static double Similarity(StorageAffinityVector item, StorageAffinityVector container)
        {
            if (item == null || container == null) throw new ArgumentNullException(item == null ? nameof(item) : nameof(container));
            if (!item.Categories.Intersect(container.Categories, StringComparer.Ordinal).Any()) return 0;
            double dot = 0;
            foreach (var pair in item.Values)
                if (container.Values.TryGetValue(pair.Key, out double weight)) dot += pair.Value * weight;
            return Math.Max(0, Math.Min(1, dot));
        }

        internal static IReadOnlyList<StorageAffinityMatch> Rank(StorageAffinityVector item,
            IEnumerable<StorageAffinityCandidate> containers)
        {
            if (containers == null) throw new ArgumentNullException(nameof(containers));
            var candidates = containers.ToList();
            if (candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
                throw new InvalidOperationException("Container identities must be unique.");
            return candidates.Select(candidate => new StorageAffinityMatch(candidate.Id, Similarity(item, candidate.Vector)))
                .Where(match => match.Similarity > 0).OrderByDescending(match => Math.Round(match.Similarity, 12))
                .ThenBy(match => match.Id, StringComparer.Ordinal).ToArray();
        }

        private static StorageAffinityVector Normalize(Dictionary<string, double> values, IEnumerable<string> categories)
        {
            double length = Math.Sqrt(values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Sum(pair => pair.Value * pair.Value));
            if (length > 0)
                foreach (string key in values.Keys.ToArray()) values[key] /= length;
            return new StorageAffinityVector(values, categories);
        }
    }
}
