using ProbablyStolen_FlatStorage;

internal static class StorageAffinityChecks
{
    internal static void Categories()
    {
        var food = Item("FOOD");
        var medical = Item("MEDICAL");
        Check(StorageAffinity.Similarity(food, Profile(food)) > .999999, "相同类别与属性具有最高相似度");
        Check(StorageAffinity.Similarity(food, Profile(medical)) == 0, "类别无交集时保留原处");
        var mixed = StorageAffinity.Item(new[] { "FOOD", "MEDICAL" }, false, 0, false, false);
        Check(StorageAffinity.Similarity(mixed, Profile(food)) > 0, "多类别物品使用类别交集");
        Check(StorageAffinity.Similarity(mixed, Profile(food)) < StorageAffinity.Similarity(food, Profile(food)), "类别完全一致优先");
        Check(StorageAffinity.Similarity(Item("LOWTECH"), Profile(Item("LOWTECH"))) == 0, "共同等级属性不能替代语义类别");
    }

    internal static void Status()
    {
        var legal = Item("SUBSTANCE");
        var contraband = StorageAffinity.Item(new[] { "SUBSTANCE", "CONTRABAND" }, true, 3, false, false);
        var low = StorageAffinity.Item(new[] { "SUBSTANCE", "CONTRABAND" }, true, 1, false, false);
        Check(StorageAffinity.Similarity(contraband, Profile(contraband)) > StorageAffinity.Similarity(contraband, Profile(legal)), "违禁物品偏向相同状态");
        Check(StorageAffinity.Similarity(contraband, Profile(contraband)) > StorageAffinity.Similarity(contraband, Profile(low)), "违禁等级参与匹配");
        var stolen = StorageAffinity.Item(new[] { "SUBSTANCE" }, false, 0, true, false);
        var waste = StorageAffinity.Item(new[] { "SUBSTANCE" }, false, 0, false, true);
        Check(StorageAffinity.Similarity(stolen, Profile(stolen)) > StorageAffinity.Similarity(stolen, Profile(legal)), "来源状态参与匹配");
        Check(StorageAffinity.Similarity(waste, Profile(waste)) > StorageAffinity.Similarity(waste, Profile(legal)), "危险废物参与匹配");
    }

    internal static void Weighting()
    {
        var food = Item("FOOD");
        var medical = Item("MEDICAL");
        var profile = StorageAffinity.Container(new[] { new StorageAffinitySample(food, 8), new StorageAffinitySample(medical, 1) });
        Check(StorageAffinity.Similarity(food, profile) > StorageAffinity.Similarity(medical, profile), "较大实际占格决定主要内容类别");
        var scaled = StorageAffinity.Container(new[] { new StorageAffinitySample(food, 16), new StorageAffinitySample(medical, 2) });
        Near(StorageAffinity.Similarity(food, profile), StorageAffinity.Similarity(food, scaled));
        Near(StorageAffinity.Similarity(medical, profile), StorageAffinity.Similarity(medical, scaled));
    }

    internal static void Ordering()
    {
        var food = Item("FOOD");
        var sameA = new StorageAffinityCandidate("item:10", Profile(food));
        var sameB = new StorageAffinityCandidate("item:20", Profile(food));
        var other = new StorageAffinityCandidate("item:00", Profile(Item("MEDICAL")));
        var ranked = StorageAffinity.Rank(food, new[] { sameB, other, sameA });
        Check(ranked.Select(match => match.Id).SequenceEqual(new[] { "item:10", "item:20" }), "相同得分按容器标识稳定排序并排除无匹配容器");
        Check(StorageAffinity.Rank(food, new[] { sameA, sameB, other }).Select(match => match.Id)
            .SequenceEqual(ranked.Select(match => match.Id)), "发现顺序不影响目标顺序");
        var mixed = StorageAffinity.Container(new[] { new StorageAffinitySample(food, 1), new StorageAffinitySample(Item("MEDICAL"), 1) });
        ranked = StorageAffinity.Rank(food, new[] { new StorageAffinityCandidate("a", mixed), sameB });
        Check(ranked[0].Id == "item:20" && ranked[1].Id == "a", "更高类别相似度优先并保留次优目标");
    }

    internal static void Determinism()
    {
        var a = StorageAffinity.Item(new[] { "FOOD", "MEDICAL", "FOOD", "LOWTECH" }, false, 0, false, false);
        var b = StorageAffinity.Item(new[] { "LOWTECH", "MEDICAL", "FOOD" }, false, 0, false, false);
        Check(a.Signature == b.Signature, "类别顺序和重复类别不改变向量");
        var samples = new[] { new StorageAffinitySample(a, 5), new StorageAffinitySample(Item("TOOL"), 3), new StorageAffinitySample(Item("FOOD"), 1) };
        Check(StorageAffinity.Container(samples).Signature == StorageAffinity.Container(samples.Reverse()).Signature, "内容发现顺序不改变容器向量");
        double norm = a.Values.Sum(pair => pair.Value * pair.Value);
        Near(norm, 1);
    }

    internal static void Boundaries()
    {
        Check(StorageAffinity.Similarity(Item("FOOD"), StorageAffinity.Container(Array.Empty<StorageAffinitySample>())) == 0, "空容器没有可推断类别");
        Check(StorageAffinity.Similarity(StorageAffinity.Item(Array.Empty<string>(), true, 4, true, true),
            StorageAffinity.Item(Array.Empty<string>(), true, 4, true, true)) == 0, "仅有特殊状态保持原处");
        Check(StorageAffinity.Rank(Item("FOOD"), Array.Empty<StorageAffinityCandidate>()).Count == 0, "无目标时得到空计划");
        Reject<ArgumentOutOfRangeException>(() => new StorageAffinitySample(Item("FOOD"), 0));
        Reject<ArgumentOutOfRangeException>(() => StorageAffinity.Item(new[] { "FOOD" }, true, 5, false, false));
        Reject<InvalidOperationException>(() => StorageAffinity.Rank(Item("FOOD"), new[]
        { new StorageAffinityCandidate("a", Item("FOOD")), new StorageAffinityCandidate("a", Item("MEDICAL")) }));
    }

    private static StorageAffinityVector Item(string category) => StorageAffinity.Item(new[] { category }, false, 0, false, false);
    private static StorageAffinityVector Profile(StorageAffinityVector item) => StorageAffinity.Container(new[] { new StorageAffinitySample(item, 1) });
    private static void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-12, "向量数值保持稳定");
    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("无效输入必须立即抛出异常。");
    }
}
