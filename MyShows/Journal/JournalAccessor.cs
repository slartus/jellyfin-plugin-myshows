using System.IO;

namespace MyShows.Journal
{
    internal static class JournalAccessor
    {
        private static JournalStore _instance;
        private static readonly object _lock = new();

        public static JournalStore Open()
        {
            if (_instance != null) return _instance;
            lock (_lock)
            {
                if (_instance != null) return _instance;
                var dir = Plugin.Instance?.DataFolderPath
                    ?? throw new System.InvalidOperationException("MyShows plugin instance is not initialised");
                var dbPath = Path.Combine(dir, "journal.db");
                _instance = new JournalStore(dbPath);
                return _instance;
            }
        }

        public static JournalStore TryOpen()
        {
            var dir = Plugin.Instance?.DataFolderPath;
            if (string.IsNullOrEmpty(dir)) return null;
            var dbPath = Path.Combine(dir, "journal.db");
            if (!File.Exists(dbPath)) return null;
            return Open();
        }
    }
}
