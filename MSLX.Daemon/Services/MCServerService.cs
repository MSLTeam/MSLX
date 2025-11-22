using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Services
{
    public class MCServerService
    {

        public async Task<bool> StartServer(uint instanceId)
        {
            await Task.Delay(1000); // Ä£ÄâÒì²½²Ù×÷
            ConfigServices.ServerListConfig config = ConfigServices.ServerList;
            if (config.GetServer(instanceId)!=null)
            {
                return true;
            }
            return false;
        }

    }
}