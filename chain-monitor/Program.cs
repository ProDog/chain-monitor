using System;
using log4net;
using System.Reflection;
using log4net.Config;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ChainMonitor
{
    public class Program
    {
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public static string configPath = "config.json";

        public static ulong neoStartHeight;
        public static ulong btcStartHeight;
        public static ulong ethStartHeight;
        public static ulong zoroStartHeight;

        static void Main(string[] args)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetExecutingAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo(@"log4net.config"));
            GlobalContext.Properties["pname"] = Assembly.GetEntryAssembly().GetName().Name;
            GlobalContext.Properties["pid"] = Process.GetCurrentProcess().Id;
            Console.OutputEncoding = Encoding.UTF8;      

            Helper.DbHelper.CreateDb();

            Config.Init(configPath);

            //neoStartHeight = Helper.DbHelper.GetIndex("neo");
            //btcStartHeight = Helper.DbHelper.GetIndex("btc");
            ethStartHeight = Helper.DbHelper.GetIndex("eth");
            zoroStartHeight = Helper.DbHelper.GetIndex("zoro");

            if (args.Length == 2)
            {
                //neoStartHeight = uint.Parse(args[0]);
                //btcStartHeight = uint.Parse(args[1]);
                ethStartHeight = ulong.Parse(args[0]);
                zoroStartHeight = ulong.Parse(args[1]);
            }         

            Thread ethThread = new Thread(EthServer.Start);
            ethThread.Start();

            Thread zoroThread = new Thread(ZoroServer.Start);
            zoroThread.Start();

            //Thread btcThread = new Thread(BtcServer.Start);
            //btcThread.Start();

            //Thread neoThread = new Thread(NeoServer.Start);
            //neoThread.Start();

            Task.Factory.StartNew(() => HttpServer.Start());

            Logger.Info("Chain monitor start. ");

            while (true)
            {
                Thread.Sleep(1000);
            }
        }

    }
}
