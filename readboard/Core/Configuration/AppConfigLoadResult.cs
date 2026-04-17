namespace readboard
{
    internal sealed class AppConfigLoadResult
    {
        public AppConfigLoadResult(AppConfig config, bool hasExistingConfig)
        {
            Config = config;
            HasExistingConfig = hasExistingConfig;
        }

        public AppConfig Config { get; private set; }
        public bool HasExistingConfig { get; private set; }
    }
}
