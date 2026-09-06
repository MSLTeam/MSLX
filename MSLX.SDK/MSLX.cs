namespace MSLX.SDK;

public static class MSLX
{
    public static Interfaces.IMSLXConfig Config { get; private set; } = null!;
    public static Interfaces.IMSLXLogger Logger { get; private set; } = null!;
    public static Interfaces.IDownloadService Downloader { get; private set; } = null!;
    public static Interfaces.IMSLXHttp Http { get; private set; } = null!;
    public static IServices.IBackgroundTaskManager Tasks { get; private set; } = null!;
    
    public static void Initialize(Interfaces.IMSLXConfig config, Interfaces.IMSLXLogger logger,Interfaces.IDownloadService downloader,Interfaces.IMSLXHttp http, IServices.IBackgroundTaskManager tasks)
    {
        Config ??= config;
        Logger ??= logger;
        Downloader ??= downloader;
        Http ??= http;
        Tasks ??= tasks;
    }
}