using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ChainMonitor.Helper;
using log4net;
using Newtonsoft.Json.Linq;
using ThinNeo;
using Zoro.Wallets;

namespace ChainMonitor
{
    public class NeoServer
    {
        private static List<TransactionInfo> neoTransRspList = new List<TransactionInfo>();
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void Start()
        {            
            ulong startHeight = Program.neoStartHeight;
            Logger.Info("Neo Watcher Start! Index: " + startHeight);
            while (true)
            {
                try
                {
                    var count = GetNeoHeight();
                    while (startHeight < count)
                    {
                        ParseNeoBlock(startHeight);
                        DbHelper.SaveIndex(startHeight, "neo");
                        startHeight += 1;

                        if (startHeight % 10 == 0)
                        {
                            Logger.Info("Parse NEO Height:" + startHeight);
                        }
                    }
                    Thread.Sleep(5000);

                }
                catch (Exception e)
                {
                    Logger.Error("neo: " + e.Message);
                    Logger.Error("stack: " + e.StackTrace);
                }
            }
        }

        public static ulong GetNeoHeight()
        {
            var url = Config._apiDict["neo"] + "?method=getblockcount&id=1&params=[]";
            var info = Helper.Helper.HttpGet(url);
            var json = JObject.Parse(info);
            var result = json["result"];
            ulong height = ulong.Parse(result.ToString());
            return height;
        }

        private static void ParseNeoBlock(ulong index)
        {
            var block = _getBlock(index);

            var txs = (JArray)block["tx"];
            for (int i = 0; i < txs.Count; i++)
            {
                var vout = (JArray)txs[i]["vout"];
                var txid = (string)txs[i]["txid"];

                if (neoTransRspList.Exists(x => x.txid == txid))
                    continue;

                for (int j = 0; j < vout.Count; j++)
                {
                    var address = (string)vout[j]["address"];

                    for (int k = 0; k < Config._neoAddrList.Count; k++)
                    {
                        if (address == Config._neoAddrList[k])
                        {
                            var assetId = (string)vout[j]["asset"];
                            var neoTrans = new TransactionInfo();
                            if (assetId == Config._nep5TokenHashDict["neo_neo"])
                                neoTrans.coinType = "neo";
                            if (assetId == Config._nep5TokenHashDict["neo_gas"])
                                neoTrans.coinType = "gas";
                            neoTrans.toAddress = address;
                            neoTrans.value = (decimal)vout[j]["value"];
                            neoTrans.confirmCount = 1;
                            neoTrans.height = index;
                            neoTrans.txid = txid;

                            neoTransRspList.Add(neoTrans);
                            Logger.Info(index + " Have A NEO Transaction To:" + address + "; Value:" + neoTrans.value + "; Txid:" + neoTrans.txid);
                        }
                    }
                }
            }

            //int utxoDataHeight = 0;
            if (neoTransRspList.Count > 0)
            {                
                TransSender.SendTransTimer(neoTransRspList);

                neoTransRspList.Clear();
            }
        }

        private static int GetNeoUtxoHeight()
        {
            WebClient wc = new WebClient();
            var url = Config._apiDict["nel"] + "?jsonrpc=2.0&id=1&method=getdatablockheight&params=[]";
            var info = wc.DownloadString(url);
            if (info.Contains("result") == false)
                return 0;
            var json = JObject.Parse(info);
            JArray result = json["result"] as JArray;
            return (int)result[0]["utxoDataHeight"];
        }

        static JToken _getBlock(ulong block)
        {
            WebClient wc = new WebClient();
            var getcounturl = Config._apiDict["neo"] + "?jsonrpc=2.0&id=1&method=getblock&params=[" + block + ",1]";
            var info = wc.DownloadString(getcounturl);
            var json = JObject.Parse(info);
            JToken result = json["result"];
            return result;
        }

        static JArray _getNotify(string txid)
        {
            WebClient wc = new WebClient();

            var getcounturl = Config._apiDict["neo"] + "?jsonrpc=2.0&id=1&method=getapplicationlog&params=[\"" + txid + "\"]";
            var info = wc.DownloadString(getcounturl);
            var json = JObject.Parse(info);
            if (json.ContainsKey("result") == false)
                return null;
            var result = (JObject)(json["result"]);
            var executions = (result["executions"][0]) as JObject;

            return executions["notifications"] as JArray;

        }
 
    }
}
