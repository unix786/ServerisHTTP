using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

/*
Windows sockets error codes:
https://msdn.microsoft.com/en-us/library/windows/desktop/ms740668(v=vs.85).aspx
WSAEINVAL = 10022, // Invalid argument.
*/

namespace ServerisHTTP
{
    /// <summary>
    /// Accepts connections and responds via <see cref="HTTPService"/>.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <seealso cref="TcpListener"/>
    internal static class MyListener
    {
        public static void Run()
        {
            EndPoint endpoint = new IPEndPoint(Settings.ListenerIP, Settings.ListenerPort);
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(endpoint);
                socket.Listen(Settings.ConnectionQueueLimit);

                var socketIP = socket.LocalEndPoint as IPEndPoint;
                Console.WriteLine(nameof(MyListener) + " started on " + (socketIP?.ToString() ?? "<non IP endpoint?>"));

                // Could use an array here instead of List.
                var connections = new List<Tuple<AcceptState, IAsyncResult>>(Settings.ConnectionQueueLimit);
                while (!Program.IsClosing)
                {
                    var state = new AcceptState(socket);
                    var res = socket.BeginAccept(AcceptCallback, state);
                    state.WaitForAccept();

                    connections.Add(Tuple.Create(state, res));

                    while (Settings.ConnectionLimit <= connections.Count)
                    {
                        if (Program.IsClosing) break;

                        for (int i = connections.Count - 1; i >= 0; i--)
                        {
                            if (connections[i].Item2.IsCompleted)
                                connections.RemoveAt(i);
                        }
                    }
                }

                socket.Close();
            }
        }

        private class AcceptState
        {
            private readonly Socket socketListener;

            /// <summary></summary>
            /// <param name="socketListener">Listening socket.</param>
            public AcceptState(Socket socketListener)
            {
                this.socketListener = socketListener;
            }

            /// <summary></summary>
            /// <remarks>
            /// ManualResetEvent and ManualResetEventSlim:
            /// <para/>https://msdn.microsoft.com/en-us/library/5hbefs30(v=vs.110).aspx
            /// </remarks>
            private readonly ManualResetEventSlim mutex = new ManualResetEventSlim();

            /// <summary>
            /// <see cref="Socket.EndAccept(IAsyncResult)"/>
            /// </summary>
            /// <param name="e"></param>
            /// <returns>Connection socket.</returns>
            internal Socket Accept(IAsyncResult e)
            {
                if (mutex.IsSet)
                {
                    // When debugging, the callback was called twice somehow. The second call was
                    // directly from BeginAccept (as if in both synchronous and asynchronous mode).
                    // I failed to reproduce this afterwards.
                    //throw new InvalidOperationException("Double accept?");
                    return null;
                }
                try
                {
                    return socketListener.EndAccept(e);
                }
                finally
                {
                    // In case of exception, maybe should make main thread throw as well?
                    mutex.Set();
                }
            }

            public void WaitForAccept()
            {
                mutex.Wait();
                mutex.Reset();
            }
        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            // Need to improve this method. Some browsers don't work if connection
            // is terminated after each response when they request keep-alive or something...
            try
            {
                var state = (AcceptState)ar.AsyncState;
                using (var socket = state.Accept(ar))
                {
                    if (socket == null) return;
                    if (socket.Available > 0)
                    {
                        socket.SendTimeout = socket.ReceiveTimeout = Settings.TransmissionTimeout;
                        var buffer = new byte[socket.Available];
                        // The request must fit inside a single packet.
                        int bytesRead = socket.Receive(buffer);
                        if (bytesRead > 0)
                        {
                            Array.Resize(ref buffer, bytesRead);
                            HTTPService.Respond(socket, buffer);
                        }
                    }
                    //socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message); // This may flood the console, which may be unsafe (haven't checked).
                var socketEx = ex as SocketException;
                if (socketEx == null) throw;
            }
        }
    }
}
