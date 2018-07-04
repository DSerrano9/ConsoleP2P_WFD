using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace ConsoleP2PApp_WFD
{
    public static class SequenceReaderWriter
    {
        private static int SavedCursorTop;
        private static int CurrentCursorTop;
        private static string ReadLineValue;
        private static readonly ManualResetEvent Start;
        private static readonly ManualResetEvent Stop;
        private static readonly object Sync;
        private enum SequenceReaderWriterOptions { ReadLine, WriteLine }
        public static bool AbortReadLine { get; set; }


        static SequenceReaderWriter()
        {
            AbortReadLine = false;
            SavedCursorTop = Console.CursorTop;
            CurrentCursorTop = SavedCursorTop;
            Sync = new object();
            Start = new ManualResetEvent(false);
            Stop = new ManualResetEvent(false);
            Console.SetIn(new StreamReader(Console.OpenStandardInput(322), Console.InputEncoding, false, 322));
            Task.Factory.StartNew(ReadLineListener, TaskCreationOptions.LongRunning);
        }

        public static void WriteLine(string str)
        {
            Sequencer(SequenceReaderWriterOptions.WriteLine, str);
            return;
        }

        #region ReadLine
        private static void ReadLineListener()
        {
            try
            {
                while (true)
                {
                    Start.WaitOne();
                    Start.Reset();
                    ReadLineValue = Console.ReadLine();
                    Stop.Set();
                }
            }
            catch { }
        }

        private static string GetReadLineValue()
        {
            Start.Set();
            while (true)
            {
                if (AbortReadLine) { return null; }
                else if (Stop.WaitOne(60000))
                {
                    Stop.Reset();
                    return ReadLineValue;
                }
            }
        }

        public static string ReadLine()
        {
            string str = GetReadLineValue();
            Sequencer(SequenceReaderWriterOptions.ReadLine);
            return str;
        }
        #endregion

        private static void Sequencer(SequenceReaderWriterOptions option, string str = null)
        {
            lock (Sync)
            {
                switch (option)
                {
                    case SequenceReaderWriterOptions.WriteLine:
                        {
                            int savedCursorLeft = Console.CursorLeft;
                            SavedCursorTop = Console.CursorTop;
                            Console.SetCursorPosition(0, (CurrentCursorTop += 6));
                            Console.Write(str);
                            Console.SetCursorPosition(savedCursorLeft, SavedCursorTop);
                            break;
                        }
                    case SequenceReaderWriterOptions.ReadLine:
                        {
                            SavedCursorTop = (CurrentCursorTop += 6);
                            Console.SetCursorPosition(0, SavedCursorTop);
                            break;
                        }
                }
            }
            return;
        }
    }
}
