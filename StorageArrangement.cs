using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class ArrangementGroup
    {
        public string Id, ParentId, Type, Label;
        public bool Machine;
        public Box Size;
        public Dictionary<string, double> Contents = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    internal static class StorageArrangement
    {
        internal const float Gap = 16;

        internal static double Similarity(ArrangementGroup a, ArrangementGroup b)
        {
            double dot = 0, aa = 0, bb = 0;
            foreach (var pair in a.Contents)
            {
                aa += pair.Value * pair.Value;
                if (b.Contents.TryGetValue(pair.Key, out double value)) dot += pair.Value * value;
            }
            foreach (var value in b.Contents.Values) bb += value * value;
            double content = aa > 0 && bb > 0 ? dot / Math.Sqrt(aa * bb) : 0;
            bool sameType = !string.IsNullOrEmpty(a.Type) && a.Type == b.Type;
            double label = LabelSimilarity(a.Label, b.Label);
            if (a.Machine || b.Machine)
                return (a.Machine && b.Machine && sameType ? .75 : 0) + .2 * content + .05 * label;
            if (aa == 0 && bb == 0) return (sameType ? .8 : 0) + .2 * label;
            return .85 * content + (sameType ? .08 : 0) + .07 * label;
        }

        private static double LabelSimilarity(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
            a = a.Trim().ToUpperInvariant(); b = b.Trim().ToUpperInvariant();
            if (a == b) return 1;
            var left = new HashSet<string>(); var right = new HashSet<string>();
            for (int i = 0; i + 1 < a.Length; i++) left.Add(a.Substring(i, 2));
            for (int i = 0; i + 1 < b.Length; i++) right.Add(b.Substring(i, 2));
            int common = left.Count(right.Contains);
            return left.Count + right.Count > common ? (double)common / (left.Count + right.Count - common) : 0;
        }

        internal static List<Box> Arrange(IReadOnlyList<ArrangementGroup> input, float aspect)
        {
            if (!CanvasMath.Finite(aspect) || aspect <= 0) throw new ArgumentOutOfRangeException(nameof(aspect));
            if (input.Any(g => g == null || string.IsNullOrEmpty(g.Id) || !CanvasMath.Finite(g.Size.Width) ||
                !CanvasMath.Finite(g.Size.Height) || g.Size.Width <= 0 || g.Size.Height <= 0) ||
                input.Select(g => g.Id).Distinct().Count() != input.Count)
                throw new ArgumentException("排列分组标识或尺寸无效。");
            if (input.Count == 0) return new List<Box>();
            var groups = input.OrderBy(g => g.Id, StringComparer.Ordinal).ToArray();
            var indices = groups.Select((g, i) => (g.Id, i)).ToDictionary(p => p.Id, p => p.i);
            var parents = groups.Select(g => g.ParentId != null && indices.TryGetValue(g.ParentId, out int p) ? p : -1).ToArray();
            var depths = new int[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                var seen = new HashSet<int> { i };
                for (int p = parents[i]; p >= 0; p = parents[p])
                {
                    if (!seen.Add(p)) throw new ArgumentException("容器所属关系存在循环。");
                    depths[i]++;
                }
            }
            var similarity = new double[groups.Length, groups.Length];
            for (int i = 0; i < groups.Length; i++)
                for (int j = 0; j < i; j++) similarity[i, j] = similarity[j, i] = Similarity(groups[i], groups[j]);
            Box[] best = null; double bestScore = double.MaxValue;
            for (int variant = 0; variant < 3; variant++)
            {
                var boxes = new Box[groups.Length]; var placed = new HashSet<int>();
                var center = new HashSet<int>();
                float x = 0, tableHeight = 0;
                foreach (string id in new[] { "root:back", "root:front" })
                    if (indices.TryGetValue(id, out int index))
                    {
                        var size = groups[index].Size;
                        boxes[index] = new Box(x, 0, size.Width, size.Height);
                        x += size.Width + Gap; tableHeight = Math.Max(tableHeight, size.Height);
                        center.Add(index); placed.Add(index);
                    }
                if (indices.TryGetValue("root:main", out int cabinet))
                {
                    var size = groups[cabinet].Size;
                    boxes[cabinet] = new Box((Math.Max(0, x - Gap) - size.Width) / 2,
                        tableHeight > 0 ? tableHeight + Gap : 0, size.Width, size.Height);
                    center.Add(cabinet); placed.Add(cabinet);
                }
                if (placed.Count == 0)
                {
                    int index = Array.FindIndex(parents, p => p < 0);
                    boxes[index] = new Box(0, 0, groups[index].Size.Width, groups[index].Size.Height);
                    center.Add(index); placed.Add(index);
                }
                var core = Bounds(placed.Select(i => boxes[i]));
                float ox = core.X + core.Width / 2, oy = core.Y + core.Height / 2;
                foreach (int i in center.ToArray())
                    boxes[i] = new Box(boxes[i].X - ox, boxes[i].Y - oy, boxes[i].Width, boxes[i].Height);
                core = new Box(core.X - ox, core.Y - oy, core.Width, core.Height);
                double totalArea = groups.Sum(g => (double)(g.Size.Width + Gap) * (g.Size.Height + Gap));
                while (placed.Count < groups.Length)
                {
                    var ready = Enumerable.Range(0, groups.Length).Where(i => !placed.Contains(i) && (parents[i] < 0 || placed.Contains(parents[i])));
                    int next = ready.OrderBy(i => depths[i]).ThenByDescending(i =>
                        variant == 0 ? groups[i].Size.Width * groups[i].Size.Height :
                        variant == 1 ? placed.Max(j => similarity[i, j]) * 100000 + groups[i].Size.Width * groups[i].Size.Height :
                        Math.Max(groups[i].Size.Width, groups[i].Size.Height)).ThenBy(i => groups[i].Id, StringComparer.Ordinal).First();
                    boxes[next] = Place(next, groups, boxes, placed, parents, similarity, core, aspect, totalArea);
                    placed.Add(next);
                }
                // 按实际包围面积与相关分组距离进行局部位置优化。
                if (groups.Length <= 96)
                    for (int i = 0; i < groups.Length; i++)
                    {
                        if (center.Contains(i) || parents.Contains(i)) continue;
                        var old = boxes[i]; placed.Remove(i);
                        var candidate = Place(i, groups, boxes, placed, parents, similarity, core, aspect, totalArea);
                        double oldScore = Score(i, old, boxes, placed, parents, similarity, aspect, totalArea);
                        if (Score(i, candidate, boxes, placed, parents, similarity, aspect, totalArea) < oldScore) boxes[i] = candidate;
                        placed.Add(i);
                    }
                double score = Quality(boxes, parents, similarity, aspect, totalArea);
                if (score < bestScore) { bestScore = score; best = boxes; }
            }
            return input.Select(g => best[indices[g.Id]]).ToList();
        }

        private static Box Place(int index, ArrangementGroup[] groups, Box[] boxes, HashSet<int> placed,
            int[] parents, double[,] similarity, Box core, float aspect, double area)
        {
            var size = groups[index].Size;
            var boundary = Bounds(placed.Select(i => boxes[i]));
            var candidates = new List<Box>();
            void Around(Box b)
            {
                foreach (float y in new[] { b.Y, b.Y + b.Height - size.Height, b.Y + (b.Height - size.Height) / 2 })
                {
                    candidates.Add(new Box(b.X - Gap - size.Width, y, size.Width, size.Height));
                    candidates.Add(new Box(b.X + b.Width + Gap, y, size.Width, size.Height));
                }
                foreach (float x in new[] { b.X, b.X + b.Width - size.Width, b.X + (b.Width - size.Width) / 2 })
                {
                    candidates.Add(new Box(x, b.Y - Gap - size.Height, size.Width, size.Height));
                    candidates.Add(new Box(x, b.Y + b.Height + Gap, size.Width, size.Height));
                }
            }
            Around(boundary);
            foreach (int i in placed.OrderByDescending(i => i == parents[index] ? 2 : similarity[index, i])
                .ThenByDescending(i => i).Take(64)) Around(boxes[i]);
            Box best = default; double bestScore = double.MaxValue;
            foreach (var candidate in candidates)
            {
                if (Overlaps(candidate, core) || placed.Any(i => Overlaps(candidate, boxes[i]))) continue;
                if (parents[index] >= 0 && Radius(candidate) + .01 < Radius(boxes[parents[index]])) continue;
                double score = Score(index, candidate, boxes, placed, parents, similarity, aspect, area);
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            if (bestScore == double.MaxValue) throw new InvalidOperationException("无法为容器找到有效排列位置。");
            return best;
        }

        private static bool Overlaps(Box a, Box b) =>
            a.X < b.X + b.Width + Gap - .01f && a.X + a.Width + Gap > b.X + .01f &&
            a.Y < b.Y + b.Height + Gap - .01f && a.Y + a.Height + Gap > b.Y + .01f;

        private static double Radius(Box b) => Math.Sqrt(Math.Pow(b.X + b.Width / 2, 2) + Math.Pow(b.Y + b.Height / 2, 2));
        private static double Distance(Box a, Box b) => Math.Sqrt(
            Math.Pow(Math.Max(0, Math.Max(a.X - b.X - b.Width, b.X - a.X - a.Width)), 2) +
            Math.Pow(Math.Max(0, Math.Max(a.Y - b.Y - b.Height, b.Y - a.Y - a.Height)), 2));
        private static Box Bounds(IEnumerable<Box> boxes)
        {
            var list = boxes.ToList(); float x = list.Min(b => b.X), y = list.Min(b => b.Y);
            return new Box(x, y, list.Max(b => b.X + b.Width) - x, list.Max(b => b.Y + b.Height) - y);
        }
        private static double Compactness(Box b, float aspect, double area)
        {
            double fittedHeight = Math.Max(b.Height, b.Width / aspect);
            return (.35 * b.Width * b.Height + .65 * fittedHeight * fittedHeight * aspect) / area;
        }
        private static double Score(int index, Box candidate, Box[] boxes, HashSet<int> placed, int[] parents,
            double[,] similarity, float aspect, double area)
        {
            double scale = Math.Sqrt(area);
            double near = placed.Where(i => similarity[index, i] > .35)
                .Select(i => similarity[index, i] * Math.Exp(-Distance(candidate, boxes[i]) / (scale * .2)))
                .DefaultIfEmpty(0).Max();
            double parent = parents[index] < 0 ? 0 : Distance(candidate, boxes[parents[index]]) / scale;
            return Compactness(Bounds(placed.Select(i => boxes[i]).Append(candidate)), aspect, area) +
                .25 * parent + .035 * Radius(candidate) / scale - .24 * near;
        }
        private static double Quality(Box[] boxes, int[] parents, double[,] similarity, float aspect, double area)
        {
            double relation = 0, scale = Math.Sqrt(area);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (parents[i] >= 0) relation += .25 * Distance(boxes[i], boxes[parents[i]]) / scale;
                double near = 0;
                for (int j = 0; j < boxes.Length; j++)
                    if (i != j && similarity[i, j] > .35)
                        near = Math.Max(near, similarity[i, j] * Math.Exp(-Distance(boxes[i], boxes[j]) / (scale * .2)));
                relation -= .24 * near;
            }
            return Compactness(Bounds(boxes), aspect, area) + relation / boxes.Length;
        }
    }
}
