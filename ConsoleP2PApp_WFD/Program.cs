using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using Windows.Networking.Proximity;


namespace ConsoleP2PApp_WFD
{
    class Program
    {
        private static Task ReceiverTask;
        private static StreamSocket StreamSocket;
        private static PeerInformation RemotePeerInformation;
        private static readonly ManualResetEvent MResetEvent;
        private static readonly object Sync;
        

        static Program()
        {
            MResetEvent = new ManualResetEvent(false);
            Sync = new object();
        }

        #region  P/Invoke
        [DllImport("shell32.dll")]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);
        #endregion

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
        #endregion

        private static void Receiver(DataReader reader, CancellationToken token)
        {
            while (true)
            {
                reader.LoadAsync(sizeof(uint)).AsTask(token).Wait();
                byte[] buffer = new byte[reader.ReadInt32()];
                reader.LoadAsync((uint)buffer.Length).AsTask(token).Wait();
                reader.ReadBytes(buffer);

                SequenceReaderWriter.WriteLine("Remote Host>  " +
                    Encoding.ASCII.GetString(buffer));
            }
        }

        #region Sender
        private static void Send(Stream writer, string str)
        {
            if (str == String.Empty) { str = " "; }
            writer.Write(BitConverter.GetBytes(str.Length), 0, 4);
            writer.Write(Encoding.ASCII.GetBytes(str), 0, str.Length);
            writer.Flush();
            return;
        }

        private static void Sender(Stream writer)
        {
            while (true)
            {
                Console.Write("Local Host>  ");
                string str = SequenceReaderWriter.ReadLine() ?? "EXIT";
                if (str.Trim().ToUpper() == "EXIT") { return; }
                Send(writer, str);
            }
        }
        #endregion       

        #region Shutdown
        private static void Shutdown(Exception e)
        {
            if ((e == null) && (ReceiverTask.IsFaulted)) { e = ReceiverTask.Exception; }
            if (e is AggregateException) { e = ((AggregateException)e).Flatten().InnerException; }
            StreamSocket?.Dispose();

            Console.Write((e == null)
                ? "\n"
                : "Error: " + e.Message + "\n");
            return;
        }

        private static void Shutdown()
        {
            Shutdown(null);
            return;
        }
        #endregion

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

        static void Main(string[] args)
        {
            try
            {
                Configuration(args);
                Console.WriteLine();
                CancellationTokenSource tokenSource = new CancellationTokenSource();
                using (DataReader reader = new DataReader(StreamSocket.InputStream) { ByteOrder = ByteOrder.LittleEndian })
                using (Stream writer = StreamSocket.OutputStream.AsStreamForWrite())
                {
                    ReceiverTask = Task.Delay(1000).ContinueWith(
                        (antecedent) =>
                        { Receiver(reader, tokenSource.Token); },
                        TaskContinuationOptions.LongRunning).ContinueWith(
                        (antecedent) =>
                        {
                            SequenceReaderWriter.AbortReadLine = true;
                            throw antecedent.Exception;
                        },
                        tokenSource.Token);
                    try { Sender(writer); }
                    finally
                    {
                        if (!ReceiverTask.IsCompleted)
                        {
                            tokenSource.Cancel();
                            EllipsisAnimation(ReceiverTask.Wait, "");
                        }
                    }
                }
                Shutdown();
            }
            catch (Exception e) { Shutdown(e); }
            return;
        }
    }
}
