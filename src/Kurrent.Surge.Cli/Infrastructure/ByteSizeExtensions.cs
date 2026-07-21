namespace Kurrent.Surge.Cli.Infrastructure;

static class ByteSizeExtensions {
    extension(long bytes) {
        /// <summary>Renders this byte count as a human-friendly size, e.g. <c>3.14 MB</c>.</summary>
        public string Humanize() {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double   size  = bytes;
            var      unit  = 0;

            while (size >= 1024 && unit < units.Length - 1) {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }
    }
}
