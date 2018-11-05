using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace P2PConsoleApp_WFD
{
    public static class StandardIOWrapper
    {
        private static int SavedCursorTop;
        private static int CurrentCursorTop;
        private static string ReadLineValue;
        private static readonly object Sync;
        private static readonly ManualResetEvent Start;
        private static readonly ManualResetEvent Stop;
        private enum StandardIOWrapperOptions { ReadLine, WriteLine }
        public static bool AbortReadLine { get; set; }


        static StandardIOWrapper()
        {
            AbortReadLine = false;
            SavedCursorTop = Console.CursorTop;
            CurrentCursorTop = SavedCursorTop;
            Sync = new object();
            Start = new ManualResetEvent(false);
            Stop = new ManualResetEvent(false);
            Console.SetIn(new StreamReader(Console.OpenStandardInput(322), Console.InputEncoding, false, 322));
            Task.Factory.StartNew(ValueListener, TaskCreationOptions.LongRunning);
        }
        public static void WriteLine(string str)
        {
            ConsoleSync(StandardIOWrapperOptions.WriteLine, str);
            return;
        }

        #region ReadLine
        public static string ReadLine()
        {
            string str = GetValue();
            ConsoleSync(StandardIOWrapperOptions.ReadLine);
            return str;
        }
        private static string GetValue()
        {
            Start.Set();
            while (true)
            {
                if (AbortReadLine)
                {
                    return null;
                }
                else if (Stop.WaitOne(60000))
                {
                    Stop.Reset();
                    return ReadLineValue;
                }
            }
        }
        private static void ValueListener()
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
        #endregion

        private static void ConsoleSync(StandardIOWrapperOptions option, string str = null)
        {
            lock (Sync)
            {
                switch (option)
                {
                    case StandardIOWrapperOptions.WriteLine:
                        {
                            int savedCursorLeft = Console.CursorLeft;
                            SavedCursorTop = Console.CursorTop;
                            CurrentCursorTop += 6;
                            Console.SetCursorPosition(0, CurrentCursorTop);
                            Console.Write(str);
                            Console.SetCursorPosition(savedCursorLeft, SavedCursorTop);
                            break;
                        }
                    case StandardIOWrapperOptions.ReadLine:
                        {
                            CurrentCursorTop += 6;
                            SavedCursorTop = CurrentCursorTop;
                            Console.SetCursorPosition(0, SavedCursorTop);
                            break;
                        }
                }
            }
            return;
        }
    }
}
