namespace readboard
{
    internal interface IAppConfigStore
    {
        AppConfigLoadResult Load();
        void Save(AppConfig config);
    }
}
