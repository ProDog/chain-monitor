using System.Collections.Generic;
using System.IO;
using System.Linq;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChainMonitor
{
    public class Config
    {
        public static Dictionary<string, int> _confirmCountDict;
        public static Dictionary<string, string> _apiDict;

        public static Dictionary<string, string> _nep5TokenHashDict;
        public static Dictionary<string, int> _nep5TokenDecimalDict;

        public static Dictionary<string, string> _erc20TokenHashDict;
        public static Dictionary<string, int> _erc20TokenDecimalDict;

        public static Dictionary<string, string> _neoTokenHashDict;

        public static string _destroyAddress;
        public static string _destroyAddressHexString { get; private set; }

        public static JObject ConfigJObject = null;

        public static List<string> _btcAddrList = new List<string>(); //BTC 监听地址列表
        public static HashSet<string> _ethAddrList = new HashSet<string>();  //ETH 监听地址列表
        public static List<string> _neoAddrList = new List<string>(); //NEO 监听地址          

        public const string _coinname = "bga";

        public static Network _nettype;

        public static string[] AllowIPs { get; set; }


        public static GameConfig _gameConfig;


        public static void Init(string configPath)
        {
            ConfigJObject = JObject.Parse(File.ReadAllText(configPath));

            _confirmCountDict = getIntDic("confirmCount");
            _apiDict = getStringDic("api");           
            
            _nep5TokenHashDict = getStringDic("zoroTokenHash");
            _nep5TokenDecimalDict = getIntDic("zoroTokenDecimal");
            _erc20TokenHashDict = getStringDic("erc20TokenHash");
            _erc20TokenDecimalDict = getIntDic("erc20TokenDecimal");
            _neoTokenHashDict = getStringDic("neoTokenHash");

            _destroyAddress = getValue("destroyAddress");
            _destroyAddressHexString = Helper.ZoroHelper.GetHexStringFromAddress(_destroyAddress);

            var net = (string)getValue("netType");
            if (net == "mainnet")
                _nettype = Network.Main;
            if (net == "testnet")
                _nettype = Network.TestNet;

            _ethAddrList = Helper.DbHelper.GetEthAddr();
            //btcAddrList = Helper.DbHelper.GetBtcAddr();
            //neoAddrList = Helper.DbHelper.GetNeoAddr();

            AllowIPs = ConfigJObject.GetValue("AllowIPs").Select(p => p.ToString()).ToArray();


            _gameConfig = new GameConfig();
            Dictionary<string,string> gameConfigDict = getStringDic("shConfig");

            _gameConfig.GameUrl = gameConfigDict["GameUrl"];
            _gameConfig.CollectionAddress = gameConfigDict["CollectionAddress"];
            _gameConfig.IssueAddress = gameConfigDict["IssueAddress"];

            _gameConfig.CollectionAddressHexString = Helper.ZoroHelper.GetHexStringFromAddress(_gameConfig.CollectionAddress);
            _gameConfig.IssueAddressHexString = Helper.ZoroHelper.GetHexStringFromAddress(_gameConfig.IssueAddress);
        }

        private static dynamic getValue(string name)
        {
            return ConfigJObject.GetValue(name);
        }

        private static Dictionary<string, int> getIntDic(string name)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, int>>(ConfigJObject[name].ToString());
        }

        private static Dictionary<string, string> getStringDic(string name)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(ConfigJObject[name].ToString());
        }

        public class GameConfig
        {
            public string GameUrl { get; set; }
            public string CollectionAddress { get; set; }
            public string CollectionAddressHexString { get; set; }
            public string IssueAddress { get; set; }
            public string IssueAddressHexString { get; set; }
        }

    }
}
