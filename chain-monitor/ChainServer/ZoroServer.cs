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
using Polly;
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
            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(retryCount: 100, sleepDurationProvider: aa => TimeSpan.FromSeconds(1), onRetry: (exception, aa, retryCount, Context) =>
                {
                    Logger.Warn($"Zoro error,retry count:{retryCount}, exception:{exception.Message} " + exception.StackTrace);
                });

            zoroHeight = Program.zoroStartHeight;

            Logger.Info("Zoro watcher start! index: " + zoroHeight);

            while (true)
            {
                policy.Execute(() =>
                {
                    uint blockCount = GetBlockCount();
                    while (zoroHeight < blockCount)
                    {
                        ParseBlock(zoroHeight);

                        DbHelper.SaveIndex(zoroHeight, "zoro");

                        if (zoroHeight % 10 == 0)
                            Logger.Info("zoro parse height:" + zoroHeight);

                        zoroHeight++;
                    }

                    Thread.Sleep(1000);
                });
            }
        }

        private static uint GetBlockCount()
        {
            WebClient wc = new WebClient();
            //try
            //{                
            var getcountUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getblockcount&params=['']";
            var info = wc.DownloadString(getcountUrl);
            wc.Dispose();

            var json = JObject.Parse(info);
            JToken result = json["result"];
            uint height = uint.Parse(result.ToString());
            return height;
            //}
            //catch (Exception ex)
            //{
            //wc.Dispose();
            //Logger.Error("zoro get block count error: " + ex.Message);
            //Logger.Error("stack: " + ex.StackTrace);
            //return 0;
            //}
        }

        private static void ParseBlock(ulong currentHeight)
        {
            WebClient wc = new WebClient();
            //try
            //{
            JObject blockInfo = GetBlock(wc, currentHeight);

            JArray txArray = blockInfo["tx"] as JArray;

            ulong blockTime = ulong.Parse(blockInfo["time"].ToString());
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
                    TransState transState = TransParse(wc, txArray[i], currentHeight, blockTime);
                    if (transState != null)
                        transStates.Add(transState);
                }
            }

            wc.Dispose();

            if (transStates.Count > 0)
            {
                blockState.TransStates = transStates;

                string jsonData = JsonConvert.SerializeObject(blockState);
                SendTransTimer(jsonData, Config._apiDict["refund"]);
            }
            //}
            //catch (Exception ex)
            //{
            //    wc.Dispose();
            //    Logger.Error("zoro: " + ex.Message);
            //    Logger.Error("stack: " + ex.StackTrace);
            //}
        }

        private static JObject GetBlock(WebClient wc, ulong currentHeight)
        {
            var getblockUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getblock&params=['',{currentHeight},1]";
            var info = wc.DownloadString(getblockUrl);
            var json = JObject.Parse(info)["result"] as JObject;

            return json;
        }

        private static TransState TransParse(WebClient wc, JToken tx, ulong currentHeight, ulong blockTime)
        {
            TransState transState = new TransState();
            List<WithdrawInfo> withdrawInfoList = new List<WithdrawInfo>();
            string txid = tx["txid"].ToString();

            var applicationlogInfo = GetApplicationlog(wc, txid);

            var executions = applicationlogInfo["executions"] as JToken;

            if (executions[0]["vmstate"].ToString() == "FAULT, BREAK")
            {
                return null;
            }

            var notifications = executions[0]["notifications"] as JArray;
            if (notifications == null || notifications.Count == 0) return null;

            for (int i = 0; i < notifications.Count; i++)
            {
                string contract = notifications[i]["contract"].ToString();

                //nep5
                foreach (var token in Config._nep5TokenHashDict)
                {
                    if (contract == token.Value)
                    {
                        var jValue = notifications[i]["state"]["value"] as JArray;
                        string method = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes(jValue[0]["value"].ToString()));

                        if (method == "transfer")
                        {
                            //游戏发奖 退款
                            if (jValue[1]["value"].ToString() == Config._gameConfig.IssueAddressHexString)
                            {
                                SendGameIssueResult(currentHeight, blockTime, txid, token.Key, jValue, method);
                                break;
                            }
                            //销毁 提币使用
                            if (jValue[2]["value"].ToString() == Config._destroyAddressHexString)
                            {
                                var withdrawInfo = GetTransferInfo(token.Key.ToUpper(), txid, jValue, tx);
                                if (withdrawInfo != null)
                                    withdrawInfoList.Add(withdrawInfo);
                                break;
                            }
                            //游戏中付款
                            else if (jValue[2]["value"].ToString() == Config._gameConfig.CollectionAddressHexString)
                            {
                                SendGameSpendResult(currentHeight, blockTime, txid, token.Key, jValue, method);
                                break;
                            }
                        }
                        break;
                    }
                }

                //nep10
                foreach (var token in Config._nftHashDict)
                {
                    if (contract == token.Value)
                    {
                        var jValue = notifications[i]["state"]["value"] as JArray;

                        string method = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes(jValue[0]["value"].ToString()));
                        dynamic transLog = null;
                        switch (method)
                        {
                            case "mintToken":
                                transLog = MintTokenResult(jValue);
                                break;
                            case "transfer":
                                transLog = TransferTokenResult(jValue);
                                break;
                            //case "modifyRwData":
                            //    transLog = ModifyRwDataResult(jValue);
                            //    break;
                            case "modifyProperties":
                                transLog = ModifyPropertiesResult(jValue);
                                break;
                            case "freeze":
                                transLog = FreezeTokenResult(jValue);
                                break;
                            case "unfreeze":
                                transLog = UnfreezeTokenResult(jValue);
                                break;
                        }

                        if (transLog != null && !string.IsNullOrEmpty((string)transLog.tokenId))
                        {
                            transLog.method = method;
                            transLog.txid = txid;
                            transLog.blockTime = blockTime;
                            transLog.blockHeight = zoroHeight;

                            string url = Config._gameConfig.GameUrl + "/sysGame/syncNFTInfo";
                            string jsonData = JsonConvert.SerializeObject(transLog);
                            SendNftTrans(jsonData, url);
                        }

                        break;
                    }
                }

                #region nftex
                //nft exchange
                //if (contract == Config._nftExchangeHash)
                //{
                //    var jValue = notifications[i]["state"]["value"] as JArray;
                //    string method = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes(jValue[0]["value"].ToString()));

                //    dynamic transLog = null;
                //    switch (method)
                //    {
                //        case "deposit":
                //            transLog = DepositResult(jValue);
                //            break;
                //        case "withdraw":
                //            transLog = WithdrawResult(jValue);
                //            break;
                //        case "makeOffer":
                //            transLog = MakeOfferResult(jValue);
                //            break;
                //        case "fillOffer":
                //            transLog = FillOfferResult(jValue);
                //            break;
                //        case "cancelOffer":
                //            transLog = CancelOffer(jValue);
                //            break;
                //    }

                //    if (transLog != null)
                //    {
                //        transLog.Method = method;
                //        transLog.Txid = txid;
                //        transLog.blockTime = blockTime;
                //        transLog.blockHeight = currentHeight;

                //        //string url = Config._gameConfig.GameUrl + "/sysGame/transConfirm";
                //        //string jsonData = JsonConvert.SerializeObject(transLog);
                //        //SendTransTimer(jsonData, url);
                //    }

                //    break;
                //}
                #endregion
            }

            if (withdrawInfoList.Count > 0)
            {
                transState.Txid = txid;
                transState.VmState = true;
                transState.Notifications = withdrawInfoList;

                return transState;
            }

            return null;
        }

        private static int tryCount = 0;
        private static void SendNftTrans(string jsonData, string url)
        {
            if (tryCount > 30)
            {
                Logger.Info("send fail:" + tryCount);
                tryCount = 0;
                return;
            }

            if (SendBlockTrans(jsonData, url))
            {
                Logger.Info("send over:" + tryCount);
                tryCount = 0;
            }
            else
            {
                tryCount++;
                SendNftTrans(jsonData, url);
            }
        }

        private static JObject GetApplicationlog(WebClient wc, string txid)
        {
            var getblockUrl = $"{Config._apiDict["zoro"]}/?jsonrpc=2.0&id=1&method=getapplicationlog&params=['','{txid}']";
            var info = wc.DownloadString(getblockUrl);
            var json = JObject.Parse(info)["result"] as JObject;

            return json;
        }

        private static void SendGameSpendResult(ulong currentHeight, ulong blockTime, string txid, string coinType, JArray jValue, string method)
        {
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

            Logger.Info("Zoro shgame receipt;Height:" + currentHeight + ", Address:" + address + ", CoinType:" + coinType + " , Value:" + amount + ", Txid: " + txid);

            string url = Config._gameConfig.GameUrl + "/sysGame/transConfirm";
            string jsonData = JsonConvert.SerializeObject(transferInfo);
            SendTransTimer(jsonData, url);
        }

        private static void SendGameIssueResult(ulong currentHeight, ulong blockTime, string txid, string coinType, JArray jValue, string method)
        {
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

            Logger.Info("Zoro shgame send;Height:" + currentHeight + ", Address:" + address + ", CoinType:" + coinType + " , Value:" + amount + ", Txid: " + txid);

            string url = Config._gameConfig.GameUrl + "/sysGame/transConfirm";
            string jsonData = JsonConvert.SerializeObject(transferInfo);
            SendTransTimer(jsonData, url);
        }

        private static WithdrawInfo GetTransferInfo(string coinType, string txid, JArray jValue, JToken tx)
        {
            var address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
            var amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

            if (((JArray)tx["attributes"]).Count > 0)
            {
                JObject attribute = (JObject)tx["attributes"][0];
                string usage = (string)attribute["usage"];
                string data = Encoding.UTF8.GetString(ZoroHelper.HexString2Bytes((string)attribute["data"]));
                if (usage != "Remark1")
                    return null;
                WithdrawInfo withdrawInfo = new WithdrawInfo();
                withdrawInfo.Method = "withdraw";
                withdrawInfo.FromAddress = address;
                withdrawInfo.ReceiveAddress = data;
                withdrawInfo.Value = amount;
                withdrawInfo.CoinType = coinType;

                Logger.Info("Zoro destroy: Address:" + withdrawInfo.FromAddress + ", CoinType:" + withdrawInfo.CoinType + " , Value:" + withdrawInfo.Value + ", Txid: " + txid);

                return withdrawInfo;
            }
            return null;
        }


        private static dynamic UnfreezeTokenResult(JArray jValue)
        {
            dynamic unfreezeLog = new ExpandoObject();
            unfreezeLog.tokenId = jValue[1]["value"].ToString();
            return unfreezeLog;
        }

        private static dynamic FreezeTokenResult(JArray jValue)
        {
            dynamic freezeLog = new ExpandoObject();
            freezeLog.tokenId = jValue[1]["value"].ToString();
            return freezeLog;
        }

        private static dynamic ModifyRwDataResult(JArray jValue)
        {
            dynamic modifyRwDataLog = new ExpandoObject();
            modifyRwDataLog.tokenId = jValue[1]["value"].ToString();
            modifyRwDataLog.rwData = jValue[2]["value"].ToString();
            return modifyRwDataLog;
        }

        private static dynamic ModifyPropertiesResult(JArray jValue)
        {
            dynamic modifyPropertiesLog = new ExpandoObject();
            modifyPropertiesLog.tokenId = jValue[1]["value"].ToString();
            modifyPropertiesLog.properties = jValue[2]["value"].ToString();
            return modifyPropertiesLog;
        }

        private static dynamic TransferTokenResult(JArray jValue)
        {
            dynamic transferTokenLog = new ExpandoObject();

            transferTokenLog.from = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
            transferTokenLog.to = ZoroHelper.GetJsonAddress((JObject)jValue[2]);
            transferTokenLog.tokenId = jValue[3]["value"].ToString();

            return transferTokenLog;
        }

        private static dynamic MintTokenResult(JArray jValue)
        {
            dynamic mintTokenLog = new ExpandoObject();

            mintTokenLog.address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
            mintTokenLog.tokenId = jValue[2]["value"].ToString();
            mintTokenLog.properties = jValue[3]["value"].ToString();

            return mintTokenLog;
        }


        #region nft ex
        private static dynamic CancelOffer(JArray notifications)
        {
            dynamic cancelOfferLog = new ExpandoObject();

            cancelOfferLog.OfferAddress = ZoroHelper.GetJsonAddress((JObject)notifications[1]);
            cancelOfferLog.OfferHash = notifications[2]["value"].ToString();
            cancelOfferLog.NftHash = ZoroHelper.GetJsonHash((JObject)notifications[3]);
            cancelOfferLog.TokenId = notifications[4]["value"].ToString();
            cancelOfferLog.OfferFeeAssetId = ZoroHelper.GetJsonHash((JObject)notifications[5]);
            cancelOfferLog.FeeReturnAmount = ZoroHelper.GetJsonDecimal((JObject)notifications[6], 8);
            cancelOfferLog.DeductFee = ZoroHelper.GetJsonDecimal((JObject)notifications[7], 8);
            return cancelOfferLog;
        }

        private static dynamic FillOfferResult(JArray notifications)
        {
            dynamic fillOfferLog = new ExpandoObject();

            fillOfferLog.FillAddress = ZoroHelper.GetJsonAddress((JObject)notifications[1]);
            fillOfferLog.OfferHash = notifications[2]["value"].ToString();
            fillOfferLog.OfferAddress = ZoroHelper.GetJsonAddress((JObject)notifications[3]);
            fillOfferLog.FillAssetId = ZoroHelper.GetJsonHash((JObject)notifications[4]);
            fillOfferLog.FillAmount = ZoroHelper.GetJsonDecimal((JObject)notifications[5], 8);
            fillOfferLog.NftHash = ZoroHelper.GetJsonHash((JObject)notifications[6]);
            fillOfferLog.TokenId = notifications[7]["value"].ToString();
            fillOfferLog.FillFeeAssetId = ZoroHelper.GetJsonHash((JObject)notifications[8]);
            fillOfferLog.FillFeeAmount = ZoroHelper.GetJsonDecimal((JObject)notifications[9], 8);
            fillOfferLog.OfferFeeAssetId = ZoroHelper.GetJsonHash((JObject)notifications[10]);
            fillOfferLog.OfferFeeAmount = ZoroHelper.GetJsonDecimal((JObject)notifications[11], 8);

            return fillOfferLog;
        }

        private static dynamic MakeOfferResult(JArray notifications)
        {
            dynamic makeOfferLog = new ExpandoObject();

            makeOfferLog.Address = ZoroHelper.GetJsonAddress((JObject)notifications[1]);
            makeOfferLog.OfferHash = notifications[2]["value"].ToString();
            makeOfferLog.NftContractHash = ZoroHelper.GetJsonHash((JObject)notifications[3]);
            makeOfferLog.TokenId = notifications[4]["value"].ToString();
            makeOfferLog.AcceptAssetId = ZoroHelper.GetJsonHash((JObject)notifications[5]);
            makeOfferLog.Price = ZoroHelper.GetJsonDecimal((JObject)notifications[6], 8);
            makeOfferLog.FeeAssetId = ZoroHelper.GetJsonHash((JObject)notifications[7]);
            makeOfferLog.FeeAmount = ZoroHelper.GetJsonDecimal((JObject)notifications[8], 8);

            return makeOfferLog;
        }

        private static dynamic WithdrawResult(JArray jValue)
        {
            dynamic withdrawLog = new ExpandoObject();

            withdrawLog.Address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
            withdrawLog.AssetId = ZoroHelper.GetJsonHash((JObject)jValue[2]);
            withdrawLog.Amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

            return withdrawLog;
        }

        private static dynamic DepositResult(JArray jValue)
        {
            dynamic depositLog = new ExpandoObject();

            depositLog.Address = ZoroHelper.GetJsonAddress((JObject)jValue[1]);
            depositLog.AssetId = ZoroHelper.GetJsonHash((JObject)jValue[2]);
            depositLog.Amount = ZoroHelper.GetJsonDecimal((JObject)jValue[3], 8);

            return depositLog;
        }
        #endregion

        public static void SendTransTimer(string jsonData, string url)
        {
            Timer _timer = new Timer(1500);
            int _retryCount = 0;

            _timer.Elapsed += (sender, e) =>
            {
                Logger.Info("_timer.Elapsed : ");
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
                    Logger.Info("发送成功");
                }
            };

            _timer.AutoReset = true;
            _timer.Enabled = true;

            Logger.Info("timer over");
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
                Stream reqStream = req.GetRequestStream();
                reqStream.Write(data, 0, data.Length);

                req.Timeout = 100000;

                HttpWebResponse resp = (HttpWebResponse)req.GetResponseAsync().Result;
                Stream stream = resp.GetResponseStream();

                Logger.Info("Zoro send transinfo : " + jsonData);

                StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                var result = reader.ReadToEnd();
                var rjson = JObject.Parse(result);

                if (req != null)
                    req.Abort();
                reqStream.Close();
                resp.Close();
                stream.Close();
                reader.Close();

                Logger.Warn("result:" + result);
                if (Convert.ToInt32(rjson["r"]) == 1 || Convert.ToInt32(rjson["res"]) == 1)
                {
                    return true;
                }
                else
                {
                    Logger.Warn("Zoro transinfo send fail:" + result);
                }

            }
            catch (Exception ex)
            {
                Logger.Error("Zoro transinfo send error:" + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);
            }

            // 調用失敗
            return false;
        }
    }
}
