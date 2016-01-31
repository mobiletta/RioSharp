﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RioSharp
{
    public abstract class RioTcpSocketPool : RioSocketPool
    {
        internal IntPtr SocketIocp;
        internal RioTcpSocket[] allSockets;

        public unsafe RioTcpSocketPool(RioFixedBufferPool sendPool, RioFixedBufferPool revicePool, uint socketCount,
            uint maxOutstandingReceive = 1024, uint maxOutstandingSend = 1024, uint maxConnections = 1024)
            : base(sendPool, revicePool, maxOutstandingReceive, maxOutstandingSend, maxConnections)
        {
            var adrSize = (sizeof(sockaddr_in) + 16) * 2;
            var overlapped = Marshal.AllocHGlobal(new IntPtr(socketCount * Marshal.SizeOf<RioNativeOverlapped>()));
            var adressBuffer = Marshal.AllocHGlobal(new IntPtr(socketCount * adrSize));

            allSockets = new RioTcpSocket[socketCount];

            for (int i = 0; i < socketCount; i++)
            {
                allSockets[i] = new RioTcpSocket(overlapped + (i * Marshal.SizeOf<RioNativeOverlapped>()), adressBuffer + (i * adrSize), this, SendBufferPool, ReceiveBufferPool, maxOutstandingReceive, maxOutstandingSend, SendCompletionQueue, ReceiveCompletionQueue);
                allSockets[i]._overlapped->SocketIndex = i;
            }


            if ((SocketIocp = Imports.CreateIoCompletionPort((IntPtr)(-1), IntPtr.Zero, 0, 1)) == IntPtr.Zero)
                Imports.ThrowLastError();

            foreach (var s in allSockets)
            {
                if ((Imports.CreateIoCompletionPort(s.Socket, SocketIocp, 0, 1)) == IntPtr.Zero)
                    Imports.ThrowLastError();
            }

            Thread SocketIocpThread = new Thread(SocketIocpComplete);
            SocketIocpThread.IsBackground = true;
            SocketIocpThread.Start();
        }

        protected abstract unsafe void SocketIocpComplete(object o);

        internal unsafe virtual void Recycle(RioTcpSocket socket)
        {
            RioSocketBase c;
            activeSockets.TryRemove(socket.GetHashCode(), out c);
            socket.ResetOverlapped();
            socket._overlapped->Status = 1;
            if (!RioStatic.DisconnectEx(c.Socket, socket._overlapped, 0x02, 0)) //TF_REUSE_SOCKET
                if (Imports.WSAGetLastError() != 997) // error_io_pending
                    Imports.ThrowLastWSAError();
            //else
            //    AcceptEx(socket);
        }
    }
}