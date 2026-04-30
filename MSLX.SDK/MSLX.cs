namespace MSLX.SDK;

public static class MSLX
{
    public static Interfaces.IMSLXConfig Config { get; private set; } = null!;
    public static Interfaces.IMSLXLogger Logger { get; private set; } = null!;
    public static void Initialize(Interfaces.IMSLXConfig config, Interfaces.IMSLXLogger logger)
    {
        Config ??= config;
        Logger ??= logger;
    }
}