﻿using System.Net.Sockets;
using System.Net;

namespace Dacs7.Communication
{
    public class ClientSocketConfiguration : ISocketConfiguration
    {
        public string Hostname { get; set; } = "localhost";
        public int ServiceName { get; set; } = 22112;
        public int ReceiveBufferSize { get; set; } = 1024;  // buffer size to use for each socket I/O operation 
        public int AutoconnectTime { get; set; } = 5000; // <= 0 means disabled
        public string NetworkAdapter { get; set; }
        public bool KeepAlive { get; set; } = false;
        public int ReconnectInterval { get; set; } = 500;

        public ClientSocketConfiguration()
        {
        }

        public static ClientSocketConfiguration FromSocket(System.Net.Sockets.Socket socket)
        {
            var ep = socket.RemoteEndPoint as IPEndPoint;
            var keepAlive = socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.KeepAlive);
            return new ClientSocketConfiguration
            {
                Hostname = ep.Address.ToString(),
                ServiceName = ep.Port,
                ReceiveBufferSize = socket.ReceiveBufferSize,  // buffer size to use for each socket I/O operation 
                KeepAlive = keepAlive != null
            };
        }

        public override string ToString()
        {
            return $"Hostname={Hostname}; ServiceName={ServiceName}; ReceiveBufferSize={ReceiveBufferSize}; KeepAlive={KeepAlive}";
        }
    }
}