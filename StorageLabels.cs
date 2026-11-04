namespace ProbablyStolen_FlatStorage
{
    public static class StorageLabels
    {
        public static string Name(string label, string name) => string.IsNullOrWhiteSpace(label) ? name : label.Trim();

        public static string Title(string parent, string label, string name)
        {
            string own = Name(label, name);
            return string.IsNullOrWhiteSpace(parent) ? own : parent.Trim() + " / " + own;
        }
    }
}
