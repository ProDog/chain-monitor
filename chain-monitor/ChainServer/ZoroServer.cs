using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using ChainMonitor.Helper;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;


namespace ChainMonitor
{
    //提币监控
    public class ZoroServer
    {
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static ulong zoroHeight;

        public static ulong GetZoroPraseHeight()
        {
            return zoroHeight;
        }

        public static ulong GetZoroBlockHeight()
        {
            return GetBlockCount();
        }

        public static void Start()
        {
            zoroHeight = Program.zoroStartHeight;

            Logger.Info("Zoro watcher start! index: " + zoroHeight);

            while (true)
            {
                uint blockCount = GetBlockCount();
                while (zoroHeight < blockCount)
                {
                    zoroHeight = ParseBlock(zoroHeight);
                }

                Thread.Sleep(1000);
            }
        }

        private static uint GetBlockCount()
        {
            try
            {
                WebClient wc = new WebClient();
                wc.Proxy = null;
                var getcountUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getblockcount&params=['']";
                var info = wc.DownloadString(getcountUrl);
                var json = JObject.Parse(info);
                JToken result = json["result"];
                uint height = uint.Parse(result.ToString());
                return height;
            }
            catch (Exception ex)
            {
                Logger.Error("zoro get block count error: " + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);
                return 0;
            }
        }

        private static ulong ParseBlock(ulong currentHeight)
        {
            try
            {
                JObject blockInfo = GetBlock(currentHeight);
                JArray txArray = blockInfo["tx"] as JArray;

                uint blockTime = uint.Parse(blockInfo["time"].ToString());
                BlockState blockState = new BlockState()
                {
                    BlockHeight = currentHeight,
                    BlockTime = blockTime,
                    ContractHash = Config._destroyAddress
                };
                List<TransState> transStates = new List<TransState>();

                for (int i = 0; i < txArray.Count; i++)
                {
                    if (txArray[i]["type"].ToString() == "InvocationTransaction")
                    {
                        TransState transState = TransParse(txArray[i], currentHeight, blockTime);
                        if (transState != null)
                            transStates.Add(transState);
                    }
                }
                if (transStates.Count > 0)
                {
                    blockState.TransStates = transStates;
                    //SendBlockTrans(blockState);
                    string jsonData = JsonConvert.SerializeObject(blockState);
                    SendTransTimer(jsonData, Config._apiDict["refund"]);
                }

                DbHelper.SaveIndex(currentHeight, "zoro");

                if (currentHeight % 10 == 0)
                    Logger.Info("zoro parse height:" + currentHeight);

                return currentHeight + 1;
            }
            catch (Exception ex)
            {
                Logger.Error("zoro: " + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);

                return currentHeight;
            }
        }

        private static JObject GetBlock(ulong currentHeight)
        {
            WebClient wc = new WebClient();

            var getblockUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getblock&params=['',{currentHeight},1]";
            var info = wc.DownloadString(getblockUrl);
            var json = JObject.Parse(info)["result"] as JObject;
            return json;
        }

        private static TransState TransParse(JToken tx, ulong currentHeight, uint blockTime)
        {
            TransState transState = new TransState();
            List<WithdrawInfo> withdrawInfoList = new List<WithdrawInfo>();
            string txid = tx["txid"].ToString();

            var applicationlogInfo = GetApplicationlog(txid);

            var executions = applicationlogInfo["executions"] as JToken;

            var notifications = executions[0]["notifications"] as JArray;
            if (notifications == null || notifications.Count == 0) return null;

            for (int i = 0; i < notifications.Count; i++)
            {
                string contract = notifications[i]["contract"].ToString();

                foreach (var token in Config._nep5TokenHashDict)
                {
                    if (contract == token.Value)
                    {
                        var jValue = notifications[i]["state"]["value"] as JArray;
                        string method = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes(jValue[0]["value"].ToString()));

                        //销毁 提币使用
                        if (method == "transfer")
                        {
                            if (jValue[2]["value"].ToString() == Config._destroyAddressHexString)
                            {
                                string coinType = token.Key.ToUpper();

                                var address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
                                var amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

                                if (((JArray)tx["attributes"]).Count > 0)
                                {
                                    JObject attribute = (JObject)tx["attributes"][0];
                                    string usage = (string)attribute["usage"];
                                    string data = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes((string)attribute["data"]));
                                    if (usage != "Remark1")
                                        continue;
                                    WithdrawInfo withdrawInfo = new WithdrawInfo();
                                    withdrawInfo.Method = "withdraw";
                                    withdrawInfo.FromAddress = address;
                                    withdrawInfo.ReceiveAddress = data;
                                    withdrawInfo.Value = amount;
                                    withdrawInfo.CoinType = coinType;
                                    withdrawInfoList.Add(withdrawInfo);

                                    Logger.Info("Zoro destroy: Height:" + currentHeight + ", Address:" + address + ", CoinType:" + coinType + " , Value:" + amount + ", Txid: " + txid);
                                }
                                break;
                            }

                            //游戏中付款
                            else if (jValue[2]["value"].ToString() == Config._gameConfig.CollectionAddressHexString)
                            {
                                string coinType = token.Key;

                                dynamic transferInfo = new ExpandoObject();

                                var address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
                                var amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

                                transferInfo.height = currentHeight;
                                transferInfo.blockTime = blockTime;
                                transferInfo.txid = txid;
                                transferInfo.coinType = coinType;
                                transferInfo.method = method;
                                transferInfo.from = address;
                                transferInfo.to = Config._gameConfig.CollectionAddress;
                                transferInfo.value = amount;

                                Logger.Info("Zoro sh game receipt;Height:" + currentHeight + ", Address:" + address + ", CoinType:" + coinType + " , Value:" + amount + ", Txid: " + txid);

                                string url = Config._gameConfig.GameUrl + "/sysGame/transConfirm";
                                string jsonData = JsonConvert.SerializeObject(transferInfo);
                                SendTransTimer(jsonData, url);
                            }

                            //发奖 退款
                            else if (jValue[1]["value"].ToString() == Config._gameConfig.IssueAddressHexString)
                            {
                                string coinType = token.Key;

                                dynamic transferInfo = new ExpandoObject();

                                var address = ZoroHelper.GetJsonAddress((JObject)jValue[2]);
                                var amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

                                transferInfo.height = currentHeight;
                                transferInfo.blockTime = blockTime;
                                transferInfo.txid = txid;
                                transferInfo.coinType = coinType;
                                transferInfo.method = method;
                                transferInfo.from = Config._gameConfig.IssueAddress;
                                transferInfo.to = address;
                                transferInfo.value = amount;

                                Logger.Info("Zoro sh game send;Height:" + currentHeight + ", Address:" + address + ", CoinType:" + coinType + " , Value:" + amount + ", Txid: " + txid);

                                string url = Config._gameConfig.GameUrl + "/sysGame/transConfirm";
                                string jsonData = JsonConvert.SerializeObject(transferInfo);
                                SendTransTimer(jsonData, url);
                            }
                        }
                    }
                }
            }

            if (withdrawInfoList.Count > 0)
            {
                transState.Txid = txid;
                if (executions[0]["vmstate"].ToString() == "FAULT, BREAK")
                {
                    transState.VmState = false;
                }
                else
                {
                    transState.VmState = true;
                    transState.Notifications = withdrawInfoList;
                }
                return transState;
            }
            return null;
        }

        private static JObject GetApplicationlog(string txid)
        {
            WebClient wc = new WebClient();

            var getblockUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getapplicationlog&params=['','{txid}']";
            var info = wc.DownloadString(getblockUrl);
            var json = JObject.Parse(info)["result"] as JObject;
            return json;
        }

        public static void SendTransTimer(string jsonData, string url)
        {
            Timer _timer = new Timer(1500);
            int _retryCount = 0;

            _timer.Elapsed += (sender, e) =>
            {
                if (!SendBlockTrans(jsonData, url))
                {
                    // 發送失敗
                    ++_retryCount;
                    if (_retryCount > 30) // 重試30次
                    {
                        // 超過重試次數，移除任務
                        _timer.Dispose();
                    }

                    // 未超重試次數，等待下次tickProcess重試
                }
                else
                {
                    // 發送成功
                    _timer.Dispose();
                }
            };

            _timer.AutoReset = true;
            _timer.Enabled = true;
        }


        private static bool SendBlockTrans(string jsonData, string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json;charset=utf-8";

                byte[] data = Encoding.Default.GetBytes(jsonData);
                req.ContentLength = data.Length;
                using (Stream reqStream = req.GetRequestStream())
                {
                    reqStream.Write(data, 0, data.Length);
                    reqStream.Close();
                }

                req.Timeout = 100000;

                HttpWebResponse resp = (HttpWebResponse)req.GetResponseAsync().Result;
                Stream stream = resp.GetResponseStream();

                Logger.Info("Zoro send transinfo : " + jsonData);

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var result = reader.ReadToEnd();
                    var rjson = JObject.Parse(result);

                    if (Convert.ToInt32(rjson["r"]) == 1 || Convert.ToInt32(rjson["res"]) == 1)
                    {
                        // 成功
                        return true;
                    }
                    else
                    {
                        Logger.Warn("Zoro transinfo send fail:" + result);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Zoro transinfo send error:" + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);

                // TO DO : 重要：永遠不要在異常處理中執行功能代碼！！！！
                //SendBlockTrans(blockState);
            }

            // 調用失敗
            return false;
        }
    }
}
