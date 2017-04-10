using System;

namespace ServerisHTTP
{
    class Program
    {
        internal static bool IsClosing { get; private set; }

        static void Main(string[] args)
        {
            Settings.Init();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            try
            {
                MyListener.Run();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            // Doesn't seem to trigger. Don't know what is the console application equivalent for OnExit.
            IsClosing = true;
        }
    }
}
