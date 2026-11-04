namespace ProbablyStolen_FlatStorage
{
    public static class StorageVisibility
    {
        public static bool CanExpose(bool hasContent, bool owned, bool locked, bool requiresOpening,
            bool tracksOpening, bool opened, bool remembered)
        {
            if (!hasContent || !owned || locked) return false;
            if (tracksOpening) return opened;
            return !requiresOpening || opened || remembered;
        }
    }
}
