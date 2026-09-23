namespace GeniaProxy.Models
{
    public sealed class AppSettings
    {
        public int LocalPort { get; set; } = 2080;

        public string LastProfile { get; set; } =
            string.Empty;

        public bool UseSystemProxy { get; set; }

        public ConnectionMode ConnectionMode { get; set; } =
            GeniaProxy.Models.ConnectionMode.LocalProxy;

        public CorePreference CorePreference { get; set; } =
            GeniaProxy.Models.CorePreference.Automatic;

        public TunStackPreference TunStackPreference { get; set; } =
            GeniaProxy.Models.TunStackPreference.Mixed;

        public Dictionary<string, ProfileRuntimeSettings> ProfileSettings
            { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool AutoConnect { get; set; }

        public bool StartWithWindows { get; set; }

        public bool ReconnectOnFailure { get; set; } = true;

        public int ReconnectDelaySeconds { get; set; } = 3;

        public bool LogCollapsed { get; set; } = true;

        public int ExpandedWindowHeight { get; set; } = 460;
    }

    public sealed class ProfileRuntimeSettings
    {
        public int LocalPort { get; set; } = 2080;

        public ConnectionMode ConnectionMode { get; set; } =
            ConnectionMode.LocalProxy;

        public CorePreference CorePreference { get; set; } =
            GeniaProxy.Models.CorePreference.Automatic;

        public TunStackPreference TunStackPreference { get; set; } =
            GeniaProxy.Models.TunStackPreference.Mixed;

        public DateTimeOffset? LastTestUtc { get; set; }

        public bool? LastTestSucceeded { get; set; }

        public double? LastTestAverageMilliseconds { get; set; }

        public string LastExitIp { get; set; } = string.Empty;
    }
}
