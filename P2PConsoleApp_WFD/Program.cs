using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using Windows.Networking.Proximity;


namespace P2PConsoleApp_WFD 
{
    class Program
    {
        private static Task ReceiverTask;
        private static StreamSocket StreamSocket;
        private static PeerInformation RemotePeerInformation;
        private static CancellationTokenSource TokenSource;
        private static readonly ManualResetEvent MResetEvent;
        private static readonly object Sync;


        #region  DLL Import
        [DllImport("shell32.dll")]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);
        #endregion

        static Program()
        {
            MResetEvent = new ManualResetEvent(false);
            Sync = new object();
        }      

        #region Configuration
        private static void Configuration(string[] args)
        {
            SetCurrentProcessExplicitAppUserModelID("App_WFD");
            switch ((args.Length == 3)
                    ? args[0].Trim().ToUpper()
                    : "default")
            {
                case "CONNECT": { StreamSocket = RunAsClient(args[1], args[2]); break; }
                case "LISTEN": { StreamSocket = RunAsServer(args[1], args[2]); break; }
                default: { throw new Exception("Failed to start correctly"); }
            }
            return;
        }
        private static StreamSocket RunAsClient(string localPeerDisplayName, string remotePeerDisplayName)
        {            
            PeerFinder.DisplayName = localPeerDisplayName;
            PeerFinder.AllowInfrastructure = false;
            PeerFinder.AllowBluetooth = false;
            PeerFinder.AllowWiFiDirect = true;
            PeerFinder.Start();
            PeerWatcher watcher = PeerFinder.CreateWatcher();
            watcher.Added += (sender, peerInfo) =>
            {
                if (peerInfo.DisplayName == remotePeerDisplayName)
                { SetRemotePeerInformation(peerInfo); }
            };
            watcher.Start();

            Task<StreamSocket> task = Task.Run(() =>
            {
                return (MResetEvent.WaitOne(30000))
                ? PeerFinder.ConnectAsync(RemotePeerInformation).AsTask().Result
                : throw new Exception("Connect Timeout");
            });
            EllipsisAnimation(task.Wait, "Connecting");

            PeerFinder.Stop();
            return task.Result;
        }
        private static StreamSocket RunAsServer(string localPeerDisplayName, string remotePeerDisplayName)
        {
            PeerFinder.DisplayName = localPeerDisplayName;
            PeerFinder.AllowInfrastructure = false;
            PeerFinder.AllowBluetooth = false;
            PeerFinder.AllowWiFiDirect = true;
            PeerFinder.ConnectionRequested += (sender, e) =>
            {
                if (e.PeerInformation.DisplayName == remotePeerDisplayName)
                { SetRemotePeerInformation(e.PeerInformation); }
            };
            PeerFinder.Start();

            Task<StreamSocket> task = Task.Run(() =>
            {
                return (MResetEvent.WaitOne(30000))
                ? PeerFinder.ConnectAsync(RemotePeerInformation).AsTask().Result
                : throw new Exception("Listen Timeout");
            });
            EllipsisAnimation(task.Wait, "Listening");

            PeerFinder.Stop();
            return task.Result;
        }         
        private static void SetRemotePeerInformation(PeerInformation peerInfo)
        {
            lock (Sync)
            {
                if (RemotePeerInformation == null)
                {
                    RemotePeerInformation = peerInfo;
                    MResetEvent.Set();
                }
            }
            return;
        }
        private static void EllipsisAnimation(Func<int, bool> wait, string str)
        {
            bool runLoop = true;
            Console.CursorVisible = false;
            do
            {
                byte i = 0;
                Console.Write(str);
                do
                {
                    i++;
                    Console.Write('.');
                    try
                    {
                        if (wait(1000))
                        { runLoop = false; break; }
                    }
                    catch { runLoop = false; break; }
                } while (i < 3);
                Console.CursorLeft -= (str.Length + i);
                Console.Write(new string(' ', (str.Length + i)));
                Console.CursorLeft -= (str.Length + i);
            } while (runLoop);
            Console.CursorVisible = true;
            return;
        }
        #endregion

        #region Receiver
        private static void Receiver(DataReader reader, CancellationToken token)
        {
            while (true)
            {
                reader.LoadAsync(sizeof(uint)).AsTask(token).Wait();
                byte[] buffer = new byte[reader.ReadInt32()];
                reader.LoadAsync((uint)buffer.Length).AsTask(token).Wait();
                reader.ReadBytes(buffer);

                StandardIOWrapper.WriteLine("Remote Host>  " +
                    Encoding.ASCII.GetString(buffer));
            }
        }
        #endregion

        #region Sender
        private static void Sender(Stream writer)
        {
            while (true)
            {
                Console.Write("Local Host>  ");
                string str = StandardIOWrapper.ReadLine() ?? "EXIT";
                if (str.Trim().ToUpper() == "EXIT") { return; }
                Send(writer, str);
            }
        }
        private static void Send(Stream writer, string str)
        {
            if (str != string.Empty)
            {
                writer.Write(BitConverter.GetBytes(str.Length), 0, 4);
                writer.Write(Encoding.ASCII.GetBytes(str), 0, str.Length);
                writer.Flush();
            }
            return;
        }
        #endregion

        #region Shutdown
        private static void Shutdown(Exception e)
        {
            if (ReceiverTask != null)
            {
                if ((e == null) && ReceiverTask.IsFaulted)
                {
                    e = ReceiverTask.Exception;
                }
                else if (!ReceiverTask.IsCompleted)
                {
                    ShutdownReceiver();
                }
            }
            StreamSocket?.Dispose();

            if (e != null)
            {
                Console.WriteLine("Error: " + 
                    ((e is AggregateException)
                    ? ((AggregateException)e).Flatten().InnerException.Message
                    : e.Message));
            }
            return;
        }
        private static void Shutdown()
        {
            Shutdown(null);
            return;
        }
        private static void ShutdownReceiver()
        {
            TokenSource.Cancel();
            try { ReceiverTask.Wait(); }
            catch { }
            return;
        }
        #endregion

        static void Main(string[] args)
        {
            try
            {
                Configuration(args);
                Console.WriteLine();
                TokenSource = new CancellationTokenSource();
                using (DataReader reader = new DataReader(StreamSocket.InputStream)
                { ByteOrder = ByteOrder.LittleEndian })
                using (Stream writer = StreamSocket.OutputStream.AsStreamForWrite())
                {
                    ReceiverTask = Task.Delay(1000).ContinueWith(
                        (antecedent) =>
                        { Receiver(reader, TokenSource.Token); },
                        TaskContinuationOptions.LongRunning).ContinueWith(
                        (antecedent) =>
                        {
                            StandardIOWrapper.AbortReadLine = true;
                            throw antecedent.Exception;
                        },
                        TokenSource.Token);
                    Sender(writer);
                }
                Shutdown();
            }
            catch (Exception e) { Shutdown(e); }
        }
    }
}
