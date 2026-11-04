namespace ProbablyStolen_FlatStorage
{
    internal static class RenderingTransfer
    {
        internal static bool KeepWorldTransform(bool requested, bool itemRoot, bool fromCanvas, bool toTemporary)
            => requested && !(itemRoot && fromCanvas && toTemporary);
    }
}
