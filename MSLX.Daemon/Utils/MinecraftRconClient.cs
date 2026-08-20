using System.Net.Sockets;
using System.Text;

namespace MSLX.Daemon.Utils;

public class MinecraftRconClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;

    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_EXECCOMMAND = 2;

    public MinecraftRconClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.SendTimeout = 3000;
            _tcpClient.ReceiveTimeout = 3000;
            
            await _tcpClient.ConnectAsync(_host, _port);
            _networkStream = _tcpClient.GetStream();

            return await SendPacketAsync(SERVERDATA_AUTH, _password);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> SendCommandAsync(string command)
    {
        if (_networkStream == null || _tcpClient == null || !_tcpClient.Connected)
            return string.Empty;

        await SendPacketAsync(SERVERDATA_EXECCOMMAND, command);
        return await ReceivePacketAsync();
    }

    private async Task<bool> SendPacketAsync(int type, string payload)
    {
        if (_networkStream == null) return false;

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        int packetLength = 4 + 4 + payloadBytes.Length + 2; 

        byte[] packet = new byte[4 + packetLength];
        
        BitConverter.GetBytes(packetLength).CopyTo(packet, 0);
        BitConverter.GetBytes(1).CopyTo(packet, 4);
        BitConverter.GetBytes(type).CopyTo(packet, 8);
        payloadBytes.CopyTo(packet, 12);

        await _networkStream.WriteAsync(packet, 0, packet.Length);

        if (type == SERVERDATA_AUTH)
        {
            // 首包
            var packet1 = await ReadRawPacketAsync();
            if (packet1 == null || packet1.Length < 12) return false;

            int p1Type = BitConverter.ToInt32(packet1, 8);
            int p1ReqId = BitConverter.ToInt32(packet1, 4);

            if (p1Type == 2) // SERVERDATA_AUTH_RESPONSE
            {
                return p1ReqId != -1;
            }
            
            var packet2 = await ReadRawPacketAsync();
            if (packet2 == null || packet2.Length < 12) return false;

            int p2Type = BitConverter.ToInt32(packet2, 8);
            int p2ReqId = BitConverter.ToInt32(packet2, 4);

            return p2ReqId != -1;
        }

        return true;
    }

    private async Task<string> ReceivePacketAsync()
    {
        var rawPacket = await ReadRawPacketAsync();
        if (rawPacket == null || rawPacket.Length < 14) return string.Empty;

        return Encoding.UTF8.GetString(rawPacket, 12, rawPacket.Length - 14).Trim();
    }

    private async Task<byte[]?> ReadRawPacketAsync()
    {
        if (_networkStream == null) return null;

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(3000);
            byte[] lengthBuffer = new byte[4];
            int read = await _networkStream.ReadAsync(lengthBuffer, 0, 4, cts.Token);
            if (read < 4) return null;

            int length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 4096) return null;

            byte[] buffer = new byte[length];
            int bytesRead = 0;
            while (bytesRead < length)
            {
                int r = await _networkStream.ReadAsync(buffer, bytesRead, length - bytesRead, cts.Token);
                if (r == 0) break;
                bytesRead += r;
            }

            byte[] fullPacket = new byte[4 + length];
            lengthBuffer.CopyTo(fullPacket, 0);
            buffer.CopyTo(fullPacket, 4);
            
            return fullPacket;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _networkStream?.Dispose();
        _tcpClient?.Dispose();
    }
}
